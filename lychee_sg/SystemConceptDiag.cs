using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lychee_sg
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SystemConceptDiag : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            "LYCHEE_COMPILE_ERR_1003",
            "Type must met System requirements",
            "",
            "SystemConstraint",
            DiagnosticSeverity.Error,
            true
        );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            // Debugger.Launch();

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

            var typeArgs = methodSymbol.TypeArguments;
            var typeParams = methodSymbol.TypeParameters;

            for (var i = 0; i < typeArgs.Length && i < typeParams.Length; i++)
            {
                var typeArg = typeArgs[i];
                var typeParam = typeParams[i];

                foreach (var attributeData in typeParam.GetAttributes())
                {
                    if (attributeData.AttributeClass?.Name == "SystemRequired" ||
                        attributeData.AttributeClass?.ToDisplayString() == "SystemRequired")
                    {
                        CheckRequirementMet(context, typeArg, context.Node);
                        break;
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

            var typeArgs = typeSymbol.TypeArguments;
            var typeParams = typeSymbol.TypeParameters;

            for (var i = 0; i < typeArgs.Length && i < typeParams.Length; i++)
            {
                var typeArg = typeArgs[i];
                var typeParam = typeParams[i];

                foreach (var attributeData in typeParam.GetAttributes())
                {
                    if (attributeData.AttributeClass?.Name == "SystemRequired" ||
                        attributeData.AttributeClass?.ToDisplayString() == "SystemRequired")
                    {
                        CheckRequirementMet(context, typeArg, context.Node);
                        break;
                    }
                }
            }
        }

        private static void CheckRequirementMet(SyntaxNodeAnalysisContext context, ITypeSymbol symbol, SyntaxNode node)
        {
            var maybeMet = false;

            if (symbol.TypeKind == TypeKind.Class || symbol.TypeKind == TypeKind.Struct)
            {
                var interfaces = (symbol as INamedTypeSymbol).Interfaces;

                foreach (var namedTypeSymbol in interfaces)
                {
                    if (namedTypeSymbol.ToDisplayString() == "lychee.interfaces.ISystem")
                    {
                        maybeMet = true;
                        break;
                    }
                }

                if (maybeMet)
                {
                    if (symbol.GetMembers("Execute")
                        .OfType<IMethodSymbol>()
                        .All(m => m.DeclaredAccessibility != Accessibility.Public))
                    {
                        Report(context, node, $"{symbol.Name} must contains a public instance method named Execute");
                    }
                }
                else
                {
                    Report(context, node, $"{symbol.Name} must implement ISystem");
                }
            }
        }

        private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode node, string message)
        {
            var diag = Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: "LYCHEE_COMPILE_ERR_1003",
                    title: "System constraint not satisfied",
                    messageFormat: message,
                    category: "Constraint",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                node.GetLocation());

            context.ReportDiagnostic(diag);
        }
    }
}
