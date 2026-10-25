using System;
using System.Collections.Generic;
using System.Linq;
using MasterMemory.Addons.Tools.Common;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>
/// 設定の出力先ごとに、コード生成の結果(出力先に置くファイル)をメモリ上で作る。ファイルには書き込まない。
/// code-gen と、生成ソースが古いときに作り直す master-gen が使う(docs/design/master-gen.md の「生成ソースが古いとき」)。
/// </summary>
public static class ConfigCodeGenerator
{
    public static ConfigCodeGenerationResult Generate(ToolsConfig config)
    {
        // 定義は1回だけパースし、target ごとのコード生成と、マスタ生成の定義の読み込みで共有する
        var sources = DefinitionSource.Load(config.Definitions, Array.Empty<string>());
        var results = new Dictionary<GenerationTarget, GenerationResult>();
        foreach (var target in config.Outputs.Select(o => o.Target).Distinct())
        {
            results[target] = SourceGenerator.Generate(sources, target);
        }

        // 定義のエラーは対象に関係なく同じなので、ファイルごとに1回だけ返す
        var syntaxErrors = results.Values
            .SelectMany(r => r.Errors)
            .GroupBy(e => e.RelativePath)
            .Select(g => $"{g.Key}: 定義のエラー{Environment.NewLine}{g.First().Message}")
            .ToList();
        var sourceCount = sources.Count;
        if (syntaxErrors.Count > 0)
        {
            return new ConfigCodeGenerationResult(Array.Empty<OutputFiles>(), syntaxErrors, null, sourceCount, sources);
        }

        // MasterDataSchema は client / server の両方に出す。値は target ごとの生成ソースから計算する
        var schemaHashes = new Dictionary<GenerationTarget, string>();
        foreach (var (target, result) in results)
        {
            var conflict = result.Files.FirstOrDefault(f =>
                string.Equals(f.RelativePath, MasterDataSchemaGenerator.FileName, StringComparison.OrdinalIgnoreCase));
            if (conflict is not null)
            {
                return new ConfigCodeGenerationResult(
                    Array.Empty<OutputFiles>(),
                    Array.Empty<string>(),
                    $"{conflict.RelativePath}: 生成する {MasterDataSchemaGenerator.FileName} と同じ名前です。定義のファイル名を変えてください。",
                    sourceCount,
                    sources);
            }

            schemaHashes[target] = MasterDataSchemaGenerator.Compute(result.Files);
        }

        var outputs = new List<OutputFiles>();
        foreach (var output in config.Outputs)
        {
            var result = results[output.Target];
            var schemaHash = schemaHashes[output.Target];
            if (result.Files.FirstOrDefault(f => DeclaresSchemaType(f.Text, output.Namespace!)) is { } declaring)
            {
                return new ConfigCodeGenerationResult(
                    Array.Empty<OutputFiles>(),
                    Array.Empty<string>(),
                    $"{declaring.RelativePath}: outputs[{output.Name}] の namespace({output.Namespace})に、生成する MasterDataSchema と同じ名前の型があります。"
                    + "型の名前か名前空間を変えてください。",
                    sourceCount,
                    sources);
            }

            // namespace は ConfigLoader で必須項目として確認している
            var schemaFile = new GeneratedFile(
                MasterDataSchemaGenerator.FileName,
                MasterDataSchemaGenerator.Generate(output.Namespace!, schemaHash));
            outputs.Add(new OutputFiles(output, result.Files.Append(schemaFile).ToList(), schemaHash, result.Omitted.Count));
        }

        return new ConfigCodeGenerationResult(outputs, Array.Empty<string>(), null, sourceCount, sources);
    }

    // 生成ソースが、名前空間 ns の直下に MasterDataSchema 型を宣言しているか(同じ名前空間に2つあるとコンパイルエラー CS0101 になる)
    private static bool DeclaresSchemaType(string text, string ns)
    {
        // ほとんどのファイルはパースせずに済ませる
        if (!text.Contains(MasterDataSchemaGenerator.TypeName, StringComparison.Ordinal))
        {
            return false;
        }

        return CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Any(t => t.Identifier.ValueText == MasterDataSchemaGenerator.TypeName
                      && t.Parent is not BaseTypeDeclarationSyntax
                      && DefinitionSchemaReader.NamespaceOf(t) == ns);
    }
}
