using System.Collections.Generic;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>CSV のデータ行1つ。<see cref="File"/> は CSV のディレクトリからの相対パス、<see cref="Line"/> は行の番号。</summary>
public sealed record CsvRow(string File, int Line, IReadOnlyList<string> Values);
