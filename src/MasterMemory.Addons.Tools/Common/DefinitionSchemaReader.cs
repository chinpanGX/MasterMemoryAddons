using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MasterMemory.Addons.Tools.Common;

/// <summary>
/// 定義のソース(<see cref="DefinitionSource.Load"/> でパースしたもの。コード生成と共有する)から、テーブルの全カラムと enum を取り出す。
/// コード生成と同じく、意味解析・コンパイルはしない。
/// </summary>
public static class DefinitionSchemaReader
{
    public static DefinitionSchema Read(IReadOnlyList<DefinitionSource> sources)
    {
        var tables = new List<TableSchema>();
        var enums = new List<EnumSchema>();
        var errors = new List<string>();

        foreach (var (relative, tree) in sources)
        {
            var syntaxErrors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (syntaxErrors.Count > 0)
            {
                errors.AddRange(syntaxErrors.Select(d => $"{relative}: 構文エラー: {d}"));
                continue;
            }

            var root = tree.GetRoot();
            foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var table = ReadTable(type, relative, errors);
                if (table is not null)
                {
                    tables.Add(table);
                }
            }

            foreach (var declaration in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
            {
                var schema = ReadEnum(declaration, relative, errors);
                if (schema is not null)
                {
                    enums.Add(schema);
                }
            }
        }

        foreach (var duplicate in tables.GroupBy(t => t.TableName, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"テーブル名 {duplicate.Key} が重複しています: {string.Join(", ", duplicate.Select(t => t.FullTypeName))}");
        }

        var result = new DefinitionSchema(tables, enums, errors);
        ValidateRules(result, errors);
        return result;
    }

    // データ段の検証の属性が、使える列・参照先を指しているかを確かめる
    private static void ValidateRules(DefinitionSchema schema, List<string> errors)
    {
        foreach (var table in schema.Tables)
        {
            foreach (var column in table.Columns)
            {
                var label = $"{table.File}: {table.TypeName}.{column.Name}";
                var kind = ColumnValue.KindOf(schema, table, column);
                if ((column.IsUnique || column.Exists is not null) && kind is null)
                {
                    errors.Add($"{label}: [Unique] / [Exists] は、CSV で使える型(int / long / string / bool / enum)の列にだけ付けられます。");
                    continue;
                }

                if (column.Exists is not { } rule)
                {
                    continue;
                }

                var target = schema.FindTable(table, rule.TableTypeName);
                if (target is null)
                {
                    errors.Add($"{label}: [Exists] の typeof({rule.TableTypeName}) が、テーブルの型([MemoryTable] を付けた class)に見つかりません。");
                    continue;
                }

                ColumnSchema? targetColumn;
                if (rule.Column is null)
                {
                    var keys = target.Columns.Where(c => c.IsPrimaryKey).ToList();
                    if (keys.Count != 1)
                    {
                        errors.Add($"{label}: [Exists] で参照する {target.TypeName} の主キーが1列ではありません。列を指定してください(例: [Exists(typeof({target.TypeName}), nameof({target.TypeName}.列))])。");
                        continue;
                    }

                    targetColumn = keys[0];
                }
                else
                {
                    targetColumn = target.Columns.FirstOrDefault(c => c.Name == rule.Column);
                    if (targetColumn is null)
                    {
                        errors.Add($"{label}: [Exists] で参照する列 {target.TypeName}.{rule.Column} がありません。");
                        continue;
                    }
                }

                var targetKind = ColumnValue.KindOf(schema, target, targetColumn);
                if (targetKind != kind)
                {
                    errors.Add($"{label}: [Exists] で参照する {target.TypeName}.{targetColumn.Name} と、列の型が合いません({column.TypeName} と {targetColumn.TypeName})。");
                }
            }
        }
    }

    private static TableSchema? ReadTable(TypeDeclarationSyntax type, string file, List<string> errors)
    {
        var memoryTable = FindAttribute(type.AttributeLists, "MemoryTable");
        if (memoryTable is null)
        {
            return null;
        }

        var label = $"{file}: {type.Identifier.ValueText}";
        if (StringArgument(memoryTable) is not { } tableName)
        {
            errors.Add($"{label}: [MemoryTable] のテーブル名は文字列リテラルで書いてください。");
            return null;
        }

        if (NonClassTableMessage(type) is { } message)
        {
            errors.Add($"{label}: {message}");
            return null;
        }

        var columns = type.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(IsColumn)
            .Select(p => new ColumnSchema(
                p.Identifier.ValueText,
                p.Type.ToString(),
                FindAttribute(p.AttributeLists, "PrimaryKey") is not null,
                FindAttribute(p.AttributeLists, "Unique") is not null,
                ReadExists(p, $"{label}.{p.Identifier.ValueText}", errors)))
            .ToList();

        return new TableSchema(tableName, NamespaceOf(type), ContainingTypesOf(type), type.Identifier.ValueText, columns, file);
    }

