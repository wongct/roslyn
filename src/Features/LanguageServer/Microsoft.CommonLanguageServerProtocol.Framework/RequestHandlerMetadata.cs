﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// This is consumed as 'generated' code in a source package and therefore requires an explicit nullable enable
#nullable enable

using System;

namespace Microsoft.CommonLanguageServerProtocol.Framework;
internal record RequestHandlerMetadata(string MethodName, Type? RequestType, Type? ResponseType, string Language)
{
    internal string HandlerDescription { get; } = $"{MethodName} ({Language})";
}
