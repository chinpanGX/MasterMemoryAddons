using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>CSV の行を、コンパイルしたテーブルの型(client 用 / server 用)のインスタンスに変換する。</summary>
internal static class TableRowConverter
{
    /// <summary>
    /// 行を <paramref name="rowType"/> の <c>List&lt;T&gt;</c> に変換する。対象にない列(反対側の Only の列)は読み飛ばす。
    /// エラーがあれば <paramref name="errors"/> に足し、null を返す。
    /// </summary>
    public static IList? Convert(
        CsvTable table,
        TableSchema schema,
        Type rowType,
        IReadOnlyList<EnumSchema> enums,
        List<CsvError> errors)
    {
        var file = table.Files[0];
        if (rowType.GetConstructor(Type.EmptyTypes) is null)
        {
            errors.Add(new CsvError(file, null, null, $"{rowType.FullName} は引数なしで作れません(引数なしのコンストラクタを用意してください)。"));
            return null;
        }

        var columns = new List<(PropertyInfo Property, int Index, ColumnSchema Column, EnumSchema? Enum)>();
        foreach (var property in rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var column = schema.Columns.FirstOrDefault(c => c.Name == property.Name);
            var index = IndexOf(table.Header, property.Name);
            if (column is null || index < 0)
            {
                errors.Add(new CsvError(file, null, property.Name,
                    $"生成ソースの {rowType.Name}.{property.Name} が、定義または CSV のヘッダにありません。code-gen をやり直してください。"));
                return null;
            }

            var enumSchema = property.PropertyType.IsEnum
                ? enums.FirstOrDefault(e => e.ClrFullName == property.PropertyType.FullName)
                : null;
            columns.Add((property, index, column, enumSchema));
        }

        var count = errors.Count;
        var rows = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        // MasterMemory の Build() は主キーの重複を検出しないので、ここで検出する(複合主キーは列の値の組で比べる)
        var keys = new Dictionary<string, CsvRow>(StringComparer.Ordinal);
        var keyColumns = columns.Where(c => c.Column.IsPrimaryKey).ToList();
        foreach (var csvRow in table.Rows)
        {
            var row = Activator.CreateInstance(rowType)!;
            var key = new List<string>();
            var ok = true;
            foreach (var (property, index, column, enumSchema) in columns)
            {
                if (CellConverter.TryConvert(csvRow.Values[index], property.PropertyType, column.IsPrimaryKey, enumSchema,
                        out var value, out var error))
                {
                    // init のプロパティも、リフレクションなら設定できる
                    property.SetValue(row, value);
                    if (column.IsPrimaryKey)
                    {
                        key.Add($"{column.Name}={value}");
                    }
                }
                else
                {
                    errors.Add(new CsvError(csvRow.File, csvRow.Line, column.Name, error));
                    ok = false;
                }
            }

            if (ok && keyColumns.Count > 0)
            {
                // 値に `,` を含む文字列の主キーでも取り違えないよう、比べるときは制御文字で区切る
                var keyId = string.Join("\u001F", key);
                if (keys.TryGetValue(keyId, out var first))
                {
                    errors.Add(new CsvError(csvRow.File, csvRow.Line, null,
                        $"主キー ({string.Join(", ", key)}) が重複しています(最初の行: {first.File} {first.Line}行)。"));
                }
                else
                {
                    keys[keyId] = csvRow;
                }
            }

            rows.Add(row);
        }

        return errors.Count == count ? rows : null;
    }

    private static int IndexOf(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
