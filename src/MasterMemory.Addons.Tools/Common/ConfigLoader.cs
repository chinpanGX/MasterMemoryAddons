using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>設定ファイル(YAML)を読む。形式は docs/design/config.md。</summary>
public static class ConfigLoader
{
    public static ToolsConfig Load(string path, string? environment)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ConfigException($"設定ファイルが見つかりません: {fullPath}");
        }

        return Parse(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!, environment);
    }

    /// <param name="yaml">設定ファイルの内容。</param>
    /// <param name="baseDirectory">相対パスの基準(設定ファイルのディレクトリ)。</param>
    /// <param name="environment">適用する環境名。null なら基本の設定だけを使う。</param>
    public static ToolsConfig Parse(string yaml, string baseDirectory, string? environment)
    {
        var document = Deserialize(yaml);
        var baseFull = Path.GetFullPath(baseDirectory);

        if (string.IsNullOrWhiteSpace(document.Definitions))
        {
            throw new ConfigException("definitions は必須です。");
        }

        if (document.Outputs is null || document.Outputs.Count == 0)
        {
            throw new ConfigException("outputs に出力先を1つ以上書いてください。");
        }

        // 基本の設定の出力先を検証する(上書きの前に、基本の設定だけで完結している必要がある)
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < document.Outputs.Count; i++)
        {
            var output = document.Outputs[i] ?? throw new ConfigException($"outputs[{i}] が空です。");
            if (string.IsNullOrWhiteSpace(output.Name))
            {
                throw new ConfigException($"outputs[{i}].name は必須です。");
            }

            if (!names.Add(output.Name))
            {
                throw new ConfigException($"outputs の name が重複しています: {output.Name}");
            }

            if (string.IsNullOrWhiteSpace(output.Target))
            {
                throw new ConfigException($"outputs[{output.Name}].target は必須です。");
            }

            if (string.IsNullOrWhiteSpace(output.Source))
            {
                throw new ConfigException($"outputs[{output.Name}].source は必須です。");
            }
        }

        var overrides = SelectEnvironment(document, environment);

        var definitions = overrides?.Definitions ?? document.Definitions;
        var overriddenOutputs = overrides?.Outputs ?? new Dictionary<string, OutputOverrideDocument?>();

        foreach (var name in overriddenOutputs.Keys)
        {
            if (!names.Contains(name))
            {
                throw new ConfigException(
                    $"environments.{environment}.outputs の {name} は、outputs にない出力先です"
                    + $"(あるもの: {string.Join(", ", names)})。");
            }
        }

        var outputs = new List<OutputConfig>();
        // null の要素は上で弾いている
        foreach (var output in document.Outputs.Select(o => o!))
        {
            overriddenOutputs.TryGetValue(output.Name!, out var outputOverride);
            // 出力先が client か server かは、環境によらず同じとする(namespace や encrypt の意味が変わってしまうため)
            if (outputOverride?.Target is not null)
            {
                throw new ConfigException(
                    $"environments.{environment}.outputs.{output.Name}.target: target は環境で上書きできません"
                    + "(出力先が client か server かは、すべての環境で同じにしてください)。");
            }

            var targetText = output.Target;
            var source = outputOverride?.Source ?? output.Source;

            if (!GenerationTargets.TryParse(targetText, out var target))
            {
                throw new ConfigException(
                    $"outputs[{output.Name}].target は client または server を指定してください: {targetText}");
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ConfigException($"outputs[{output.Name}].source が空です。");
            }

            var @namespace = outputOverride?.Namespace ?? output.Namespace;
            ValidateNamespace(output.Name!, @namespace);

            var binary = outputOverride?.Binary ?? output.Binary;
            if (binary is not null && string.IsNullOrWhiteSpace(binary))
            {
                throw new ConfigException($"outputs[{output.Name}].binary が空です。");
            }

            // 暗号化はクライアント用だけの想定(サーバーのバイナリは利用者に配らない)
            var encrypt = outputOverride?.Encrypt ?? output.Encrypt ?? false;
            if (encrypt && target != GenerationTarget.Client)
            {
                throw new ConfigException(
                    $"outputs[{output.Name}].encrypt は target が client のときだけ true にできます(サーバーのバイナリは暗号化しない想定です)。");
            }

            outputs.Add(new OutputConfig(
                output.Name!,
                target,
                Resolve(baseFull, source),
                @namespace,
                binary is null ? null : Resolve(baseFull, binary),
                encrypt));
        }

        if (string.IsNullOrWhiteSpace(definitions))
        {
            throw new ConfigException("definitions が空です。");
        }

        var csv = overrides?.Csv ?? document.Csv;
        if (csv is not null && string.IsNullOrWhiteSpace(csv))
        {
            throw new ConfigException("csv が空です。");
        }

        return new ToolsConfig(
            baseFull,
            Resolve(baseFull, definitions),
            outputs,
            csv is null ? null : Resolve(baseFull, csv));
    }

    private static ConfigDocument Deserialize(string yaml)
    {
        // 未知の項目は例外にする(打ち間違いや、まだない項目を黙って無視しない)
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        try
        {
            return deserializer.Deserialize<ConfigDocument?>(yaml)
                ?? throw new ConfigException("設定ファイルが空です。");
        }
        catch (YamlException ex)
        {
            var reason = ex.InnerException?.Message ?? ex.Message;

            // YamlDotNet の "Property 'x' not found on type '...'" を、利用者向けの表現にする
            var unknown = Regex.Match(reason, "^Property '(?<name>[^']+)' not found on type");
            if (unknown.Success)
            {
                reason = $"{unknown.Groups["name"].Value} は設定できない項目です(書ける項目は docs/design/config.md を参照)。";
            }

            throw new ConfigException($"設定ファイルを読めません({ex.Start.Line}行 {ex.Start.Column}列): {reason}");
        }
    }

    private static EnvironmentDocument? SelectEnvironment(ConfigDocument document, string? environment)
    {
        if (environment is null)
        {
            return null;
        }

        if (document.Environments is null || !document.Environments.TryGetValue(environment, out var selected))
        {
            var known = document.Environments is null ? "なし" : string.Join(", ", document.Environments.Keys);
            throw new ConfigException($"environments に {environment} がありません(あるもの: {known})。");
        }

        // "production:" のように中身を書かない環境は、上書きなしとして扱う
        return selected ?? new EnvironmentDocument();
    }

    private static string Resolve(string baseDirectory, string path)
    {
        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    // MasterDataSchema は client / server の両方に出すので、namespace はどちらでも必須
    private static void ValidateNamespace(string name, string? @namespace)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            throw new ConfigException(
                $"outputs[{name}].namespace は必須です(MasterDataSchema を出力する名前空間)。");
        }

        if (!IsValidNamespace(@namespace))
        {
            throw new ConfigException($"outputs[{name}].namespace が C# の名前空間として正しくありません: {@namespace}");
        }
    }

    private static bool IsValidNamespace(string @namespace)
    {
        return @namespace.Split('.').All(s =>
            SyntaxFacts.IsValidIdentifier(s) && SyntaxFacts.GetKeywordKind(s) == SyntaxKind.None);
    }

    // ---- YAML の形(YamlDotNet が値を入れる) ----

    private sealed class ConfigDocument
    {
        public string? Definitions { get; set; }

        public string? Csv { get; set; }

        public List<OutputDocument?>? Outputs { get; set; }

        public Dictionary<string, EnvironmentDocument?>? Environments { get; set; }
    }

    private sealed class OutputDocument
    {
        public string? Name { get; set; }

        public string? Target { get; set; }

        public string? Source { get; set; }

        public string? Namespace { get; set; }

        public string? Binary { get; set; }

        public bool? Encrypt { get; set; }
    }

    private sealed class EnvironmentDocument
    {
        public string? Definitions { get; set; }

        public string? Csv { get; set; }

        public Dictionary<string, OutputOverrideDocument?>? Outputs { get; set; }
    }

    private sealed class OutputOverrideDocument
    {
        // 上書きできないが、未知の項目のエラーより分かりやすいエラーにするため読む
        public string? Target { get; set; }

        public string? Source { get; set; }

        public string? Namespace { get; set; }

        public string? Binary { get; set; }

        public bool? Encrypt { get; set; }
    }
}
