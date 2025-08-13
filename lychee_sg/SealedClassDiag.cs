using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lychee_sg
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SealedRequiredAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            "LYCHEE_COMPILE_ERR_1001",
            "Generic type argument must be sealed",
            "Type argument '{0}' for generic '{1}' must be a sealed class",
            "SealedConstraint",
            DiagnosticSeverity.Error,
            true
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
            if (!(symbolInfo.Symbol is IMethodSymbol methodSymbol))
            {
                return;
            }

            if (!methodSymbol.IsGenericMethod)
            {
                return;
            }

            // if (!HasSealedRequired(methodSymbol))
            // {
            //     return;
            // }

            var typeArgs = methodSymbol.TypeArguments;
            var typeParams = methodSymbol.TypeParameters;

            for (var i = 0; i < typeArgs.Length && i < typeParams.Length; i++)
            {
                var typeArg = typeArgs[i];
                var typeParam = typeParams[i];

                foreach (var attributeData in typeParam.GetAttributes())
                {
                    if (attributeData.AttributeClass?.Name == "SealedRequired" ||
                        attributeData.AttributeClass?.ToDisplayString() == "SealedRequired")
                    {
                        if (typeArg.TypeKind == TypeKind.Class && !typeArg.IsSealed)
                        {
                            var diag = Diagnostic.Create(
                                Rule,
                                invocation.GetLocation(),
                                typeArg.ToDisplayString(),
                                methodSymbol.Name
                            );
                            context.ReportDiagnostic(diag);
                        }
                    }
                }
            }
        }

        private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
        {
            var creation = (ObjectCreationExpressionSyntax)context.Node;
            var symbolInfo = context.SemanticModel.GetSymbolInfo(creation);
            var ctorSymbol = symbolInfo.Symbol as IMethodSymbol;

            var typeSymbol = ctorSymbol?.ContainingType;
            if (typeSymbol == null || !typeSymbol.IsGenericType)
            {
                return;
            }

            // if (!HasSealedRequired(typeSymbol))
            // {
            //     return;
            // }

            var typeArgs = typeSymbol.TypeArguments;
            var typeParams = typeSymbol.TypeParameters;

            for (var i = 0; i < typeArgs.Length && i < typeParams.Length; i++)
            {
                var typeArg = typeArgs[i];
                var typeParam = typeParams[i];

                foreach (var attributeData in typeParam.GetAttributes())
                {
                    if (attributeData.AttributeClass?.Name == "SealedRequired" ||
                        attributeData.AttributeClass?.ToDisplayString() == "SealedRequired")
                    {
                        if (typeArg.TypeKind == TypeKind.Class && !typeArg.IsSealed)
                        {
                            var diag = Diagnostic.Create(
                                Rule,
                                creation.GetLocation(),
                                typeArg.ToDisplayString(),
                                typeSymbol.Name
                            );
                            context.ReportDiagnostic(diag);
                        }
                    }
                }
            }
        }

        private static bool HasSealedRequired(ISymbol symbol)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass != null &&
                    (attrClass.Name == "SealedRequired" || attrClass.ToDisplayString() == "SealedRequired"))
                {
                    return true;
                }
            }

            return false;
        }
    }
}