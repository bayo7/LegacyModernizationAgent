using LegacyModernizationAgent.Analyzer.Abstractions;
using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyModernizationAgent.Analyzer.Rules;

/// <summary>
/// N+1 Query kuralı: foreach döngüsü içinde, döngü değişkeninin bir collection
/// navigation property'sine erişilip üzerinde LINQ çağrısı yapılması (veya iç içe
/// foreach ile gezilmesi) lazy loading altında her iterasyonda ayrı sorgu üretir.
///
/// Yanlış pozitif azaltma önlemleri:
///  - Navigation property doğrulaması SemanticModel ile yapılır (sadece isim değil):
///    property, kaynak kodda tanımlı bir entity tipinin koleksiyonu olmalı.
///  - Döngünün kaynağı .Include()/.ThenInclude() ile yüklendiyse bulgu üretilmez.
///  - Koleksiyon zaten bellekteyse sorun olmayabilir → Confidence: Medium, dil "kontrol edilmeli".
/// </summary>
public sealed class NPlusOneQueryAnalyzer : ICodeAnalyzer
{
    public string RuleName => "NPlusOneQuery";
    public Severity DefaultSeverity => Severity.High;

    private static readonly HashSet<string> TriggerMethods = new(StringComparer.Ordinal)
    {
        "ToList", "ToArray", "Count", "Any", "First", "FirstOrDefault",
        "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Sum", "Max", "Min", "Average", "Select", "Where", "Contains"
    };

    private const string Limitation =
        "Koleksiyon döngüden önce belleğe yüklendiyse (ör. Include sonrası materialize edilmiş liste) " +
        "veya lazy loading kapalıysa bu bir sorun olmayabilir. Sorgu davranışının kontrol edilmesi önerilir.";

    public IEnumerable<Finding> Analyze(SyntaxNode root, SemanticModel semanticModel, string filePath)
    {
        if (GeneratedCodeDetector.IsGeneratedFile(filePath))
            yield break;

        foreach (var forEach in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (SourceIsEagerLoaded(forEach, semanticModel))
                continue;

            if (semanticModel.GetDeclaredSymbol(forEach) is not ILocalSymbol loopVariable)
                continue;

            // Aynı navigation property için döngü başına tek bulgu yeterli.
            var reportedProperties = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in forEach.Statement.DescendantNodes())
            {
                MemberAccessExpressionSyntax? navigationAccess = node switch
                {
                    // Desen A: order.Lines.Count() gibi navigation üzerinde LINQ çağrısı
                    InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax inner } outerAccess
                    } when TriggerMethods.Contains(outerAccess.Name.Identifier.Text) => inner,

                    // Desen B: foreach (var line in order.Lines) — iç içe döngüyle gezme
                    ForEachStatementSyntax { Expression: MemberAccessExpressionSyntax nestedSource } => nestedSource,

                    _ => null
                };

                if (navigationAccess is null)
                    continue;

                if (!IsCollectionNavigationOnLoopVariable(navigationAccess, loopVariable, semanticModel, out var propertyName))
                    continue;

                if (!reportedProperties.Add(propertyName))
                    continue;

                var lineSpan = navigationAccess.GetLocation().GetLineSpan();
                var className = navigationAccess.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "";

                yield return new Finding(
                    RuleName,
                    DefaultSeverity,
                    Confidence.Medium,
                    filePath,
                    lineSpan.StartLinePosition.Line + 1,
                    className,
                    $"Döngü içinde '{loopVariable.Name}.{propertyName}' navigation property'sine erişiliyor. " +
                    "Lazy loading aktifse her iterasyon ayrı bir veritabanı sorgusu üretebilir (N+1). " +
                    "Sorgunun .Include() ile veya projeksiyonla tek seferde yüklenmesi kontrol edilmeye değer.",
                    Limitation,
                    new Dictionary<string, object>
                    {
                        ["LoopVariable"] = loopVariable.Name,
                        ["NavigationProperty"] = propertyName
                    });
            }
        }
    }

    /// <summary>
    /// Erişilen üye: döngü değişkeni üzerinden, kaynak kodda tanımlı bir entity
    /// tipinin koleksiyonunu döndüren bir property mi? (Semantic doğrulama —
    /// isim benzerliği yeterli değil.)
    /// </summary>
    private static bool IsCollectionNavigationOnLoopVariable(
        MemberAccessExpressionSyntax memberAccess,
        ILocalSymbol loopVariable,
        SemanticModel semanticModel,
        out string propertyName)
    {
        propertyName = memberAccess.Name.Identifier.Text;

        if (memberAccess.Expression is not IdentifierNameSyntax identifier)
            return false;

        var identifierSymbol = semanticModel.GetSymbolInfo(identifier).Symbol;
        if (!SymbolEqualityComparer.Default.Equals(identifierSymbol, loopVariable))
            return false;

        if (semanticModel.GetSymbolInfo(memberAccess).Symbol is not IPropertySymbol property)
            return false;

        // Kaynak kod dışındaki tipler (framework property'leri) navigation değildir.
        if (!property.Locations.Any(l => l.IsInSource))
            return false;

        return IsCollectionOfSourceDeclaredType(property.Type);
    }

    private static bool IsCollectionOfSourceDeclaredType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;

        var enumerableInterfaces = type.AllInterfaces
            .Where(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);

        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            enumerableInterfaces = enumerableInterfaces.Prepend(named);
        }

        return enumerableInterfaces.Any(i =>
            i.TypeArguments.Length == 1 &&
            i.TypeArguments[0] is { TypeKind: TypeKind.Class } element &&
            element.Locations.Any(l => l.IsInSource));
    }

    /// <summary>
    /// Döngünün kaynağı .Include()/.ThenInclude() içeriyor mu? Hem doğrudan ifadeye
    /// (foreach (var o in ctx.Orders.Include(...))) hem de kaynak bir değişkense o
    /// değişkenin metod içindeki tanımına/atamalarına bakılır.
    /// </summary>
    private static bool SourceIsEagerLoaded(ForEachStatementSyntax forEach, SemanticModel semanticModel)
    {
        if (ContainsIncludeCall(forEach.Expression))
            return true;

        if (forEach.Expression is not IdentifierNameSyntax sourceIdentifier)
            return false;

        if (semanticModel.GetSymbolInfo(sourceIdentifier).Symbol is not ILocalSymbol sourceLocal)
            return false;

        var enclosingBody = forEach.Ancestors()
            .FirstOrDefault(a => a is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or LambdaExpressionSyntax);
        if (enclosingBody is null)
            return false;

        // Değişkenin tanımı
        foreach (var declarator in enclosingBody.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Identifier.Text == sourceLocal.Name &&
                declarator.Initializer is not null &&
                ContainsIncludeCall(declarator.Initializer.Value))
            {
                return true;
            }
        }

        // Sonradan yapılan atamalar
        return enclosingBody.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
            assignment.Left is IdentifierNameSyntax left &&
            left.Identifier.Text == sourceLocal.Name &&
            ContainsIncludeCall(assignment.Right));
    }

    private static bool ContainsIncludeCall(SyntaxNode expression) =>
        expression.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(m => m.Name.Identifier.Text is "Include" or "ThenInclude");
}
