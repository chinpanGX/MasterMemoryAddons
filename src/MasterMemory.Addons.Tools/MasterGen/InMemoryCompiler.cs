using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using MasterMemory.Addons.Tools.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>
/// コード生成の出力をメモリ上でコンパイルし、読み込む。MasterMemory / MessagePack の Source Generator もその場で動かす。
/// 方式は docs/design/master-gen.md の「コンパイル」。参照と C# のバージョンは target ごとに分ける(「参照」)。
/// </summary>
internal static class InMemoryCompiler
{
    // 生成される DatabaseBuilder などの名前空間。Tools がリフレクションで探すだけなので、利用者の名前とは関係ない
    /// <summary>MasterMemory の Source Generator が DatabaseBuilder などを置く名前空間(コンパイルの RootNamespace)。</summary>
    public const string RootNamespace = "MasterMemory.Addons.MasterGen.Generated";

    // server 用のコンパイルで参照する、Tools の出力先にある DLL(バイナリはこのバージョンで作られる)
    private static readonly string[] PackageAssemblies =
    {
        "MasterMemory.dll",
        "MasterMemory.Annotations.dll",
        "MessagePack.dll",
        "MessagePack.Annotations.dll",
    };

    // client は Unity と同じ C# 9 で読む。server は固定しない
    private static readonly CSharpParseOptions ClientParseOptions = new(LanguageVersion.CSharp9);

    private static readonly CSharpParseOptions ServerParseOptions = new(LanguageVersion.Latest);

    // init を netstandard2.1 で使うための polyfill。Unity では利用者が置く(生成ソースには含まれない)
    private const string IsExternalInitSource = """
        namespace System.Runtime.CompilerServices
        {
            internal static class IsExternalInit
            {
            }
        }
        """;

    private static readonly Lazy<IReadOnlyList<MetadataReference>> ClientReferences = new(LoadClientReferences);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> ServerReferences = new(LoadServerReferences);

    private static readonly Lazy<IReadOnlyList<ISourceGenerator>> Generators = new(LoadGenerators);

    public static CompilationResult Compile(IReadOnlyList<GeneratedFile> sources, GenerationTarget target)
    {
        var client = target == GenerationTarget.Client;
        var parseOptions = client ? ClientParseOptions : ServerParseOptions;
        var trees = sources
            .Select(f => CSharpSyntaxTree.ParseText(f.Text, parseOptions, f.RelativePath))
            .ToList();
        if (client && !sources.Any(f => f.Text.Contains("class IsExternalInit", StringComparison.Ordinal)))
        {
            trees.Add(CSharpSyntaxTree.ParseText(IsExternalInitSource, parseOptions, "<IsExternalInit>"));
        }

        var compilation = CSharpCompilation.Create(
            RootNamespace,
            trees,
            client ? ClientReferences.Value : ServerReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            Generators.Value,
            parseOptions: parseOptions,
            optionsProvider: new BuildPropertyOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.RootNamespace"] = RootNamespace,
            }));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        var errors = generatorDiagnostics
            .Concat(emit.Diagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(Format)
            .Distinct()
            .ToList();
        if (!emit.Success || errors.Count > 0)
        {
            return new CompilationResult(null, errors);
        }

        // MasterMemory / MessagePack は Tools と同じもの(既定のコンテキスト)を使う
        stream.Position = 0;
        var context = new AssemblyLoadContext(RootNamespace, isCollectible: true);
        return new CompilationResult(context.LoadFromStream(stream), Array.Empty<string>());
    }

    private static string Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var location = span.IsValid ? $"{span.Path} {span.StartLinePosition.Line + 1}行: " : "";
        return $"{location}{diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    // netstandard2.1 の参照アセンブリ + MasterMemory / MessagePack の netstandard 版(ビルド時に references/client へコピーしたもの)
    private static IReadOnlyList<MetadataReference> LoadClientReferences()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "references", "client");
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"client 用のコンパイルの参照が見つかりません: {directory}");
        }

        return Directory.EnumerateFiles(directory, "*.dll")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f))
            .ToList();
    }

    // 実行環境の .NET の共有フレームワーク(Microsoft.NETCore.App)+ MasterMemory / MessagePack。
    // Roslyn・YamlDotNet・Tools・Core などは参照しない
    private static IReadOnlyList<MetadataReference> LoadServerReferences()
    {
        var framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => string.Equals(Path.GetDirectoryName(p), framework, StringComparison.OrdinalIgnoreCase));
        return platform
            .Concat(PackageAssemblies.Select(f => Path.Combine(AppContext.BaseDirectory, f)))
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(g => (MetadataReference)MetadataReference.CreateFromFile(g.First()))
            .ToList();
    }

    private static IReadOnlyList<ISourceGenerator> LoadGenerators()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "generators");
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Source Generator の DLL が見つかりません: {directory}");
        }

        var loader = new GeneratorAssemblyLoader();
        return Directory.EnumerateFiles(directory, "*.dll")
            .OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(path => new AnalyzerFileReference(path, loader).GetGenerators(LanguageNames.CSharp))
            .ToList();
    }
}
