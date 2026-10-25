using System.Collections.Generic;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>定義ディレクトリ全体を1つの対象向けに変換した結果。ファイルには書き込まない。</summary>
public sealed class GenerationResult
{
    public List<GeneratedFile> Files { get; } = new();

    /// <summary>この対象に残る型がなく、出力しないファイルの相対パス。</summary>
    public List<string> Omitted { get; } = new();

    public List<GenerationError> Errors { get; } = new();

    public int SourceCount => Files.Count + Omitted.Count + Errors.Count;
}
