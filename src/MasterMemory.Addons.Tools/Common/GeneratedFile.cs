namespace MasterMemory.Addons.Tools.Common;

/// <summary>生成した1ファイル分。<see cref="RelativePath"/> は定義ディレクトリからの相対パス。</summary>
public sealed record GeneratedFile(string RelativePath, string Text);
