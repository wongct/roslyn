﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;

namespace Microsoft.CodeAnalysis;

internal partial class SolutionCompilationState
{
    private interface ICompilationTracker
    {
        ProjectState ProjectState { get; }
        GeneratorDriver? GeneratorDriver { get; }

        SkeletonReferenceCache SkeletonReferenceCache { get; }

        /// <summary>
        /// Returns <see langword="true"/> if this <see cref="Project"/>/<see cref="Compilation"/> could produce the
        /// given <paramref name="symbol"/>.  The symbol must be a <see cref="IAssemblySymbol"/>, <see
        /// cref="IModuleSymbol"/> or <see cref="IDynamicTypeSymbol"/>.
        /// </summary>
        /// <remarks>
        /// If <paramref name="primary"/> is true, then <see cref="Compilation.References"/> will not be considered
        /// when answering this question.  In other words, if <paramref name="symbol"/>  is an <see
        /// cref="IAssemblySymbol"/> and <paramref name="primary"/> is <see langword="true"/> then this will only
        /// return true if the symbol is <see cref="Compilation.Assembly"/>.  If <paramref name="primary"/> is
        /// false, then it can return true if <paramref name="symbol"/> is <see cref="Compilation.Assembly"/> or any
        /// of the symbols returned by <see cref="Compilation.GetAssemblyOrModuleSymbol(MetadataReference)"/> for
        /// any of the references of the <see cref="Compilation.References"/>.
        /// </remarks>
        bool ContainsAssemblyOrModuleOrDynamic(ISymbol symbol, bool primary, out MetadataReferenceInfo? referencedThrough);

        ICompilationTracker Fork(ProjectState newProject, TranslationAction? translate);

        Task<Compilation> GetCompilationAsync(SolutionCompilationState compilationState, CancellationToken cancellationToken);

        ICompilationTracker FreezePartialState(CancellationToken cancellationToken);

        Task<VersionStamp> GetDependentVersionAsync(SolutionCompilationState compilationState, CancellationToken cancellationToken);
        Task<VersionStamp> GetDependentSemanticVersionAsync(SolutionCompilationState compilationState, CancellationToken cancellationToken);
        Task<Checksum> GetDependentChecksumAsync(SolutionCompilationState compilationState, CancellationToken cancellationToken);

        MetadataReference? GetPartialMetadataReference(ProjectState fromProject, ProjectReference projectReference);
        ValueTask<TextDocumentStates<SourceGeneratedDocumentState>> GetSourceGeneratedDocumentStatesAsync(SolutionCompilationState compilationState, CancellationToken cancellationToken);
        ValueTask<ImmutableArray<Diagnostic>> GetSourceGeneratorDiagnosticsAsync(SolutionCompilationState compilationState, CancellationToken cancellationToken);

        Task<bool> HasSuccessfullyLoadedAsync(SolutionCompilationState compilationState, CancellationToken cancellationToken);
        bool TryGetCompilation([NotNullWhen(true)] out Compilation? compilation);
        SourceGeneratedDocumentState? TryGetSourceGeneratedDocumentStateForAlreadyGeneratedId(DocumentId documentId);
    }
}
