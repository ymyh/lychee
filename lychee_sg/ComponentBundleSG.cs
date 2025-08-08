using System.Diagnostics;
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
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => HasAttribute(s),
                transform: (ctx, _) => GetSemanticTarget(ctx)
            )
            .Where(m => m != null);

        context.RegisterSourceOutput(classDeclarations, (spc, classInfo) =>
        {
            var (declType, name, ns) = classInfo.Value;
            var sb = new StringBuilder($@"
using System;
using lychee.interfaces;

namespace lychee_dev
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

            foreach (var attributeList in typeDeclNode.AttributeLists)
            {
                if (attributeList.Attributes.Select(attr => attr.Name.ToString()).Any(name => name == "ComponentBundle" || name == "lychee.attributes.ComponentBundle"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (string declType, string name, string ns)? GetSemanticTarget(GeneratorSyntaxContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;
        var ns = GetNamespace(typeDecl);

        return (typeDecl is ClassDeclarationSyntax ? "class" : "struct", typeDecl.Identifier.Text, ns);
    }

    private static string GetNamespace(SyntaxNode syntax)
    {
        while (syntax != null)
        {
            if (syntax is NamespaceDeclarationSyntax ns)
            {
                return ns.Name.ToString();
            }
            syntax = syntax.Parent;
        }
        return null;
    }
}

}
