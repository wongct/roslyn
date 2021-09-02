﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.CodeRefactorings.PullMemberUp;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.MoveStaticMembers
{
    internal class MoveStaticMembersWithDialogCodeAction : CodeActionWithOptions
    {
        private readonly Document _document;
        private readonly ISymbol? _selectedMember;
        private readonly INamedTypeSymbol _selectedType;
        private readonly IMoveStaticMembersOptionsService _service;

        public TextSpan Span { get; }
        public override string Title => FeaturesResources.Move_static_members_to_another_type;

        public MoveStaticMembersWithDialogCodeAction(
            Document document,
            TextSpan span,
            IMoveStaticMembersOptionsService service,
            INamedTypeSymbol selectedType,
            ISymbol? selectedMember = null)
        {
            _document = document;
            _service = service;
            _selectedType = selectedType;
            _selectedMember = selectedMember;
            Span = span;
        }

        public override object? GetOptions(CancellationToken cancellationToken)
        {
            return _service.GetMoveMembersToTypeOptions(_document, _selectedType, _selectedMember);
        }

        protected override async Task<IEnumerable<CodeActionOperation>> ComputeOperationsAsync(object options, CancellationToken cancellationToken)
        {
            if (options is not MoveStaticMembersOptions moveOptions || moveOptions.IsCancelled)
            {
                return SpecializedCollections.EmptyEnumerable<CodeActionOperation>();
            }

            // Find the original doc root
            var syntaxFacts = _document.GetRequiredLanguageService<ISyntaxFactsService>();
            var root = await _document.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            // add annotations to the symbols that we selected so we can find them later to pull up
            // These symbols should all have (singular) definitions, but in the case that we can't find
            // any location, we just won't move that particular symbol
            var memberNodes = moveOptions.SelectedMembers
                .Select(symbol => symbol.Locations.FirstOrDefault())
                .WhereNotNull()
                .SelectAsArray(loc => loc.FindNode(cancellationToken));
            root = root.TrackNodes(memberNodes);
            var sourceDoc = _document.WithSyntaxRoot(root);

            var typeParameters = ExtractTypeHelpers.GetRequiredTypeParametersForMembers(_selectedType, moveOptions.SelectedMembers);
            // which indices of the old type params should we keep for a new class reference, used for refactoring usages
            var typeArgIndices = Enumerable.Range(0, _selectedType.TypeParameters.Length)
                .Where(i => typeParameters.Contains(_selectedType.TypeParameters[i]))
                .ToImmutableArrayOrEmpty();

            // even though we can move members here, we will move them by calling PullMembersUp
            var newType = CodeGenerationSymbolFactory.CreateNamedTypeSymbol(
                ImmutableArray.Create<AttributeData>(),
                Accessibility.NotApplicable,
                DeclarationModifiers.Static,
                GetNewTypeKind(_selectedType),
                moveOptions.TypeName,
                typeParameters: typeParameters);

            var (newDoc, annotation) = await ExtractTypeHelpers.AddTypeToNewFileAsync(
                sourceDoc.Project.Solution,
                moveOptions.NamespaceDisplay,
                moveOptions.FileName,
                _document.Project.Id,
                _document.Folders,
                newType,
                _document,
                cancellationToken).ConfigureAwait(false);

            // get back type declaration in the newly created file
            var destRoot = await newDoc.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var destSemanticModel = await newDoc.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            newType = destSemanticModel.GetRequiredDeclaredSymbol(destRoot.GetAnnotatedNodes(annotation).Single(), cancellationToken) as INamedTypeSymbol;

            // refactor references across the entire solution
            var memberReferenceLocations = await FindMemberReferencesAsync(moveOptions.SelectedMembers, newDoc.Project.Solution, cancellationToken).ConfigureAwait(false);
            var projectToLocations = memberReferenceLocations.ToLookup(loc => loc.location.Document.Project.Id);
            var solutionWithFixedReferences = await RefactorReferencesAsync(projectToLocations, newDoc.Project.Solution, newType!, typeArgIndices, cancellationToken).ConfigureAwait(false);

            sourceDoc = solutionWithFixedReferences.GetRequiredDocument(sourceDoc.Id);

            // get back nodes from our changes
            root = await sourceDoc.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await sourceDoc.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var members = memberNodes
                .Select(node => root.GetCurrentNode(node))
                .WhereNotNull()
                .SelectAsArray(node => (semanticModel.GetDeclaredSymbol(node!, cancellationToken), false));

            var pullMembersUpOptions = PullMembersUpOptionsBuilder.BuildPullMembersUpOptions(newType!, members);
            var movedSolution = await MembersPuller.PullMembersUpAsync(sourceDoc, pullMembersUpOptions, cancellationToken).ConfigureAwait(false);

            return new CodeActionOperation[] { new ApplyChangesOperation(movedSolution) };
        }

        /// <summary>
        /// Finds what type kind new type should be. Currently, we just select whatever type the source is.
        /// This means always a class for C#, and a module for VB iff we moved from a module
        /// This functionality can later be expanded or moved to language-specific implementations
        /// </summary>
        private static TypeKind GetNewTypeKind(INamedTypeSymbol oldType)
        {
            return oldType.TypeKind;
        }

        private static async Task<Solution> RefactorReferencesAsync(
            ILookup<ProjectId, (ReferenceLocation location, bool isExtensionMethod)> projectToLocations,
            Solution solution,
            INamedTypeSymbol newType,
            ImmutableArray<int> typeArgIndices,
            CancellationToken cancellationToken)
        {
            // keep our new solution separate, since each change can be performed separately
            var updatedSolution = solution;
            foreach (var (projectId, referencesForProject) in projectToLocations)
            {
                // organize by project first, so we can solve one project at a time
                var project = solution.GetRequiredProject(projectId);
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                var documentToLocations = referencesForProject.ToLookup(reference => reference.location.Document.Id);
                foreach (var (docId, referencesForDoc) in documentToLocations)
                {
                    var doc = project.GetRequiredDocument(docId);
                    var updatedRoot = await FixReferencesSingleDocumentAsync(
                        referencesForDoc.ToImmutableArray(),
                        doc,
                        newType,
                        typeArgIndices,
                        cancellationToken).ConfigureAwait(false);

                    updatedSolution = updatedSolution.WithDocumentSyntaxRoot(docId, updatedRoot);
                }

                // We keep the compilation until we are done with the project
                GC.KeepAlive(compilation);
            }

            return updatedSolution;
        }

        private static async Task<SyntaxNode> FixReferencesSingleDocumentAsync(
            ImmutableArray<(ReferenceLocation location, bool isExtensionMethod)> referenceLocations,
            Document doc,
            INamedTypeSymbol newType,
            ImmutableArray<int> typeArgIndices,
            CancellationToken cancellationToken)
        {
            var syntaxFacts = doc.GetRequiredLanguageService<ISyntaxFactsService>();

            // keep extension method flag attached to node through dict
            var trackNodesDict = referenceLocations
                .ToImmutableDictionary(refLoc => refLoc.location.Location.FindNode(
                    getInnermostNodeForTie: true,
                    cancellationToken));

            var docEditor = await DocumentEditor.CreateAsync(doc, cancellationToken).ConfigureAwait(false);
            var generator = docEditor.Generator;

            foreach (var refNode in trackNodesDict.Keys)
            {
                var (_, isExtensionMethod) = trackNodesDict[refNode];

                // now change the actual references to use the new type name, add a symbol annotation
                // for every reference we move so that if an import is necessary/possible,
                // we add it, and simplifiers so we don't over-qualify after import
                if (isExtensionMethod)
                {
                    // extension methods should be changed into their static class versions with
                    // full qualifications, then the qualification changed to the new type
                    if (syntaxFacts.IsNameOfAnyMemberAccessExpression(refNode) &&
                        syntaxFacts.IsAnyMemberAccessExpression(refNode?.Parent) &&
                        syntaxFacts.IsInvocationExpression(refNode.Parent?.Parent))
                    {
                        // get the entire expression, guaranteed not null based on earlier checks
                        var extensionMethodInvocation = refNode.GetRequiredParent().GetRequiredParent();
                        // expand using our (possibly outdated) document/syntaxes
                        var expandedExtensionInvocation = await Simplifier.ExpandAsync(
                            extensionMethodInvocation,
                            doc,
                            cancellationToken: cancellationToken).ConfigureAwait(false);

                        // should be an invocation of a simple member access expression with the expression as a type name
                        var memberAccessExpression = syntaxFacts.GetExpressionOfInvocationExpression(expandedExtensionInvocation);
                        var typeExpression = syntaxFacts.GetExpressionOfMemberAccessExpression(memberAccessExpression)!;
                        expandedExtensionInvocation = expandedExtensionInvocation.ReplaceNode(typeExpression, generator.TypeExpression(newType)
                            .WithTriviaFrom(refNode)
                            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation, SymbolAnnotation.Create(newType)));

                        docEditor.ReplaceNode(extensionMethodInvocation, expandedExtensionInvocation);
                    }
                }
                else if (syntaxFacts.IsNameOfSimpleMemberAccessExpression(refNode))
                {
                    // static member access should never be pointer or conditional member access,
                    // so syntax in this block should be of the form 'Class.Member' or 'Class<TArg>.Member'
                    var expression = syntaxFacts.GetExpressionOfMemberAccessExpression(refNode.Parent);
                    if (expression != null)
                    {
                        SyntaxNode replacement;
                        if (syntaxFacts.IsGenericName(expression))
                        {
                            // if the access uses a generic name, then we copy only the type args we need
                            var typeArgs = syntaxFacts.GetTypeArgumentsOfGenericName(expression);
                            var newTypeArgs = typeArgIndices.SelectAsArray(i => typeArgs[i]);
                            replacement = generator.GenericName(newType.Name, newTypeArgs);
                        }
                        else
                        {
                            replacement = generator.TypeExpression(newType);
                        }

                        docEditor.ReplaceNode(expression, replacement
                            .WithTriviaFrom(refNode)
                            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation, SymbolAnnotation.Create(newType)));
                    }
                }
                else if (syntaxFacts.IsIdentifierName(refNode))
                {
                    // We now are in an identifier name that isn't a member access expression
                    // This could either be because of a static using, module usage in VB, or because we are in the original source type
                    // either way, we want to change it to a member access expression for the type that is imported
                    docEditor.ReplaceNode(
                        refNode,
                        generator.MemberAccessExpression(
                            generator.TypeExpression(newType)
                                .WithAdditionalAnnotations(Simplifier.AddImportsAnnotation, SymbolAnnotation.Create(newType)),
                            refNode));
                }
            }

            return docEditor.GetChangedRoot();
        }

        private static async Task<ImmutableArray<(ReferenceLocation location, bool isExtension)>> FindMemberReferencesAsync(
            ImmutableArray<ISymbol> members,
            Solution solution,
            CancellationToken cancellationToken)
        {
            var tasks = members.Select(symbol => SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken));
            var symbolRefs = await Task.WhenAll(tasks).ConfigureAwait(false);
            return symbolRefs
                .Flatten()
                .SelectMany(refSymbol => refSymbol.Locations
                    .Where(loc => !loc.IsCandidateLocation && !loc.IsImplicit)
                    .Select(loc => (loc, refSymbol.Definition.IsExtensionMethod())))
                .ToImmutableArrayOrEmpty();
        }
    }
}