    // [Exists(typeof(Item))] / [Exists(typeof(Item), nameof(Item.Code))] / [Exists(typeof(Item), "Code", AllowEmpty = true)] を読む
    private static ExistsRule? ReadExists(PropertyDeclarationSyntax property, string label, List<string> errors)
    {
        var attribute = FindAttribute(property.AttributeLists, "Exists");
        if (attribute is null)
        {
            return null;
        }

        var arguments = attribute.ArgumentList?.Arguments ?? default;
        var positional = arguments.Where(a => a.NameEquals is null).ToList();
        var table = positional.ElementAtOrDefault(0)?.Expression as TypeOfExpressionSyntax;
        if (table is null)
        {
            errors.Add($"{label}: [Exists] の1つ目の引数は typeof(テーブルの型) で書いてください。");
            return null;
        }

        string? column = null;
        if (positional.ElementAtOrDefault(1) is { } second)
        {
            column = second.Expression switch
            {
                LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
                InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } invocation
                    => LastIdentifier(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression),
                _ => null,
            };
            if (column is null)
            {
                errors.Add($"{label}: [Exists] の2つ目の引数(列)は、nameof(型.列) か文字列リテラルで書いてください。");
                return null;
            }
        }

        var allowEmpty = false;
        if (arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == "AllowEmpty") is { } named)
        {
            if (!named.Expression.IsKind(SyntaxKind.TrueLiteralExpression) && !named.Expression.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                errors.Add($"{label}: [Exists] の AllowEmpty は true か false で書いてください。");
                return null;
            }

            allowEmpty = named.Expression.IsKind(SyntaxKind.TrueLiteralExpression);
        }

        return new ExistsRule(table.Type.ToString(), column, allowEmpty);
    }

    private static string? LastIdentifier(ExpressionSyntax? expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };
    }

    // 列になるのは、public でインスタンスの、set か init を持つプロパティ(MessagePack がシリアライズするもの)
    private static bool IsColumn(PropertyDeclarationSyntax property)
    {
        return property.Modifiers.Any(SyntaxKind.PublicKeyword)
            && !property.Modifiers.Any(SyntaxKind.StaticKeyword)
            && property.AccessorList is not null
            && property.AccessorList.Accessors.Any(a =>
                a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration));
    }

    private static EnumSchema? ReadEnum(EnumDeclarationSyntax declaration, string file, List<string> errors)
    {
        var members = new List<EnumMemberSchema>();
        // ulong の大きな値や、long.MaxValue の次の値も表せるよう、decimal で数える
        decimal next = 0;
        foreach (var member in declaration.Members)
        {
            var label = $"{file}: {declaration.Identifier.ValueText}.{member.Identifier.ValueText}";
            var value = next;
            if (member.EqualsValue is not null && !TryReadInteger(member.EqualsValue.Value, out value))
            {
                errors.Add($"{label}: enum の値は整数のリテラルで書いてください。");
                return null;
            }

            if (value < long.MinValue || value > long.MaxValue)
            {
                errors.Add($"{label}: enum の値 {value} が long の範囲を超えています(扱えるのは long に収まる値だけです)。");
                return null;
            }

            var inputName = FindAttribute(member.AttributeLists, "InputName") is { } attribute
                ? StringArgument(attribute)
                : null;
            members.Add(new EnumMemberSchema(member.Identifier.ValueText, (long)value, inputName));
            next = value + 1;
        }

        return new EnumSchema(NamespaceOf(declaration), ContainingTypesOf(declaration), declaration.Identifier.ValueText, members, file);
    }

    private static bool TryReadInteger(ExpressionSyntax expression, out decimal value)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal
                when literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.Value is int or uint or long or ulong:
                value = Convert.ToDecimal(literal.Token.Value, CultureInfo.InvariantCulture);
                return true;
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression } negative
                when TryReadInteger(negative.Operand, out var operand):
                value = -operand;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>宣言を囲む名前空間(`A.B`。名前空間の外なら空)。</summary>
    public static string NamespaceOf(SyntaxNode node)
    {
        var names = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(n => n.Name.ToString())
            .Reverse();
        return string.Join(".", names);
    }

    // 型の中に入れ子にした宣言の、外側の型名(外側から順)
    private static IReadOnlyList<string> ContainingTypesOf(SyntaxNode node)
    {
        return node.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(t => t.Identifier.ValueText)
            .Reverse()
            .ToList();
    }

    /// <summary>
    /// テーブルの型(<c>[MemoryTable]</c> を付けた型)が class でなければ、そのエラーの文を返す(class か、テーブルの型でなければ null)。
    /// テーブルの型は class だけ(record・struct・interface は使えない)。コード生成とマスタ生成で同じ文を使う。
    /// </summary>
    public static string? NonClassTableMessage(TypeDeclarationSyntax type)
    {
        if (type is ClassDeclarationSyntax || FindAttribute(type.AttributeLists, "MemoryTable") is null)
        {
            return null;
        }

        var kind = type switch
        {
            RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "record struct",
            RecordDeclarationSyntax => "record",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            _ => type.Keyword.ValueText,
        };
        return $"テーブルの型に {kind} は使えません。class で書いてください(プロパティは {{ get; init; }})。";
    }

    private static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> lists, string name)
    {
        return lists.SelectMany(l => l.Attributes).FirstOrDefault(a => IsAttribute(a, name));
    }

    private static string? StringArgument(AttributeSyntax attribute)
    {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();
        return argument?.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    private static bool IsAttribute(AttributeSyntax attribute, string name)
    {
        var simple = attribute.Name switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            SimpleNameSyntax s => s.Identifier.ValueText,
            _ => attribute.Name.ToString(),
        };

        return simple == name || simple == name + "Attribute";
    }
}
