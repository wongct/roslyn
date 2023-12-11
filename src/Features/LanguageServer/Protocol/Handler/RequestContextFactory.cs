﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommonLanguageServerProtocol.Framework;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.LanguageServer.Handler;

internal class RequestContextFactory : AbstractRequestContextFactory<RequestContext>, ILspService
{
    private readonly ILspServices _lspServices;

    public RequestContextFactory(ILspServices lspServices)
    {
        _lspServices = lspServices;
    }

    public override Task<RequestContext> CreateRequestContextAsync<TRequestParam>(IQueueItem<RequestContext> queueItem, IMethodHandler methodHandler, TRequestParam requestParam, CancellationToken cancellationToken)
    {
        var clientCapabilitiesManager = _lspServices.GetRequiredService<IInitializeManager>();
        var clientCapabilities = clientCapabilitiesManager.TryGetClientCapabilities();
        var logger = _lspServices.GetRequiredService<AbstractLspLogger>();
        var serverInfoProvider = _lspServices.GetRequiredService<ServerInfoProvider>();

        if (clientCapabilities is null && queueItem.MethodName != Methods.InitializeName)
        {
            throw new InvalidOperationException($"ClientCapabilities was null for a request other than {Methods.InitializeName}.");
        }

        var textDocumentIdentifier = queueItem.RequestUri is null ? null : new TextDocumentIdentifier
        {
            Uri = queueItem.RequestUri,
        };

        bool requiresLSPSolution;
        if (methodHandler is ISolutionRequiredHandler requiredHandler)
        {
            requiresLSPSolution = requiredHandler.RequiresLSPSolution;
        }
        else
        {
            throw new InvalidOperationException($"{nameof(IMethodHandler)} implementation {methodHandler.GetType()} does not implement {nameof(ISolutionRequiredHandler)}");
        }

        return RequestContext.CreateAsync(
            methodHandler.MutatesSolutionState,
            requiresLSPSolution,
            textDocumentIdentifier,
            serverInfoProvider.ServerKind,
            clientCapabilities,
            serverInfoProvider.SupportedLanguages,
            _lspServices,
            logger,
            queueItem.MethodName,
            cancellationToken);
    }
}
