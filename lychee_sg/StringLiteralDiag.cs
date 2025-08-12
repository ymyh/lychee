using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace lychee_sg
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LiteralStringAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            id: "LYCHEE_COMPILE_ERR_1002",
            title: "Argument must be a string literal",
            messageFormat: "Argument \"{0}\" must be a string literal",
            category: "ArgumentConstraint",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        }

        private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol == null) return;

            CheckArguments(context, symbol, invocation.ArgumentList.Arguments);
        }

        private void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
        {
            var creation = (ObjectCreationExpressionSyntax)context.Node;
            var symbol = context.SemanticModel.GetSymbolInfo(creation).Symbol as IMethodSymbol;
            if (symbol == null) return;

            CheckArguments(context, symbol, creation.ArgumentList?.Arguments ?? default);
        }

        private void CheckArguments(SyntaxNodeAnalysisContext context, IMethodSymbol method,
            SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            for (var i = 0; i < method.Parameters.Length && i < arguments.Count; i++)
            {
                var parameter = method.Parameters[i];
                var (exist, dynamic) = ExtractStringLiteralAttribute(parameter);
                if (!exist)
                {
                    continue;
                }

                var argExpr = arguments[i].Expression;

                // const string counts as literal
                if (argExpr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    continue;
                }

                // interpolated string counts as literal
                if (argExpr is InterpolatedStringExpressionSyntax)
                {
                    if (dynamic)
                    {
                        continue;
                    }

                    var constVal = context.SemanticModel.GetConstantValue(argExpr);
                    if (constVal.HasValue && constVal.Value is string)
                    {
                        continue;
                    }
                }

                var constantValue = context.SemanticModel.GetConstantValue(argExpr);
                if (constantValue.HasValue && constantValue.Value is string)
                {
                    continue;
                }

                // otherwise report error
                context.ReportDiagnostic(Diagnostic.Create(Rule, argExpr.GetLocation(), parameter.Name));
            }
        }

        private (bool, bool) ExtractStringLiteralAttribute(IParameterSymbol parameter)
        {
            var attributes = parameter.GetAttributes();
            foreach (var attr in attributes)
            {
                var dynamic = false;
                if (attr.AttributeClass?.Name == "StringLiteral" ||
                    attr.AttributeClass?.ToDisplayString() == "StringLiteral")
                {
                    foreach (var arg in attr.ConstructorArguments)
                    {
                        if (arg.Value is bool b)
                        {
                            dynamic = b;
                        }
                    }

                    return (true, dynamic);
                }
            }

            return (false, false);
        }
    }
}
