using System;
using System.Collections.Generic;
using System.Linq;
using MasterMemory.Addons.Tools.Common;

namespace MasterMemory.Addons.Tools.MasterGen;

/// <summary>
/// データ段の検証(docs/design/pipeline.md の「検証の項目」)。定義の <c>[Unique]</c> / <c>[Exists]</c> を、CSV の値で確かめる。
/// client / server に分ける前の全カラム(CSV)に対して1回だけ行う。
/// </summary>
public static class DataValidator
{
    public static void Validate(DefinitionSchema schema, IReadOnlyDictionary<string, CsvTable> tables, List<CsvError> errors)
    {
        // 参照先の列の値は、同じ列を何度も参照してもまとめて1回だけ集める
        var referenced = new Dictionary<(string Table, string Column), HashSet<string>?>();
        foreach (var table in schema.Tables)
        {
            if (!tables.TryGetValue(table.TableName, out var csv))
            {
                continue;
            }

            foreach (var column in table.Columns)
            {
                var index = IndexOf(csv.Header, column.Name);
                var kind = ColumnValue.KindOf(schema, table, column);
                if (index < 0 || kind is null)
                {
                    continue;
                }

                if (column.IsUnique)
                {
                    ValidateUnique(csv, column, index, kind, schema, errors);
                }

                if (column.Exists is { } rule && FindTarget(schema, table, rule) is { } found)
                {
                    var (target, targetColumn) = found;
                    var key = (target.TableName, targetColumn.Name);
                    if (!referenced.TryGetValue(key, out var values))
                    {
                        values = CollectValues(schema, tables, target, targetColumn);
                        referenced[key] = values;
                    }

                    if (values is not null)
                    {
                        ValidateExists(csv, column, index, kind, rule, target, targetColumn, values, schema, errors);
                    }
                }
            }
        }
    }

    private static void ValidateUnique(CsvTable csv, ColumnSchema column, int index, string kind, DefinitionSchema schema, List<CsvError> errors)
    {
        var first = new Dictionary<string, CsvRow>(StringComparer.Ordinal);
        foreach (var row in csv.Rows)
        {
            var raw = row.Values[index];
            if (!ColumnValue.TryNormalize(raw, kind, schema, out var value))
            {
                continue;
            }

            if (first.TryGetValue(value, out var previous))
            {
                errors.Add(new CsvError(row.File, row.Line, column.Name,
                    $"[Unique] の値 '{raw}' が重複しています(最初は {previous.File} {previous.Line}行)。"));
            }
            else
            {
                first[value] = row;
            }
        }
    }

    private static void ValidateExists(
        CsvTable csv,
        ColumnSchema column,
        int index,
        string kind,
        ExistsRule rule,
        TableSchema target,
        ColumnSchema targetColumn,
        HashSet<string> values,
        DefinitionSchema schema,
        List<CsvError> errors)
    {
        foreach (var row in csv.Rows)
        {
            var raw = row.Values[index];
            if ((raw.Length == 0 && rule.AllowEmpty) || !ColumnValue.TryNormalize(raw, kind, schema, out var value))
            {
                continue;
            }

            if (!values.Contains(value))
            {
                var shown = raw.Length == 0 ? "(空欄)" : $"'{raw}'";
                errors.Add(new CsvError(row.File, row.Line, column.Name,
                    $"[Exists] の値 {shown} が、{target.TableName} の {targetColumn.Name} にありません。"));
            }
        }
    }

    // 参照先のテーブルと列(定義の誤りは DefinitionSchemaReader が表示するので、ここでは見つからなければ null)
    private static (TableSchema Table, ColumnSchema Column)? FindTarget(DefinitionSchema schema, TableSchema table, ExistsRule rule)
    {
        var target = schema.FindTable(table, rule.TableTypeName);
        var column = target is null
            ? null
            : rule.Column is null
                ? target.Columns.SingleOrDefault(c => c.IsPrimaryKey)
                : target.Columns.FirstOrDefault(c => c.Name == rule.Column);
        return target is not null && column is not null ? (target, column) : null;
    }

    // 参照先の列の値。参照先の CSV が読めなければ null(そのエラーは CSV の段で表示している)
    private static HashSet<string>? CollectValues(
        DefinitionSchema schema,
        IReadOnlyDictionary<string, CsvTable> tables,
        TableSchema target,
        ColumnSchema targetColumn)
    {
        var kind = ColumnValue.KindOf(schema, target, targetColumn);
        if (!tables.TryGetValue(target.TableName, out var csv) || kind is null)
        {
            return null;
        }

        var index = IndexOf(csv.Header, targetColumn.Name);
        if (index < 0)
        {
            return null;
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in csv.Rows)
        {
            if (ColumnValue.TryNormalize(row.Values[index], kind, schema, out var value))
            {
                values.Add(value);
            }
        }

        return values;
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
