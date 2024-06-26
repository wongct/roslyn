﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis;

internal readonly struct UnmergedDocumentChanges(ImmutableArray<TextChange> unmergedChanges, string projectName, DocumentId documentId)
{
    public readonly ImmutableArray<TextChange> UnmergedChanges = unmergedChanges;
    public readonly string ProjectName = projectName;
    public readonly DocumentId DocumentId = documentId;
}
