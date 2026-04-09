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

        public bool MultiThread;
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

        public bool ResourceRequireOnExec;
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
using lychee.extensions;
using ThreadPool = lychee.threading.ThreadPool;

namespace {sysInfo.Namespace};

partial class {sysInfo.Name} : ISystem
{{
    private static class SystemDataAG
    {{
        public static ResourcePool Pool;{(sysInfo.MultiThread ? "\n        public static ThreadPool ThreadPool;" : "")}

        public static SystemDescriptor descriptor;

        public static int[] TypeIdList;

        public static Archetype[] Archetypes = [];

        public static Commands[] Commands;

        public static int LastArchetypeIdx = 0;
    }}
{MakeResourceDataAGCode(resourceTypes)}
{MakeInitializeAGCode(componentTypes, resourceTypes, sysInfo.MultiThread)}

    public void ConfigureAG(App app, SystemFilterInfo filterInfo)
    {{
        SystemDataAG.Archetypes = SystemDataAG.Archetypes.ConcatCollection(app.World.ArchetypeManager.MatchArchetypesByPredicate(filterInfo.AllFilter, filterInfo.AnyFilter, filterInfo.NoneFilter, SystemDataAG.TypeIdList, ref SystemDataAG.LastArchetypeIdx));
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
            var resourceRequireOnExec = false;

            var multiThread = (bool)autoImplAttr.ConstructorArguments[0].Value;

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

                            var attr = x.GetAttributes().FirstOrDefault(a =>
                            {
                                var name = a.AttributeClass.ToDisplayString();
                                return name == "lychee.attributes.Resource";
                            });

                            if (attr != null)
                            {
                                paramKind = x.Type.IsValueType ? ParamKind.StructResource : ParamKind.ClassResource;
                                resourceRequireOnExec = (bool)attr.ConstructorArguments[1].Value;
                            }

                            return new ParamInfo
                            {
                                Type = x.Type,
                                RefKind = x.RefKind,
                                ParamKind = paramKind,
                                ParamName = x.Name,
                                ResourceRequireOnExec = resourceRequireOnExec
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
                MultiThread = multiThread,
            };
        }

        private static string MakeInitializeAGCode(ParamInfo[] componentTypes, ParamInfo[] resourceTypes, bool multiThread)
        {
            var resourceDecl = new StringBuilder();

            foreach (var resourceType in resourceTypes)
            {
                if (!resourceType.ResourceRequireOnExec)
                {
                    if (resourceType.ParamKind == ParamKind.ClassResource)
                    {
                        if (resourceType.RefKind == RefKind.None)
                        {
                            resourceDecl.AppendLine(
                                $"        ResourceDataAG.{resourceType.ParamName} = app.GetResource<{resourceType.Type}>();");
                        }
                    }
                    else
                    {
                        resourceDecl.AppendLine(
                            $"        ResourceDataAG.{resourceType.ParamName} = app.GetResourcePtr<{resourceType.Type}>();");
                    }
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
    public unsafe void InitializeAG(App app, SystemDescriptor descriptor)
    {{
        SystemDataAG.Pool = app.ResourcePool;{(multiThread ? $"\n        SystemDataAG.ThreadPool = app.CreateThreadPool(SystemDataAG.descriptor.ThreadCount);" : "")}
        SystemDataAG.descriptor = descriptor;
        SystemDataAG.TypeIdList = [{registerTypes}];
        SystemDataAG.Commands = new Commands[Math.Max(1, SystemDataAG.descriptor.ThreadCount)];

        for (var i = 0; i < SystemDataAG.Commands.Length; i++) SystemDataAG.Commands[i] = new(app);

{resourceDecl}
    }}";
        }

        private static string MakeResourceDataAGCode(ParamInfo[] resourceParams)
        {
            var declResourceCode = new StringBuilder();

            if (resourceParams.Length == 0)
            {
                return "";
            }

            for (var i = 0; i < resourceParams.Length; i++)
            {
                var resourceParam = resourceParams[i];
                var paramName = resourceParam.ParamName;

                if (!resourceParam.ResourceRequireOnExec)
                {
                    if (resourceParam.ParamKind == ParamKind.ClassResource)
                    {
                        if (resourceParam.RefKind == RefKind.None)
                        {
                            declResourceCode.AppendLine($"        public static {resourceParam.Type} {paramName};");
                        }
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
            }

            var code = $@"
    private static class ResourceDataAG
    {{
{declResourceCode}
    }}
";

            return code;
        }

        private static string MakeExecuteAGCode(ParamInfo[] allParams, ParamInfo[] componentParams,
            ParamInfo[] resourceParams, SystemInfo systemInfo, bool hasComponentSpan)
        {
            string body;
            var execParams = GenExecuteParams(allParams, systemInfo.MultiThread, hasComponentSpan);
            var declResourceCode = GenResourceCode(resourceParams);

            if (componentParams.Length > 0)
            {
                body = $@"{(systemInfo.HasBeforeExecute ? "\n        BeforeExecute();" : "")}
{declResourceCode}
        foreach (var archetype in SystemDataAG.Archetypes)
        {{
{GenIterArchetypeCode(componentParams, execParams, hasComponentSpan, systemInfo.MultiThread)}
        }}
        {(systemInfo.MultiThread ? "\n        SystemDataAG.ThreadPool.Wait();" : "")}
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
    public unsafe Commands[] ExecuteAG()
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

                    if (resourceParam.ResourceRequireOnExec)
                    {
                        if (resourceParam.ParamKind == ParamKind.StructResource)
                        {
                            declResourceCode.AppendLine(
                                $"        ref var {paramName} = ref SystemDataAG.Pool.GetResourceStructRef<{resourceParam.Type}>();");
                        }
                        else if (resourceParam.ParamKind == ParamKind.ClassResource)
                        {
                            if (resourceParam.RefKind != RefKind.None)
                            {
                                declResourceCode.AppendLine(
                                    $"        ref var {paramName} = ref SystemDataAG.Pool.GetResourceClassRef<{resourceParam.Type}>();");
                            }
                            else
                            {
                                declResourceCode.AppendLine(
                                    $"        var {paramName} = SystemDataAG.Pool.GetResource<{resourceParam.Type}>();");
                            }
                        }

                        continue;
                    }

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
                    }
                }
            }

            return declResourceCode.ToString();
        }

        private static string GenIterArchetypeCode(ParamInfo[] componentParams, string execParams,
            bool hasComponentSpan, bool multiThread)
        {
            return multiThread
                ? GenIterArchetypeMultiThreadCode(componentParams, execParams, hasComponentSpan)
                : GenIterArchetypeSingleThreadCode(componentParams, execParams, hasComponentSpan);
        }

        private static string GenIterArchetypeMultiThreadCode(ParamInfo[] componentParams, string execParams,
            bool hasComponentSpan)
        {
            var declIterCode = new StringBuilder();

            for (var i = 0; i < componentParams.Length; i++)
            {
                declIterCode.AppendLine($"                        var {componentParams[i].ParamName} = archetype.GetChunkData<{componentParams[i].Type}>(SystemDataAG.TypeIdList[{i}], j);");
            }

            if (hasComponentSpan)
            {
                return $@"
            foreach (var (chunkIdx, chunkCount) in archetype.IterateChunksAmongType(SystemDataAG.descriptor.GroupSize))
            {{
                SystemDataAG.ThreadPool.Dispatch(threadIdx =>
                {{
                    var beginIndex = 0;

                    for (var j = chunkIdx; j < chunkIdx + chunkCount; j++)
                    {{
{declIterCode}
                        var size = {componentParams[0].ParamName}.Length;
                        var entitySpan = archetype.GetEntitiesSpan().Slice(beginIndex, size);

                        Execute({execParams});
                        beginIndex += size;
                    }}
                }});
            }}";
            }

            return $@"
            foreach (var (chunkIdx, chunkCount) in archetype.IterateChunksAmongType(SystemDataAG.descriptor.GroupSize))
            {{
                SystemDataAG.ThreadPool.Dispatch(threadIdx =>
                {{
                    var beginIndex = 0;

                    for (var j = chunkIdx; j < chunkIdx + chunkCount; j++)
                    {{
{declIterCode}
                        var size = {componentParams[0].ParamName}.Length;
                        var entitySpan = archetype.GetEntitiesSpan().Slice(beginIndex, size);
                        for (var i = 0; i < size; i++)
                        {{
                            var entity = new Entity(SystemDataAG.Commands[threadIdx], archetype, entitySpan[i].Item2, new(j, i));
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
                    $"            var iter{i} = archetype.IterateDataAmongChunk<{componentParams[i].Type}>(SystemDataAG.TypeIdList[{i}]).GetEnumerator();");
                iterMoveNextCode.Add($"iter{i}.MoveNext()");

                iterDeclCurrentCode.AppendLine($"                var {componentParams[i].ParamName} = iter{i}.Current.Span;");
            }

            if (hasComponentSpan)
            {
                return $@"{declIterCode}
            var beginIndex = 0;

            while ({string.Join(" & ", iterMoveNextCode)})
            {{
{iterDeclCurrentCode}
                var size = {componentParams[0].ParamName}.Length;
                var entitySpan = archetype.GetEntitiesSpan().Slice(beginIndex, size);

                Execute({execParams});
                beginIndex += size;
            }}";
            }

            return $@"{declIterCode}
            var beginIndex = 0;
            var chunkIdx = 0;

            while ({string.Join(" & ", iterMoveNextCode)})
            {{
{iterDeclCurrentCode}
                var size = {componentParams[0].ParamName}.Length;
                var entitySpan = archetype.GetEntitiesSpan().Slice(beginIndex, size);

                for (var i = 0; i < size; i++)
                {{
                    var entity = new Entity(SystemDataAG.Commands[0], archetype, entitySpan[i].Item2, new(chunkIdx, i));
                    Execute({execParams});
                }}
                beginIndex += size;
                chunkIdx++;
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
                        var derefCode = $"{paramName}[i]";
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
                        return paramName;

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

                                if (!param.ResourceRequireOnExec)
                                {
                                    return $"ResourceDataAG.{paramName}";
                                }

                                return paramName;
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
                        if (hasComponentSpan)
                        {
                            return "entitySpan";
                        }

                        switch (param.RefKind)
                        {
                            case RefKind.In:
                            case RefKind.RefReadOnlyParameter:
                                return "in entity";
                            case RefKind.Out:
                                return "out entity";
                            case RefKind.Ref:
                                return "ref entity";
                            case RefKind.None:
                                return "entity";

                            default:
                                return "";
                        }
                }

                return "";
            }));
        }
    }
}
