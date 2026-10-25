using System;
using System.Globalization;
using System.Linq;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// データ段の検証(<c>[Unique]</c> / <c>[Exists]</c>)で、CSV のセルを比べるための値を作る。
/// 生成ソースをコンパイルする前に、定義の型名(構文のまま)だけで変換する。
/// </summary>
public static class ColumnValue
{
    /// <summary>
    /// 列の値の種類。<c>int</c> と <c>long</c> は同じ種類(数値)として比べる。enum は <c>enum:完全名</c>。
    /// CSV で使えない型なら null。
    /// </summary>
    public static string? KindOf(DefinitionSchema schema, TableSchema table, ColumnSchema column)
    {
        switch (column.TypeName)
        {
            case "int":
            case "long":
            case "Int32":
            case "Int64":
            case "System.Int32":
            case "System.Int64":
                return "number";
            case "string":
            case "String":
            case "System.String":
                return "string";
            case "bool":
            case "Boolean":
            case "System.Boolean":
                return "bool";
        }

        return schema.FindEnum(table, column.TypeName) is { } found ? "enum:" + found.FullName : null;
    }

    /// <summary>
    /// セルの文字列を、比べるための値にする。空欄は型の既定値(<c>0</c> / <c>""</c> / <c>false</c> / enum の値 <c>0</c>)。
    /// 変換できないセルは false(そのエラーは行の変換で表示するので、ここでは表示しない)。
    /// </summary>
    public static bool TryNormalize(string raw, string kind, DefinitionSchema schema, out string value)
    {
        value = "";
        switch (kind)
        {
            case "number":
                if (raw.Length == 0)
                {
                    value = "0";
                    return true;
                }

                if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                {
                    value = number.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                return false;
            case "string":
                value = raw;
                return true;
            case "bool":
                if (!CellConverter.TryConvert(raw, typeof(bool), false, null, out var boolean, out _))
                {
                    return false;
                }

                value = boolean!.ToString()!;
                return true;
        }

        var enumSchema = schema.Enums.FirstOrDefault(e => "enum:" + e.FullName == kind);
        var member = raw.Length == 0
            ? enumSchema?.Members.FirstOrDefault(m => m.Value == 0)
            : enumSchema?.Members.FirstOrDefault(m => m.InputName == raw);
        if (member is null)
        {
            return false;
        }

        value = member.Value.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
