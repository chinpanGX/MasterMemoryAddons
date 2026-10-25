using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>キーと値の組を、Source Generator に渡す設定値として見せる。</summary>
internal sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
{
    public static readonly DictionaryAnalyzerConfigOptions Empty = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> values;

    public DictionaryAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
    {
        this.values = values;
    }

    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        return values.TryGetValue(key, out value);
    }
}
