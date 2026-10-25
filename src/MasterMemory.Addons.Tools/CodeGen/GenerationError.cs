namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>定義のエラー(構文エラー、テーブルの型が record など)で変換できなかった1ファイル分。</summary>
public sealed record GenerationError(string RelativePath, string Message);
