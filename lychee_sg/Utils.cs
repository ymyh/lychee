using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace lychee_sg
{
    internal static class Utils
    {
        /// <summary>
        /// Gets the namespace of a syntax node.
        /// </summary>
        /// <param name="syntax"></param>
        /// <returns></returns>
        public static string GetNamespace(SyntaxNode syntax)
        {
            var namespaces = new Stack<string>();

            for (var node = syntax; node != null; node = node.Parent)
            {
                switch (node)
                {
                    case NamespaceDeclarationSyntax nsDecl:
                        namespaces.Push(nsDecl.Name.ToString());
                        break;

                    case FileScopedNamespaceDeclarationSyntax fileNsDecl:
                        namespaces.Push(fileNsDecl.Name.ToString());
                        break;
                }
            }

            if (namespaces.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(".", namespaces);
        }
    }
}