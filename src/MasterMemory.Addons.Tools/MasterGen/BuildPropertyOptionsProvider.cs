using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>Source Generator に build_property.* を渡す(通常のビルドでは MSBuild が渡すもの)。</summary>
internal sealed class BuildPropertyOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly DictionaryAnalyzerConfigOptions globalOptions;

    public BuildPropertyOptionsProvider(IReadOnlyDictionary<string, string> globalValues)
    {
        globalOptions = new DictionaryAnalyzerConfigOptions(globalValues);
    }

    public override AnalyzerConfigOptions GlobalOptions => globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return DictionaryAnalyzerConfigOptions.Empty;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return DictionaryAnalyzerConfigOptions.Empty;
    }
}
