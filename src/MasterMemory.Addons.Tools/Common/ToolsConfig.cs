using System.Collections.Generic;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// 設定ファイルに環境の上書きを適用し、パスを絶対パスに解決したもの。
/// <see cref="Csv"/> は CSV のディレクトリで、書いていなければ null(master-gen では必須)。
/// </summary>
public sealed record ToolsConfig(
    string ConfigDirectory,
    string Definitions,
    IReadOnlyList<OutputConfig> Outputs,
    string? Csv = null);
