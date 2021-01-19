﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.Extensions.ContextQuery;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Completion.Providers
{
    internal abstract partial class AbstractKeywordCompletionProvider<TContext> : LSPCompletionProvider
        where TContext : SyntaxContext
    {
        private readonly ImmutableArray<IKeywordRecommender<TContext>> _keywordRecommenders;

        protected AbstractKeywordCompletionProvider(
            ImmutableArray<IKeywordRecommender<TContext>> keywordRecommenders)
        {
            _keywordRecommenders = keywordRecommenders;
        }

        private class Comparer : IEqualityComparer<CompletionItem>
        {
            public bool Equals(CompletionItem x, CompletionItem y)
                => x.DisplayText == y.DisplayText;

            public int GetHashCode(CompletionItem obj)
                => Hash.Combine(obj.DisplayText.GetHashCode(), obj.DisplayText.GetHashCode());
        }

        private static readonly Comparer s_comparer = new();

        public override async Task ProvideCompletionsAsync(CompletionContext context)
        {
            var cancellationToken = context.CancellationToken;

            using (Logger.LogBlock(FunctionId.Completion_KeywordCompletionProvider_GetItemsWorker, cancellationToken))
            {
                context.AddItems(await context.Document.GetUnionItemsFromDocumentAndLinkedDocumentsAsync(
                    s_comparer,
                    d => RecommendCompletionItemsAsync(d, context.Position, cancellationToken),
                    cancellationToken).ConfigureAwait(false));
            }
        }

        private async Task<ImmutableArray<CompletionItem>> RecommendCompletionItemsAsync(Document document, int position, CancellationToken cancellationToken)
        {
            var semanticModel = await document.ReuseExistingSpeculativeModelAsync(position, cancellationToken).ConfigureAwait(false);
            var contextService = document.GetRequiredLanguageService<ISyntaxContextService>();
            var syntaxContext = (TContext)contextService.CreateContext(document.Project.Solution.Workspace, semanticModel, position, cancellationToken);
            var keywords = await RecommendKeywordsAsync(document, position, syntaxContext, cancellationToken).ConfigureAwait(false);
            return keywords.NullToEmpty().SelectAsArray(k => CreateItem(k, syntaxContext));
        }

        protected static ImmutableArray<string> s_Tags = ImmutableArray.Create(WellKnownTags.Intrinsic);

        protected static CompletionItemRules s_keywordRules = CompletionItemRules.Default;

        protected abstract CompletionItem CreateItem(RecommendedKeyword keyword, TContext context);

        private async Task<ImmutableArray<RecommendedKeyword>> RecommendKeywordsAsync(
            Document document,
            int position,
            TContext context,
            CancellationToken cancellationToken)
        {
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var syntaxFacts = document.GetLanguageService<ISyntaxFactsService>();
            if (syntaxFacts.IsInNonUserCode(syntaxTree, position, cancellationToken))
                return default;

            using var _ = ArrayBuilder<RecommendedKeyword>.GetInstance(out var set);
            foreach (var recommender in _keywordRecommenders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var keywords = await recommender.RecommendKeywordsAsync(position, context, cancellationToken).ConfigureAwait(false);
                if (keywords != null)
                    set.AddRange(keywords);
            }

            set.RemoveDuplicates();
            return set.ToImmutable();
        }

        public override Task<TextChange?> GetTextChangeAsync(Document document, CompletionItem item, char? ch, CancellationToken cancellationToken)
            => Task.FromResult((TextChange?)new TextChange(item.Span, item.DisplayText));
    }
}
