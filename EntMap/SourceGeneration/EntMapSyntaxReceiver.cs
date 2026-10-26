using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntMap.SourceGeneration
{
    /// <summary>
    /// 
    /// </summary>
    internal class EntMapSyntaxReceiver : ISyntaxReceiver
    {
        public List<MethodDeclarationSyntax> CandidateMethods { get; } = [];

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            // Фильтруем partial методы с атрибутом GenerateMap
            if (syntaxNode is MethodDeclarationSyntax methodDeclaration &&
                methodDeclaration.AttributeLists.Count > 0 &&
                methodDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
            {
                CandidateMethods.Add(methodDeclaration);
            }
        }
    }
}