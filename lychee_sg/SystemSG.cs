using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace lychee_sg
{
    internal sealed class SystemInfo
    {
        public string Name;

        public string Namesapce;

        public ParamInfo[] Params;
    }

    internal struct ParamInfo
    {
        public ITypeSymbol Type;

        public RefKind Kind;
    }

    [Generator]
    public sealed class SystemSG : IIncrementalGenerator
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

            context.RegisterSourceOutput(values, (spc, sysInfo) =>
            {
                var sb = new StringBuilder($@"
using System;
using lychee.interfaces;

namespace {sysInfo.Namesapce};

public partial class {sysInfo.Name} : ISystem
{{
    private static class SystemDataAG
    {{
        public static int[] TypeIdList;

        public static Archetype[] Archetypes;
    }}

    public void ConfigureAG(ArchetypeManager manager)
    {{
        // SystemDataAG.Archetypes = manager.
    }}

    public unsafe void ExecuteAG()
    {{
        
    }}
}}

");

                spc.AddSource($"{sysInfo.Name}_System.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
            });
        }

        private static bool HasAttribute(SyntaxNode node)
        {
            if (node is ClassDeclarationSyntax typeDeclNode)
            {
                return typeDeclNode.AttributeLists.Any(attributeList =>
                    attributeList.Attributes.Select(attr => attr.Name.ToString()).Any(name =>
                        name == "System" || name == "lychee.attributes.System"));
            }

            return false;
        }

        private static SystemInfo GetTargetMethodInfo(ref GeneratorSyntaxContext context)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;

            foreach (var memberDecl in classDecl.Members)
            {
                if (memberDecl is MethodDeclarationSyntax methodDecl)
                {
                    if (memberDecl.Kind() == SyntaxKind.MethodDeclaration && methodDecl.Identifier.Text == "Execute")
                    {
                        var symbol = context.SemanticModel.GetSymbolInfo(methodDecl).Symbol as IMethodSymbol;
                        var paramList = symbol.Parameters.Select(x => new ParamInfo { Type = x.Type, Kind = x.RefKind })
                            .ToArray();

                        return new SystemInfo
                        {
                            Name = classDecl.Identifier.Text,
                            Namesapce = Utils.GetNamespace(classDecl),
                            Params = paramList,
                        };
                    }
                }
            }

            return null;
        }
    }
}
