namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// 出力先1つ分。<see cref="Source"/> と <see cref="Binary"/> は絶対パス。
/// <see cref="Namespace"/> は MasterDataSchema を出力する名前空間(必須)。
/// <see cref="Binary"/> はバイナリのパスで、書いていなければ null(master-gen の対象にしない)。
/// <see cref="Encrypt"/> は client のときだけ true にできる。
/// </summary>
public sealed record OutputConfig(
    string Name,
    GenerationTarget Target,
    string Source,
    string? Namespace = null,
    string? Binary = null,
    bool Encrypt = false);
