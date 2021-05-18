﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Adapts an ISourceGenerator to an incremental generator that
    /// by providing an execution environment that matches the old one
    /// </summary>
    internal sealed class SourceGeneratorAdaptor : IIncrementalGenerator
    {
        internal ISourceGenerator SourceGenerator { get; }

        public SourceGeneratorAdaptor(ISourceGenerator generator)
        {
            SourceGenerator = generator;
        }

        public void Initialize(IncrementalGeneratorInitializationContext initContext)
        {
            GeneratorInitializationContext generatorInitContext = new GeneratorInitializationContext(initContext.CancellationToken);
            SourceGenerator.Initialize(generatorInitContext);

            initContext.InfoBuilder.PostInitCallback = generatorInitContext.InfoBuilder.PostInitCallback;
            var syntaxContextReceiverCreator = generatorInitContext.InfoBuilder.SyntaxContextReceiverCreator;

            initContext.RegisterExecutionPipeline((executionContext) =>
            {
                var contextBuilderSource = executionContext.Sources.Compilation
                                            .Transform(c => new GeneratorContextBuilder(c))
                                            .Join(executionContext.Sources.ParseOptions).Transform(p => p.Item1 with { ParseOptions = p.Item2.FirstOrDefault() })
                                            .Join(executionContext.Sources.AnalyzerConfigOptions).Transform(p => p.Item1 with { ConfigOptions = p.Item2.FirstOrDefault() })
                                            .Join(executionContext.Sources.AdditionalTexts).Transform(p => p.Item1 with { AdditionalTexts = p.Item2.ToImmutableArray() });

                if (syntaxContextReceiverCreator is object)
                {
                    contextBuilderSource = contextBuilderSource
                                           .Join(executionContext.Sources.Syntax.CreateSyntaxReceiverInput(syntaxContextReceiverCreator))
                                           .Transform(p => p.Item1 with { Receiver = p.Item2.FirstOrDefault() });
                }

                var output = contextBuilderSource.GenerateSource((productionContext, contextBuilder) =>
                {
                    var generatorExecutionContext = contextBuilder.ToExecutionContext(productionContext.CancellationToken);

                    // PROTOTYPE(source-generators):If this throws, we'll wrap it in a user func as expected. We probably *shouldn't* do that for the rest of this code though
                    // So we probably need an internal version that doesn't wrap it? Maybe we can just construct the nodes manually.
                    SourceGenerator.Execute(generatorExecutionContext);

                    // copy the contents of the old context to the new
                    generatorExecutionContext.CopyToProductionContext(productionContext);
                    generatorExecutionContext.Free();
                });
                executionContext.RegisterOutput(output);
            });
        }

        internal record GeneratorContextBuilder(Compilation Compilation)
        {
            public ParseOptions? ParseOptions;

            public ImmutableArray<AdditionalText> AdditionalTexts;

            public Diagnostics.AnalyzerConfigOptionsProvider? ConfigOptions;

            public ISyntaxContextReceiver? Receiver;

            public GeneratorExecutionContext ToExecutionContext(CancellationToken cancellationToken)
            {
                Debug.Assert(ParseOptions is object && ConfigOptions is object);
                return new GeneratorExecutionContext(Compilation, ParseOptions, AdditionalTexts, ConfigOptions, Receiver, cancellationToken);
            }
        }
    }
}
