using System;
using System.Globalization;
using System.Linq;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// CSV のセル1つを、列の型の値に変換する。規則は docs/design/master-gen.md の「CSV の読み込み」。
/// </summary>
public static class CellConverter
{
    /// <param name="raw">セルの文字列。</param>
    /// <param name="type">列の型(コンパイルした型のプロパティの型)。</param>
    /// <param name="isPrimaryKey">主キーの列か。主キーは空欄にできない。</param>
    /// <param name="enumSchema"><paramref name="type"/> が enum のとき、その定義(入力名を持つ)。</param>
    /// <param name="value">変換した値。</param>
    /// <param name="error">変換できないときの理由。</param>
    public static bool TryConvert(
        string raw,
        Type type,
        bool isPrimaryKey,
        EnumSchema? enumSchema,
        out object? value,
        out string error)
    {
        value = null;
        error = "";

        if (type.IsEnum && enumSchema is null)
        {
            error = $"enum {type.FullName} の定義が見つかりません。";
            return false;
        }

        if (raw.Length == 0)
        {
            return TryDefault(type, isPrimaryKey, enumSchema, out value, out error);
        }

        if (type == typeof(string))
        {
            value = raw;
            return true;
        }

        if (type == typeof(int))
        {
            if (int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }

            error = $"int に変換できません: '{raw}'";
            return false;
        }

        if (type == typeof(long))
        {
            if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }

            error = $"long に変換できません: '{raw}'";
            return false;
        }

        if (type == typeof(bool))
        {
            switch (raw)
            {
                case "TRUE":
                case "True":
                case "1":
                    value = true;
                    return true;
                case "FALSE":
                case "False":
                case "0":
                    value = false;
                    return true;
                default:
                    error = $"bool に変換できません(TRUE / FALSE / True / False / 1 / 0): '{raw}'";
                    return false;
            }
        }

        if (type.IsEnum)
        {
            var member = enumSchema!.Members.FirstOrDefault(m => string.Equals(m.InputName, raw, StringComparison.Ordinal));
            if (member is null)
            {
                var names = string.Join(", ", enumSchema.Members.Where(m => m.InputName is not null).Select(m => m.InputName));
                error = $"'{raw}' は {enumSchema.Name} の入力名にありません(書けるもの: {names})。";
                return false;
            }

            value = Enum.Parse(type, member.Name);
            return true;
        }

        error = $"未対応の型です: {type.FullName}(int / long / string / bool / enum が使えます)";
        return false;
    }

    private static bool TryDefault(Type type, bool isPrimaryKey, EnumSchema? enumSchema, out object? value, out string error)
    {
        value = null;
        error = "";

        if (isPrimaryKey)
        {
            error = "主キーの列は空欄にできません。";
            return false;
        }

        if (type == typeof(string))
        {
            value = "";
            return true;
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(bool))
        {
            value = Activator.CreateInstance(type);
            return true;
        }

        if (type.IsEnum)
        {
            if (enumSchema!.Members.All(m => m.Value != 0))
            {
                error = $"{enumSchema.Name} には値 0 のメンバーがないため、空欄にできません。";
                return false;
            }

            value = Enum.ToObject(type, 0);
            return true;
        }

        error = $"未対応の型です: {type.FullName}(int / long / string / bool / enum が使えます)";
        return false;
    }
}
