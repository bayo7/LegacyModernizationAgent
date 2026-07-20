using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyModernizationAgent.Analyzer;

/// <summary>
/// Auto-generated kodu analiz dışında tutmak için ortak filtre
/// (*.Designer.cs, Migrations, [GeneratedCode] attribute'ü, *.g.cs).
/// Bu dosyalar insan eliyle bakılmayacağı için bulgu üretmek gürültüden ibarettir.
/// </summary>
public static class GeneratedCodeDetector
{
    public static bool IsGeneratedFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasGeneratedCodeAttribute(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        return symbol is not null && HasGeneratedCodeAttribute(symbol);
    }

    public static bool HasGeneratedCodeAttribute(ISymbol symbol) =>
        symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "GeneratedCodeAttribute" or "CompilerGeneratedAttribute");
}
