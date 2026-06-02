using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace lychee_sg
{
    internal sealed class ComponentInfo
    {
        public string Name;

        public string Namespace;

        public string TypeParameters;

        public bool IsEmpty;

        public bool IsPartial;
    }

    [Generator]
    public sealed class ComponentSG : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor NotPartialRule = new DiagnosticDescriptor(
            "LYCHEE_COMPILE_ERR_1005",
            "Struct with [Component] must be partial",
            "Struct '{0}' marked with [Component] must be declared as partial",
            "Component Auto Implementation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var values = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (s, _) => HasAttribute(s),
                    transform: (ctx, _) => GetComponentInfo(ref ctx)
                )
                .Where(m => m != null);

            context.RegisterSourceOutput(values, (spc, info) =>
            {
                if (!info.IsPartial)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(NotPartialRule, Location.None, info.Name));
                    return;
                }

                var actualType = string.IsNullOrEmpty(info.TypeParameters)
                    ? info.Name
                    : info.Name + info.TypeParameters;

                var sizeCode = info.IsEmpty
                    ? "new lychee.interfaces.ComponentMeta(0);"
                    : $"new lychee.interfaces.ComponentMeta(System.Runtime.CompilerServices.Unsafe.SizeOf<{actualType}>());";

                var sb = new StringBuilder($@"
using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace {info.Namespace};

partial struct {info.Name}{info.TypeParameters} : lychee.interfaces.IComponent
{{
    public lychee.interfaces.ComponentMeta GetComponentMeta()
    {{
        return {sizeCode}
    }}
}}
");

                spc.AddSource($"{info.Name}_Component.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
            });
        }

        private static bool HasAttribute(SyntaxNode node)
        {
            if (node is StructDeclarationSyntax typeDeclNode)
            {
                return typeDeclNode.AttributeLists.Any(attributeList =>
                    attributeList.Attributes.Select(attr => attr.Name.ToString()).Any(name =>
                        name == "Component" || name == "ComponentAttribute" || name == "lychee.attributes.Component" || name == "lychee.attributes.ComponentAttribute"));
            }

            return false;
        }

        private static ComponentInfo GetComponentInfo(ref GeneratorSyntaxContext context)
        {
            var structDecl = (StructDeclarationSyntax)context.Node;
            var isPartial = structDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

            var classSymbol = context.SemanticModel.GetDeclaredSymbol(structDecl);
            var hasInstanceFields = classSymbol.GetMembers()
                .OfType<IFieldSymbol>()
                .Any(f => !f.IsStatic);

            var typeParams = structDecl.TypeParameterList?.ToString() ?? "";
            var namespaceName = Utils.GetNamespace(structDecl);

            return new ComponentInfo
            {
                Name = structDecl.Identifier.Text,
                Namespace = namespaceName,
                TypeParameters = typeParams,
                IsEmpty = !hasInstanceFields,
                IsPartial = isPartial,
            };
        }
    }
}
