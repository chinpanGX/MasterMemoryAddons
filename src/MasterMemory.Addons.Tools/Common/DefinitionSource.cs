using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// 定義のファイル1つ分。定義のディレクトリからの相対パスと、パースした構文木。
/// 定義は1回だけパースし、target ごとのコード生成と、マスタ生成の定義の読み込み(<see cref="DefinitionSchemaReader"/>)で共有する。
/// </summary>
public sealed record DefinitionSource(string RelativePath, SyntaxTree Tree)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>定義のソースの文字列をパースする。</summary>
    public static DefinitionSource Parse(string relativePath, string text)
    {
        return new DefinitionSource(relativePath, CSharpSyntaxTree.ParseText(text, ParseOptions));
    }

    /// <summary>
    /// <paramref name="definitionsDirectory"/> 以下の <c>*.cs</c> を、パスの順に読んでパースする。
    /// <c>bin</c> / <c>obj</c> と、<paramref name="excludedDirectories"/>(出力先が定義の中にある場合など。絶対パス)の中は読まない。
    /// </summary>
    public static List<DefinitionSource> Load(string definitionsDirectory, IEnumerable<string> excludedDirectories)
    {
        var excluded = excludedDirectories.ToList();
        return Directory.EnumerateFiles(definitionsDirectory, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (FullPath: f, RelativePath: Path.GetRelativePath(definitionsDirectory, f)))
            .Where(f => !IsExcluded(f.FullPath, f.RelativePath, excluded))
            .Select(f => Parse(f.RelativePath, File.ReadAllText(f.FullPath)))
            .ToList();
    }

    private static bool IsExcluded(string fullPath, string relativePath, List<string> excludedDirectories)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                              || s.Equals("obj", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 出力先が入力の中にある場合、生成物を再処理しない
        return excludedDirectories.Any(d => PathUtility.IsSameOrUnder(fullPath, d));
    }
}
