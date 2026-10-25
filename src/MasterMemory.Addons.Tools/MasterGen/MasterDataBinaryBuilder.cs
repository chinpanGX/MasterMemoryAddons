using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>
/// コンパイルしたアセンブリの <c>DatabaseBuilder</c>(MasterMemory の Source Generator が生成したもの)を、リフレクションで使う。
/// </summary>
internal static class MasterDataBinaryBuilder
{
    private const string MemoryTableAttributeName = "MasterMemory.MemoryTableAttribute";

    /// <summary>テーブル名(<c>[MemoryTable]</c> の名前)→ テーブルの型。</summary>
    public static Dictionary<string, Type> FindTables(Assembly assembly)
    {
        var tables = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes())
        {
            var attribute = type.GetCustomAttributes(inherit: false)
                .FirstOrDefault(a => a.GetType().FullName == MemoryTableAttributeName);
            if (attribute is null)
            {
                continue;
            }

            var name = (string)attribute.GetType().GetProperty("TableName")!.GetValue(attribute)!;
            tables[name] = type;
        }

        return tables;
    }

    /// <summary>行を <c>DatabaseBuilder</c> に追加し、<c>Build()</c> したバイナリを返す。</summary>
    public static byte[] Build(Assembly assembly, IEnumerable<(Type RowType, IList Rows)> tables)
    {
        // 定義に同じ名前の型があっても取り違えないよう、名前空間まで含めて探す
        var builderType = assembly.GetType($"{InMemoryCompiler.RootNamespace}.DatabaseBuilder")
            ?? throw new InvalidOperationException("DatabaseBuilder が生成されていません([MemoryTable] の型がありません)。");
        var builder = Activator.CreateInstance(builderType)!;

        foreach (var (rowType, rows) in tables)
        {
            var append = builderType.GetMethod("Append", new[] { typeof(IEnumerable<>).MakeGenericType(rowType) })
                ?? throw new InvalidOperationException($"DatabaseBuilder.Append({rowType.Name}) がありません。");
            Invoke(append, builder, rows);
        }

        var build = builderType.GetMethod("Build", Type.EmptyTypes)
            ?? throw new InvalidOperationException("DatabaseBuilder.Build() がありません。");
        return (byte[])Invoke(build, builder)!;
    }

    /// <summary>
    /// <paramref name="bytes"/>(<see cref="Build"/> の結果)から <c>MemoryDatabase</c> を作り、MasterMemory の <c>Validate()</c> を呼ぶ。
    /// 検証に失敗すれば、その内容(<c>FormatFailedResults()</c>)を返す。成功なら null。
    /// </summary>
    public static string? Validate(Assembly assembly, byte[] bytes)
    {
        var databaseType = assembly.GetType($"{InMemoryCompiler.RootNamespace}.MemoryDatabase")
            ?? throw new InvalidOperationException("MemoryDatabase が生成されていません。");
        // MemoryDatabase(byte[] databaseBinary, bool internString = true, IFormatterResolver? formatterResolver = null, int maxDegreeOfParallelism = 1)
        var database = Activator.CreateInstance(databaseType, bytes, true, null, 1)!;
        var validate = databaseType.GetMethod("Validate", Type.EmptyTypes)
            ?? throw new InvalidOperationException("MemoryDatabase.Validate() がありません。");
        var result = Invoke(validate, database)!;
        var failed = (bool)result.GetType().GetProperty("IsValidationFailed")!.GetValue(result)!;
        return failed ? (string?)Invoke(result.GetType().GetMethod("FormatFailedResults", Type.EmptyTypes)!, result) : null;
    }

    // リフレクションの呼び出しで出た例外は、元の例外にして投げ直す(メッセージを利用者に見せるため)
    private static object? Invoke(MethodInfo method, object target, params object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
