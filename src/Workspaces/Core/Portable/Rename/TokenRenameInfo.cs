﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Rename;

internal sealed class TokenRenameInfo(bool hasSymbols, IEnumerable<ISymbol> symbols, bool isMemberGroup)
{
    public bool HasSymbols { get; private set; } = hasSymbols;
    public IEnumerable<ISymbol> Symbols { get; private set; } = symbols;
    public bool IsMemberGroup { get; private set; } = isMemberGroup;

    public static TokenRenameInfo CreateMemberGroupTokenInfo(IEnumerable<ISymbol> symbols)
    {
        return new TokenRenameInfo
        (
            hasSymbols: true,
            isMemberGroup: true,
            symbols: symbols
        );
    }

    public static TokenRenameInfo CreateSingleSymbolTokenInfo(ISymbol symbol)
    {
        return new TokenRenameInfo
        (
            hasSymbols: true,
            isMemberGroup: false,
            symbols: SpecializedCollections.SingletonEnumerable(symbol)
        );
    }

    public static TokenRenameInfo NoSymbolsTokenInfo = new
    (
        hasSymbols: false,
        isMemberGroup: false,
        symbols: SpecializedCollections.EmptyEnumerable<ISymbol>()
    );
}
