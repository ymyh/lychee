using System;
using System.Collections.Generic;
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

        public bool HasBeforeExecute;

        public uint GroupSize;

        public uint ThreadCount;
    }

    internal enum ParamKind
    {
        Component,
        ComponentSpan,
        ClassResource,
        StructResource,
        Entity,
        Commands,
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
                if (sysInfo.Params == null)
                {
                    var descriptor = new DiagnosticDescriptor(
                        "LYCHEE_COMPILE_ERR_1004",
                        "System must contain a method named 'Execute'",
                        "System must contain a method named 'Execute'",
                        "System Auto Implementation",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true
                    );

                    spc.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, sysInfo.Name));
                    return;
                }

                var componentTypes = sysInfo.Params
                    .Where(p => p.ParamKind == ParamKind.Component || p.ParamKind == ParamKind.ComponentSpan).ToArray();
                var resourceTypes = sysInfo.Params.Where(p =>
                    p.ParamKind == ParamKind.ClassResource || p.ParamKind == ParamKind.StructResource).ToArray();
                var sb = new StringBuilder($@"
using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using lychee;
using lychee.interfaces;
using ThreadPool = lychee.threading.ThreadPool;

namespace {sysInfo.Namespace};

partial class {sysInfo.Name} : ISystem
{{
    private static class SystemDataAG
    {{
        public static ResourcePool Pool;{(sysInfo.ThreadCount > 1 ? "\n        public static ThreadPool ThreadPool;" : "")}

        public static int[] TypeIdList;

        public static Archetype[] Archetypes;

        public static Commands[] Commands;
    }}
{MakeResourceDataAGCode(resourceTypes)}
{MakeInitializeAGCode(componentTypes, resourceTypes, sysInfo.ThreadCount)}

    public void ConfigureAG(App app, SystemDescriptor descriptor)
    {{
        SystemDataAG.Archetypes = app.World.ArchetypeManager.MatchArchetypesByPredicate(descriptor.AllFilter, descriptor.AnyFilter, descriptor.NoneFilter, SystemDataAG.TypeIdList);
    }}
{MakeExecuteAGCode(sysInfo.Params, componentTypes, resourceTypes, sysInfo, componentTypes.Any(t => t.ParamKind == ParamKind.ComponentSpan))}
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
            var hasBeforeExecute = false;
            var hasAfterExecute = false;
            ParamInfo[] paramList = null;

            var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
            var autoImplAttr = classSymbol.GetAttributes()
                .First(a => a.AttributeClass.ToDisplayString() == "lychee.attributes.AutoImplSystem");

            var groupSize = (uint)autoImplAttr.ConstructorArguments[0].Value;
            var threadCount = (uint)autoImplAttr.ConstructorArguments[1].Value;

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
                            var typeName = x.Type.ToDisplayString();

                            if (typeName == "lychee.Commands")
                            {
                                paramKind = ParamKind.Commands;
                            }
                            else if (typeName == "lychee.Entity")
                            {
                                paramKind = ParamKind.Entity;
                            }
                            else if (typeName.StartsWith("System.Span") || typeName.StartsWith("System.ReadOnlySpan"))
                            {
                                paramKind = ParamKind.ComponentSpan;
                            }

                            if (x.GetAttributes().Any(a =>
                                {
                                    var name = a.AttributeClass.ToDisplayString();
                                    return name == "lychee.attributes.Resource";
                                }))
                            {
                                paramKind = x.Type.IsValueType ? ParamKind.StructResource : ParamKind.ClassResource;
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

                    if (memberDecl.Kind() == SyntaxKind.MethodDeclaration &&
                        methodDecl.Identifier.Text == "BeforeExecute")
                    {
                        var symbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
                        hasBeforeExecute = symbol.Parameters.Length == 0;
                    }

                    if (memberDecl.Kind() == SyntaxKind.MethodDeclaration &&
                        methodDecl.Identifier.Text == "AfterExecute")
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
                HasBeforeExecute = hasBeforeExecute,
                HasAfterExecute = hasAfterExecute,
                ThreadCount = threadCount,
                GroupSize = groupSize,
            };
        }

        private static string MakeInitializeAGCode(ParamInfo[] componentTypes, ParamInfo[] resourceTypes,
            uint threadCount)
        {
            var resourceDecl = new StringBuilder();

            foreach (var resourceType in resourceTypes)
            {
                if (resourceType.ParamKind == ParamKind.ClassResource)
                {
                    resourceDecl.AppendLine(
                        $"        ResourceDataAG.{resourceType.ParamName} = app.GetResource<{resourceType.Type}>();");
                }
                else
                {
                    resourceDecl.AppendLine(
                        $"        ResourceDataAG.{resourceType.ParamName} = app.GetResourcePtr<{resourceType.Type}>();");
                }
            }

            var registerTypes = string.Join(", ", componentTypes.Select(p =>
            {
                if (p.Type is INamedTypeSymbol named)
                {
                    var typeName = p.Type.ToDisplayString();

                    if (typeName.StartsWith("System.Span") || typeName.StartsWith("System.ReadOnlySpan"))
                    {
                        return $"app.TypeRegistrar.RegisterComponent<{named.TypeArguments[0]}>()";
                    }
                }

                return $"app.TypeRegistrar.RegisterComponent<{p.Type}>()";
            }));

            return $@"
    public unsafe void InitializeAG(App app)
    {{
        SystemDataAG.Pool = app.ResourcePool;{(threadCount > 1 ? $"\n        SystemDataAG.ThreadPool = app.CreateThreadPool({threadCount});" : "")}
        SystemDataAG.TypeIdList = [{registerTypes}];
        SystemDataAG.Commands = [{string.Join(", ", Enumerable.Repeat("app.CreateCommands()", (int)Math.Max(1, threadCount)))}];

{resourceDecl}
    }}";
        }

        private static string MakeResourceDataAGCode(ParamInfo[] resourceParams)
        {
            var declResourceCode = new StringBuilder();

            if (resourceParams.Length > 0)
            {
                declResourceCode.AppendLine("    private static class ResourceDataAG");
                declResourceCode.AppendLine("    {");

                for (var i = 0; i < resourceParams.Length; i++)
                {
                    var resourceParam = resourceParams[i];
                    var paramName = resourceParam.ParamName;

                    if (resourceParam.ParamKind == ParamKind.ClassResource)
                    {
                        declResourceCode.AppendLine($"        public static {resourceParam.Type} {paramName};");
                    }
                    else
                    {
                        declResourceCode.AppendLine($"        public static unsafe {resourceParam.Type}* {paramName};");
                    }

                    if (i != resourceParams.Length - 1)
                    {
                        declResourceCode.AppendLine();
                    }
                }

                declResourceCode.Append("    }");
            }

            return declResourceCode.ToString();
        }

        private static string MakeExecuteAGCode(ParamInfo[] allParams, ParamInfo[] componentParams,
            ParamInfo[] resourceParams, SystemInfo systemInfo, bool hasComponentSpan)
        {
            string body;
            var execParams = GenExecuteParams(allParams, systemInfo.GroupSize > 0, hasComponentSpan);
            var declResourceCode = GenResourceCode(resourceParams);

            if (componentParams.Length > 0)
            {
                body = $@"{(systemInfo.HasBeforeExecute ? "        BeforeExecute();" : "")}
{declResourceCode}
        foreach (var archetype in SystemDataAG.Archetypes)
        {{
{GenIterArchetypeCode(componentParams, execParams, hasComponentSpan, systemInfo.GroupSize)}
        }}
        {(systemInfo.HasAfterExecute ? "\n        AfterExecute();" : "")}
        return SystemDataAG.Commands;";
            }
            else
            {
                body = $@"{(systemInfo.HasBeforeExecute ? "        BeforeExecute();" : "")}
{declResourceCode}        Execute({execParams});{(systemInfo.HasAfterExecute ? "\n        AfterExecute();" : "")}

        return SystemDataAG.Commands;";
            }

            return $@"
    public unsafe ReadOnlySpan<Commands> ExecuteAG()
    {{{body}
    }}";
        }

        private static string GenResourceCode(ParamInfo[] resourceParams)
        {
            var declResourceCode = new StringBuilder();

            if (resourceParams.Length > 0)
            {
                foreach (var resourceParam in resourceParams)
                {
                    var paramName = resourceParam.ParamName;

                    if (resourceParam.ParamKind == ParamKind.StructResource)
                    {
                        declResourceCode.AppendLine(
                            $"        ref var {paramName} = ref Unsafe.AsRef<{resourceParam.Type}>(ResourceDataAG.{paramName});");
                    }
                    else if (resourceParam.ParamKind == ParamKind.ClassResource &&
                             resourceParam.RefKind != RefKind.None)
                    {
                        declResourceCode.AppendLine(
                            $"        ref var {paramName} = ref SystemDataAG.Pool.GetResourceClassRef<{resourceParam.Type}>();");
                        break;
                    }
                }
            }

            return declResourceCode.ToString();
        }

        private static string GenIterArchetypeCode(ParamInfo[] componentParams, string execParams,
            bool hasComponentSpan, uint groupSize)
        {
            return groupSize > 0
                ? GenIterArchetypeMultiThreadCode(componentParams, execParams, hasComponentSpan, groupSize)
                : GenIterArchetypeSingleThreadCode(componentParams, execParams, hasComponentSpan);
        }

        private static string GenIterArchetypeMultiThreadCode(ParamInfo[] componentParams, string execParams,
            bool hasComponentSpan, uint groupSize)
        {
            var declIterCode = new StringBuilder();

            for (var i = 0; i < componentParams.Length; i++)
            {
                declIterCode.AppendLine(
                    i == 0
                        ? $"                        var ({componentParams[i].ParamName}, size) = archetype.GetChunkData(SystemDataAG.TypeIdList[{i}], j);"
                        : $"                        var ({componentParams[i].ParamName}, _) = archetype.GetChunkData(SystemDataAG.TypeIdList[{i}], j);");
            }

            if (hasComponentSpan)
            {
                return $@"
            foreach (var (chunkIdx, chunkCount) in archetype.IterateChunksAmongType({groupSize}))
            {{
                SystemDataAG.ThreadPool.Dispatch(threadIdx =>
                {{
                    SystemDataAG.Commands[threadIdx].CurrentArchetype = archetype;
                    var beginIndex = 0;

                    for (var j = chunkIdx; j < chunkIdx + chunkCount; j++)
                    {{
{declIterCode}
                        var entitySpan = archetype.GetEntitiesSpan().Slice(beginIndex, size);

                        Execute({execParams});
                        beginIndex += size;
                    }}
                }});
            }}";
            }

            return $@"
            foreach (var (chunkIdx, chunkCount) in archetype.IterateChunksAmongType({groupSize}))
            {{
                SystemDataAG.ThreadPool.Dispatch(threadIdx =>
                {{
                    SystemDataAG.Commands[threadIdx].CurrentArchetype = archetype;
                    var beginIndex = 0;

                    for (var j = chunkIdx; j < chunkIdx + chunkCount; j++)
                    {{
{declIterCode}
                        var entitySpan = archetype.GetEntitiesSpan().Slice(beginIndex, size);
                        for (var i = 0; i < size; i++)
                        {{
                            Execute({execParams});
                        }}
                        beginIndex += size;
                    }}
                }});
            }}";
        }

        private static string GenIterArchetypeSingleThreadCode(ParamInfo[] componentParams, string execParams,
            bool hasComponentSpan)
        {
            var declIterCode = new StringBuilder();
            var iterDeclCurrentCode = new StringBuilder();
            var iterMoveNextCode = new List<string>(componentParams.Length);

            for (var i = 0; i < componentParams.Length; i++)
            {
                declIterCode.AppendLine(
                    $"            var iter{i} = archetype.IterateDataAmongChunk(SystemDataAG.TypeIdList[{i}]).GetEnumerator();");
                iterMoveNextCode.Add($"iter{i}.MoveNext()");

                iterDeclCurrentCode.AppendLine(i == 0
                    ? $"                var ({componentParams[i].ParamName}, size) = iter{i}.Current;"
                    : $"                var ({componentParams[i].ParamName}, _) = iter{i}.Current;");
            }

            if (hasComponentSpan)
            {
                return $@"{declIterCode}
            var beginIndex = 0;

            while ({string.Join(" & ", iterMoveNextCode)})
            {{
{iterDeclCurrentCode}
                var entitySpan = archetype.GetEntitiesSpan().Slice(beginIndex, size);

                Execute({execParams});
                beginIndex += size;
            }}";
            }

            return $@"{declIterCode}
            var beginIndex = 0;

            while ({string.Join(" & ", iterMoveNextCode)})
            {{
{iterDeclCurrentCode}
                var entitySpan = archetype.GetEntitiesSpan().Slice(beginIndex, size);

                for (var i = 0; i < size; i++)
                {{
                    Execute({execParams});
                }}
                beginIndex += size;
            }}";
        }

        private static string GenExecuteParams(ParamInfo[] allParams, bool multiThread, bool hasComponentSpan)
        {
            return string.Join(", ", allParams.Select((param, idx) =>
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

                    case ParamKind.ComponentSpan:
                        return $"new Span(({param.Type}*){paramName}, size)";

                    case ParamKind.ClassResource:

                        switch (param.RefKind)
                        {
                            case RefKind.In:
                            case RefKind.RefReadOnlyParameter:
                                return $"in {paramName}";
                            case RefKind.Out:
                                return $"out {paramName}";
                            case RefKind.Ref:
                                return $"ref {paramName}";
                            case RefKind.None:
                                return $"ResourceDataAG.{paramName}";
                        }

                        break;

                    case ParamKind.StructResource:
                        switch (param.RefKind)
                        {
                            case RefKind.In:
                            case RefKind.RefReadOnlyParameter:
                                return $"in {paramName}";
                            case RefKind.Out:
                                return $"out {paramName}";
                            case RefKind.Ref:
                                return $"ref {paramName}";
                            case RefKind.None:
                                return paramName;
                        }

                        break;

                    case ParamKind.Commands:
                        return multiThread ? "SystemDataAG.Commands[threadIdx]" : "SystemDataAG.Commands[0]";

                    case ParamKind.Entity:
                        return hasComponentSpan ? "entitySpan" : "entitySpan[i].Item2";
                }

                return "";
            }));
        }
    }
}
