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

        public string Namespace;

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
                var componentTypes = sysInfo.Params.Where(p => p.Type.ToDisplayString() != "lychee.EntityCommander");
                var registerTypes = string.Join(", ",
                    componentTypes.Select(p => $"app.TypeRegistry.Register<{p.Type}>()"));

                var getIters = new StringBuilder();
                for (var i = 0; i < componentTypes.Count(); i++)
                {
                    getIters.AppendLine(
                        $"var iter{i} = archetype.IterateTypeAmongChunk(SystemDataAG.TypeIdList[{i}]);");
                }

                var sb = new StringBuilder($@"
using System;
using lychee;
using lychee.interfaces;

namespace {sysInfo.Namespace};

public sealed partial class {sysInfo.Name} : ISystem
{{
    private static class SystemDataAG
    {{
        public static int[] TypeIdList;

        public static Archetype[] Archetypes;

        public static EntityCommander EntityCommander;
    }}

    public void InitializeAG(App app)
    {{
        SystemDataAG.TypeIdList = [{registerTypes}];
        SystemDataAG.EntityCommander = new(app.World);
    }}

    public void ConfigureAG(App app)
    {{
        SystemDataAG.Archetypes = app.World.ArchetypeManager.MatchArchetypesByPredicate([], [], [], SystemDataAG.TypeIdList);
    }}

    public unsafe void ExecuteAG()
    {{
        foreach (var archetype in SystemDataAG.Archetypes)
        {{
            {getIters}
            Execute(new {sysInfo.Params[0].Type.Name}(), SystemDataAG.EntityCommander);  
        }}
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
                        name == "AutoImplSystem" || name == "lychee.attributes.AutoImplSystem"));
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
                        var symbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
                        var paramList = symbol.Parameters.Select(x => new ParamInfo { Type = x.Type, Kind = x.RefKind })
                            .ToArray();

                        return new SystemInfo
                        {
                            Name = classDecl.Identifier.Text,
                            Namespace = Utils.GetNamespace(classDecl),
                            Params = paramList,
                        };
                    }
                }
            }

            return null;
        }
    }
}
