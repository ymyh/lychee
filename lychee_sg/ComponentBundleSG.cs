using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace lychee_sg
{
    internal sealed class StructDecl
    {
        public string Name;

        public string Namespace;

        public List<(string type, string name)> FieldProp;
    }

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

            context.RegisterSourceOutput(values, (spc, structDecl) =>
            {
                var switchCases = new StringBuilder();

                for (var i = 0; i < structDecl.FieldProp.Count; i++)
                {
                    var fieldName = structDecl.FieldProp[i].name;
                    switchCases.AppendLine($@"
        case {i}:
            fixed (void* srcPtr = &{fieldName})
            {{
                NativeMemory.Copy(srcPtr, ptr, (nuint) sizeof({structDecl.FieldProp[i].type}));
            }}
            break;");
                }

                var sb = new StringBuilder($@"
using System;
using System.Runtime.InteropServices;
using lychee.interfaces;

namespace {structDecl.Namespace};

public partial struct {structDecl.Name} : IComponentBundle
{{
    public unsafe void SetDataAG(int index, void* ptr)
    {{
        switch (index)
        {{
        {switchCases}
        }}
    }}

    public static int[] TypeIdAG {{ get; set; }}
}}
");

                spc.AddSource($"{structDecl.Name}_ComponentBundle.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
            });
        }

        private static bool HasAttribute(SyntaxNode node)
        {
            if (node is StructDeclarationSyntax structDecl)
            {
                return structDecl.AttributeLists.Any(attributeList =>
                    attributeList.Attributes.Select(attr => attr.Name.ToString()).Any(name =>
                        name == "ComponentBundle" || name == "lychee.attributes.ComponentBundle"));
            }

            return false;
        }

        private static StructDecl GetTargetMethodInfo(ref GeneratorSyntaxContext context)
        {
            var structDecl = (StructDeclarationSyntax)context.Node;
            var ns = Utils.GetNamespace(structDecl);
            var fields = structDecl.Members.OfType<FieldDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mo => mo.IsKind(SyntaxKind.PublicKeyword))).ToArray();
            var fieldProp = new List<(string type, string name)>();

            foreach (var field in fields)
            {
                var typeName = context.SemanticModel.GetTypeInfo(field.Declaration.Type).Type.Name;

                fieldProp.AddRange(
                    field.Declaration.Variables.Select(variable => (typeName, variable.Identifier.Text)));
            }

            return new StructDecl
            {
                Name = structDecl.Identifier.Text,
                Namespace = ns,
                FieldProp = fieldProp,
            };
        }
    }
}
