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
                    transform: (ctx, _) => GetTargetMethodInfo(ref ctx)
                )
                .Where(m => m != null);

            context.RegisterSourceOutput(values, (spc, classInfo) =>
            {
                var (declType, name, ns) = classInfo.Value;
                var sb = new StringBuilder($@"
using System;
using lychee.interfaces;

namespace {ns};

public partial {declType} {name} : IComponentBundle
{{
    public unsafe void SetDataWithPtr(int typeId, void* ptr)
    {{
            
    }}

    public static int[] TypeIds {{ get; set; }} = [];
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
                return typeDeclNode.AttributeLists.Any(attributeList =>
                    attributeList.Attributes.Select(attr => attr.Name.ToString()).Any(name =>
                        name == "ComponentBundle" || name == "lychee.attributes.ComponentBundle"));
            }

            return false;
        }

        private static (string declType, string name, string ns)? GetTargetMethodInfo(
            ref GeneratorSyntaxContext context)
        {
            var typeDecl = (TypeDeclarationSyntax)context.Node;
            var ns = Utils.GetNamespace(typeDecl);

            return (typeDecl is ClassDeclarationSyntax ? "class" : "struct", typeDecl.Identifier.Text, ns);
        }
    }
}