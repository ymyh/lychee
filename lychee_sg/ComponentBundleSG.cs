using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace lychee_sg
{

[Generator]
public class ComponentBundleSG : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
         // Debugger.Launch();
        var values = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => HasAttribute(s),
                transform: (ctx, _) => GetSemanticTarget(ref ctx)
            )
            .Where(m => m != null);

        context.RegisterSourceOutput(values, (spc, classInfo) =>
        {
            var (declType, name, ns) = classInfo.Value;
            var sb = new StringBuilder($@"
using System;
using lychee.interfaces;

namespace {ns}
{{
    public partial {declType} {name} : IComponentBundle
    {{
        public void Hello() => Console.WriteLine(""Hello from generated method!"");

        public unsafe void SetDataWithPtr(int typeId, void* ptr)
        {{
            
        }}

        public static int[] TypeIds {{ get; set; }} = [];
    }}
}}
");

            spc.AddSource($"{name}_ComponentBundle.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }

    private static bool HasAttribute(SyntaxNode node)
    {
        if (node is ClassDeclarationSyntax || node is StructDeclarationSyntax)
        {
            var typeDeclNode = (TypeDeclarationSyntax)node;
            return typeDeclNode.AttributeLists.Any(attributeList => attributeList.Attributes.Select(attr => attr.Name.ToString()).Any(name => name == "ComponentBundle" || name == "lychee.attributes.ComponentBundle"));
        }

        return false;
    }

    private static (string declType, string name, string ns)? GetSemanticTarget(ref GeneratorSyntaxContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var ns = GetNamespace(typeDecl);

        return (typeDecl is ClassDeclarationSyntax ? "class" : "struct", typeDecl.Identifier.Text, ns);
    }

    private static string GetNamespace(SyntaxNode syntax)
    {
        // 存储命名空间的各级名称
        var namespaces = new Stack<string>();

        for (var node = syntax; node != null; node = node.Parent)
        {
            switch (node)
            {
                case NamespaceDeclarationSyntax nsDecl:
                    namespaces.Push(nsDecl.Name.ToString());
                    break;

                case FileScopedNamespaceDeclarationSyntax fileNsDecl:
                    namespaces.Push(fileNsDecl.Name.ToString());
                    break;
            }
        }

        // 如果没有命名空间，就返回空字符串（代表 global namespace）
        if (namespaces.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(".", namespaces);
    }
}

}
