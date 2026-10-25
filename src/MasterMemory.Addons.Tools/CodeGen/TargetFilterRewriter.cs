using System.Linq;
using MasterMemory.Addons.Tools.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MasterMemory.Addons.Tools.CodeGen;

/// <summary>
/// 対象外の [ServerOnly] / [ClientOnly] の列・型を削除し、Only 属性と属性の using を取り除く。
/// 構文だけを見るので、MasterMemory などの参照は不要。
/// </summary>
internal sealed class TargetFilterRewriter : CSharpSyntaxRewriter
{
    private const string ServerOnly = "ServerOnly";
    private const string ClientOnly = "ClientOnly";
    private const string InputName = "InputName";

    // マスタ生成だけが使い、出力からは取り除く属性(入力名と、データ段の検証)
    private static readonly string[] ToolOnlyAttributes = { InputName, "Unique", "Exists" };

    private static readonly string[] AttributeNamespaces = { "MasterMemory.Addons", "MasterMemory.Addons.Attributes" };

    // 出力から削除する側の属性名と、属性だけを取り除く側の属性名
    private readonly string removeName;
    private readonly string stripName;

    public TargetFilterRewriter(GenerationTarget target)
    {
        if (target == GenerationTarget.Client)
        {
            removeName = ServerOnly;
            stripName = ClientOnly;
        }
        else
        {
            removeName = ClientOnly;
            stripName = ServerOnly;
        }
    }

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Alias is null
            && node.StaticKeyword.IsKind(SyntaxKind.None)
            && node.Name is { } name
            && AttributeNamespaces.Contains(name.ToString()))
        {
            return null;
        }

        return base.VisitUsingDirective(node);
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        => ShouldRemove(node.AttributeLists) ? null : base.VisitClassDeclaration(node);

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        => ShouldRemove(node.AttributeLists) ? null : base.VisitStructDeclaration(node);

    public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
        => ShouldRemove(node.AttributeLists) ? null : base.VisitRecordDeclaration(node);

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        => ShouldRemove(node.AttributeLists) ? null : base.VisitPropertyDeclaration(node);

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
        => ShouldRemove(node.AttributeLists) ? null : base.VisitFieldDeclaration(node);

    public override SyntaxNode? VisitParameterList(ParameterListSyntax node)
    {
        // 位置指定 record の引数だけを対象にする(通常のメソッド引数には触れない)。
        // SeparatedSyntaxList の要素は Visit で null を返せないので、リスト側で取り除く。
        if (node.Parent is RecordDeclarationSyntax)
        {
            var parameters = node.Parameters;
            for (var i = parameters.Count - 1; i >= 0; i--)
            {
                if (ShouldRemove(parameters[i].AttributeLists))
                {
                    parameters = parameters.RemoveAt(i);
                }
            }

            node = node.WithParameters(parameters);
        }

        return base.VisitParameterList(node);
    }

    public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
    {
        var kept = node.Attributes
            .Where(a => !IsAttribute(a, removeName) && !IsAttribute(a, stripName) && !ToolOnlyAttributes.Any(n => IsAttribute(a, n)))
            .ToList();

        if (kept.Count == node.Attributes.Count)
        {
            return base.VisitAttributeList(node);
        }

        // 属性がすべてなくなる場合は、属性リストごと削除する
        if (kept.Count == 0)
        {
            return null;
        }

        return base.VisitAttributeList(node.WithAttributes(SyntaxFactory.SeparatedList(kept)));
    }

    private bool ShouldRemove(SyntaxList<AttributeListSyntax> lists)
        => lists.SelectMany(l => l.Attributes).Any(a => IsAttribute(a, removeName));

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
