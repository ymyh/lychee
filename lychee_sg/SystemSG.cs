﻿using System.Collections.Generic;
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

        public bool HasAfterExecute;
    }

    internal enum ParamKind
    {
        Component,
        Resource,
        ResourceRef,
        Entity,
        EntityCommander,
    }

    internal struct ParamInfo
    {
        public ITypeSymbol Type;

        public RefKind RefKind;

        public ParamKind ParamKind;

        public string ParamName;
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
                var componentTypes = sysInfo.Params.Where(p => p.ParamKind == ParamKind.Component).ToArray();
                var resourceTypes = sysInfo.Params.Where(p => p.ParamKind == ParamKind.Resource || p.ParamKind == ParamKind.ResourceRef).ToArray();
                var sb = new StringBuilder($@"
using System;
using System.Runtime.InteropServices;
using lychee;
using lychee.interfaces;

namespace {sysInfo.Namespace};

sealed partial class {sysInfo.Name} : ISystem
{{
    private static class SystemDataAG
    {{
        public static ResourcePool Pool;

        public static int[] TypeIdList;

        public static Archetype[] Archetypes;

        public static EntityCommander EntityCommander;
    }}

    private static class ResourceDataAG
    {{
{MakeResourceDataAGCode(resourceTypes)}
    }}

{MakeInitializeAGCode(componentTypes, resourceTypes)}

    public void ConfigureAG(App app, SystemDescriptor descriptor)
    {{
        SystemDataAG.Archetypes = app.World.ArchetypeManager.MatchArchetypesByPredicate(descriptor.AllFilter, descriptor.AnyFilter, descriptor.NoneFilter, SystemDataAG.TypeIdList);
    }}
{MakeExecuteAGCode(sysInfo.Params, componentTypes, resourceTypes, sysInfo.HasAfterExecute)}
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
            var hasAfterExecute = false;
            ParamInfo[] paramList = null;

            foreach (var memberDecl in classDecl.Members)
            {
                if (memberDecl is MethodDeclarationSyntax methodDecl)
                {
                    if (memberDecl.Kind() == SyntaxKind.MethodDeclaration && methodDecl.Identifier.Text == "Execute")
                    {
                        var symbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
                        paramList = symbol.Parameters.Select(x =>
                        {
                            var paramKind = ParamKind.Component;

                            if (x.Type.ToDisplayString() == "lychee.EntityCommander")
                            {
                                paramKind = ParamKind.EntityCommander;
                            }
                            else if (x.Type.ToDisplayString() == "lychee.Entity")
                            {
                                paramKind = ParamKind.Entity;
                            }

                            if (x.GetAttributes().Any(a =>
                                {
                                    var name = a.AttributeClass.ToDisplayString();
                                    return name == "lychee.attributes.Resource";
                                }))
                            {
                                paramKind = x.Type.IsValueType ? ParamKind.ResourceRef : ParamKind.Resource;
                            }

                            return new ParamInfo
                            {
                                Type = x.Type,
                                RefKind = x.RefKind,
                                ParamKind = paramKind,
                                ParamName = x.Name,
                            };
                        }).ToArray();
                    }

                    if (memberDecl.Kind() == SyntaxKind.MethodDeclaration && methodDecl.Identifier.Text == "AfterExecute")
                    {
                        var symbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
                        hasAfterExecute = symbol.Parameters.Length == 0;
                    }
                }
            }

            return new SystemInfo
            {
                Name = classDecl.Identifier.Text,
                Namespace = Utils.GetNamespace(classDecl),
                Params = paramList,
                HasAfterExecute = hasAfterExecute,
            };
        }

        private static string MakeInitializeAGCode(ParamInfo[] componentTypes, ParamInfo[] resourceTypes)
        {
            var resourceDecl = new StringBuilder();

            foreach (var resourceType in resourceTypes)
            {
                if (resourceType.ParamKind == ParamKind.Resource)
                {
                    resourceDecl.AppendLine($"        ResourceDataAG.{resourceType.ParamName} = app.ResourcePool.GetResource<{resourceType.Type}>();");
                }
                else
                {
                    resourceDecl.AppendLine($"        ResourceDataAG.{resourceType.ParamName} = app.ResourcePool.GetResourcePtr<{resourceType.Type}>();");
                }
            }

            var registerTypes = string.Join(", ", componentTypes.Select(p => $"app.TypeRegistrar.RegisterComponent<{p.Type}>()"));

            return $@"
    public void InitializeAG(App app)
    {{
        SystemDataAG.Pool = app.ResourcePool;
        SystemDataAG.TypeIdList = [{registerTypes}];
        SystemDataAG.EntityCommander = new(app);

{resourceDecl}
    }}";
        }

        private static string MakeResourceDataAGCode(ParamInfo[] resourceParams)
        {
            var declResourceCode = new StringBuilder();

            if (resourceParams.Length > 0)
            {
                for (var i = 0; i < resourceParams.Length; i++)
                {
                    var resourceParam = resourceParams[i];
                    var paramName = resourceParam.ParamName;

                    if (resourceParam.ParamKind == ParamKind.Resource)
                    {
                        declResourceCode.AppendLine($"        public static {resourceParam.Type} {paramName};");
                    }
                    else
                    {
                        declResourceCode.AppendLine($"        public static byte[] {paramName};");
                    }

                    if (i != resourceParams.Length - 1)
                    {
                        declResourceCode.AppendLine();
                    }
                }
            }

            return declResourceCode.ToString();
        }

        private static string MakeExecuteAGCode(ParamInfo[] allParams, ParamInfo[] componentParams, ParamInfo[] resourceParams, bool hasAfterExecute)
        {
            string body;

            var execParams = string.Join(", ", allParams.Select((param, idx) =>
            {
                var paramName = param.ParamName;

                switch (param.ParamKind)
                {
                    case ParamKind.Component:
                        var derefCode = $"(({param.Type}*){paramName})[i]";
                        switch (param.RefKind)
                        {
                            case RefKind.In:
                            case RefKind.RefReadOnlyParameter:
                                return "in " + derefCode;
                            case RefKind.Out:
                                return "out " + derefCode;
                            case RefKind.Ref:
                                return $"ref {derefCode}";
                            case RefKind.None:
                                return derefCode;
                        }

                        break;

                    case ParamKind.Resource:
                        return $"ResourceDataAG.{paramName}";

                    case ParamKind.ResourceRef:
                        switch (param.RefKind)
                        {
                            case RefKind.In:
                            case RefKind.RefReadOnlyParameter:
                                return "in " + paramName;
                            case RefKind.Out:
                                return "out " + paramName;
                            case RefKind.Ref:
                                return $"ref {paramName}";
                            case RefKind.None:
                                return paramName;
                        }

                        break;

                    case ParamKind.EntityCommander:
                        return "SystemDataAG.EntityCommander";

                    case ParamKind.Entity:
                        return "entitySpan[i].Item2";
                }

                return "";
            }));

            var declResourceCode = new StringBuilder();

            if (resourceParams.Length > 0)
            {
                foreach (var resourceParam in resourceParams)
                {
                    var paramName = resourceParam.ParamName;

                    if (resourceParam.ParamKind == ParamKind.ResourceRef)
                    {
                        declResourceCode.AppendLine($"        ref var {paramName} = ref MemoryMarshal.AsRef<{resourceParam.Type}>(new Span<byte>(ResourceDataAG.{paramName}));");
                    }
                }
            }

            if (componentParams.Length > 0)
            {
                var declIterCode = new StringBuilder();
                var iterDeclCurrentCode = new StringBuilder();
                var iterMoveNextCode = new List<string>(componentParams.Length);

                for (var i = 0; i < componentParams.Length; i++)
                {
                    declIterCode.AppendLine(
                        $"            var iter{i} = archetype.IterateTypeAmongChunk(SystemDataAG.TypeIdList[{i}]).GetEnumerator();");
                    iterMoveNextCode.Add($"iter{i}.MoveNext()");

                    iterDeclCurrentCode.AppendLine(i == 0
                        ? $"                var ({componentParams[i].Type.Name.ToLower()}, size) = iter{i}.Current;"
                        : $"                var ({componentParams[i].Type.Name.ToLower()}, _) = iter{i}.Current;");
                }

                var iterateChunkWhileExpr = $@"
            while ({string.Join(" & ", iterMoveNextCode)})
            {{
{iterDeclCurrentCode}
                for (var i = 0; i < size; i++)
                {{
                    Execute({execParams});
                }}
            }}";

                body = $@"
{declResourceCode}
        foreach (var archetype in SystemDataAG.Archetypes)
        {{
            var entitySpan = archetype.GetEntitiesSpan();
{declIterCode}{iterateChunkWhileExpr}
        }}
        {(hasAfterExecute ? "AfterExecute();" : "")}

        return SystemDataAG.EntityCommander;";
            }
            else
            {
                body = $@"
{declResourceCode}        Execute({execParams});
        {(hasAfterExecute ? "AfterExecute();" : "")}

        return SystemDataAG.EntityCommander;";
            }

            return $@"
    public unsafe EntityCommander ExecuteAG()
    {{{body}
    }}";
        }
    }
}
