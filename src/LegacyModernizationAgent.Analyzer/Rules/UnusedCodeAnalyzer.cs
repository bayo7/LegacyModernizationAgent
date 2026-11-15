using LegacyModernizationAgent.Analyzer.Abstractions;
using LegacyModernizationAgent.Analyzer.Engine;
using LegacyModernizationAgent.Analyzer.Graph;
using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace LegacyModernizationAgent.Analyzer.Rules;

/// <summary>
/// Kullanılmayan Kod kuralı: SymbolFinder.FindReferencesAsync ile solution genelinde
/// hiç referansı olmayan public/internal metodları işaretler.
///
/// Bu, 5 kuralın en riskli olanıdır (reflection, DI, routing, dışa açık API yüzeyi
/// statik analizle görünmez) → Confidence: Low ve zorunlu muafiyet filtreleri:
///  - Controller action'ları (routing ile çağrılır)
///  - Interface implementasyonları (çağrı interface üzerinden yapılır)
///  - virtual/override/abstract metodlar (polimorfik çağrı görünmez)
///  - Main, test metodları ([Fact], [Test]...), [GeneratedCode]
///  - Adı bir .razor/.cshtml dosyasında geçen metodlar (Razor'dan çağrı compilation'da görünmeyebilir)
/// </summary>
public sealed class UnusedCodeAnalyzer : ISolutionAnalyzer
{
    public string RuleName => "UnusedCode";
    public Severity DefaultSeverity => Severity.Low;

    private static readonly HashSet<string> FrameworkInvokedAttributes = new(StringComparer.Ordinal)
    {
        "FactAttribute", "TheoryAttribute", "TestAttribute", "TestMethodAttribute",
        "BenchmarkAttribute", "SetUpAttribute", "TearDownAttribute",
        "OneTimeSetUpAttribute", "OneTimeTearDownAttribute",
        "TestInitializeAttribute", "TestCleanupAttribute",
        "AssemblyInitializeAttribute", "AssemblyCleanupAttribute",
        "ClassInitializeAttribute", "ClassCleanupAttribute",
        "HttpGetAttribute", "HttpPostAttribute", "HttpPutAttribute",
        "HttpDeleteAttribute", "HttpPatchAttribute", "JSInvokableAttribute"
    };

    private const string Limitation =
        "Reflection, dependency injection, routing veya harici tüketiciler tarafından çağrılan metodlar " +
        "statik analizde referanssız görünür. Bu bulgu SİLİNMELİ anlamına gelmez; yalnızca gözden geçirme önerisidir.";

    public async Task<IReadOnlyList<Finding>> AnalyzeAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        // .razor/.cshtml dosyalarından yapılan çağrılar compilation'da görünmeyebilir;
        // bu dosyalarda adı geçen metodlar muaf tutulur (bkz. MarkupReferenceScanner).
        var markupIdentifiers = MarkupReferenceScanner.CollectIdentifiers(
            solution.Projects
                .Select(p => Path.GetDirectoryName(p.FilePath))
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d!));

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
                continue;

            foreach (var classSymbol in DependencyGraphBuilder.GetSourceClasses(compilation))
            {
                if (GeneratedCodeDetector.HasGeneratedCodeAttribute(classSymbol))
                    continue;

                var isFrameworkInvoked = IsFrameworkInvokedType(classSymbol);

                foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    if (!IsCandidate(method, isFrameworkInvoked))
                        continue;

                    if (markupIdentifiers.Contains(method.Name))
                        continue;

                    var references = await SymbolFinder.FindReferencesAsync(method, solution, cancellationToken);
                    var referenceCount = references.Sum(r => r.Locations.Count());
                    if (referenceCount > 0)
                        continue;

                    var location = method.Locations.FirstOrDefault(l => l.IsInSource);
                    if (location is null)
                        continue;

                    var span = location.GetLineSpan();
                    if (GeneratedCodeDetector.IsGeneratedFile(span.Path))
                        continue;

                    findings.Add(new Finding(
                        RuleName,
                        DefaultSeverity,
                        Confidence.Low,
                        span.Path,
                        span.StartLinePosition.Line + 1,
                        classSymbol.Name,
                        $"'{classSymbol.Name}.{method.Name}' metoduna solution içinde hiçbir referans bulunamadı. " +
                        "Gerçekten kullanılmıyorsa kaldırılması kod tabanını sadeleştirebilir; " +
                        "ancak reflection/DI/harici tüketici ihtimali mutlaka kontrol edilmelidir.",
                        Limitation,
                        new Dictionary<string, object>
                        {
                            ["MethodName"] = method.Name,
                            ["Accessibility"] = method.DeclaredAccessibility.ToString()
                        }));
                }
            }
        }

        return findings;
    }

    private static bool IsCandidate(IMethodSymbol method, bool isFrameworkInvoked)
    {
        if (method.MethodKind != MethodKind.Ordinary)
            return false;

        // Derleyici üretimi üyeler (record'ların Deconstruct/Clone metodları gibi)
        // kullanıcı kodu değildir; işaretlemek yalnızca gürültü üretir.
        if (method.IsImplicitlyDeclared)
            return false;

        if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            return false;

        // Polimorfik/arayüz üzerinden çağrılar statik referans olarak görünmez → muaf.
        if (method.IsVirtual || method.IsOverride || method.IsAbstract)
            return false;

        if (method is { Name: "Main", IsStatic: true })
            return false;

        // Controller action'ları, Razor Page handler'ları vb. routing/framework ile çağrılır.
        if (isFrameworkInvoked && method.DeclaredAccessibility == Accessibility.Public)
            return false;

        // Middleware konvansiyonu: Invoke/InvokeAsync runtime tarafından çağrılır.
        if (method.Name is "Invoke" or "InvokeAsync" &&
            method.ContainingType.Name.EndsWith("Middleware", StringComparison.Ordinal))
        {
            return false;
        }

        if (method.GetAttributes().Any(a =>
                a.AttributeClass is { } attr &&
                (FrameworkInvokedAttributes.Contains(NormalizeAttributeName(attr.Name)) || attr.Name == "GeneratedCodeAttribute")))
        {
            return false;
        }

        // Tipin taban sınıfı ya da arayüzlerinden biri çözümlenemiyorsa (restore edilmemiş
        // paket), metodun bir arayüz/taban üyesini karşılayıp karşılamadığı bilinemez.
        // Bilinmeyeni "kullanılmıyor" diye raporlamak yerine sessiz kalınır.
        if (HasUnresolvedBaseOrInterface(method.ContainingType))
            return false;

        // Interface implementasyonu mu? (explicit veya implicit)
        if (method.ExplicitInterfaceImplementations.Length > 0)
            return false;

        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            foreach (var interfaceMember in iface.GetMembers().OfType<IMethodSymbol>())
            {
                var implementation = method.ContainingType.FindImplementationForInterfaceMember(interfaceMember);
                if (SymbolEqualityComparer.Default.Equals(implementation, method))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Çözümlenemeyen attribute'larda (ör. xunit restore edilmemişken [Fact]) sembol adı
    /// kaynakta yazıldığı gibi gelebilir ("Fact"); karşılaştırma için "…Attribute" biçimine getirilir.
    /// </summary>
    private static string NormalizeAttributeName(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal) ? name : name + "Attribute";

    private static bool HasUnresolvedBaseOrInterface(INamedTypeSymbol type)
    {
        if (type.AllInterfaces.Any(i => i.TypeKind == TypeKind.Error))
            return true;

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.TypeKind == TypeKind.Error)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Public metodları framework tarafından (routing, render pipeline, hub dispatch)
    /// çağrılan tipler: Controller, Razor PageModel, Blazor component, SignalR Hub.
    /// Bu tiplerin public metodlarına "referans yok" demek anlamsızdır.
    /// </summary>
    private static bool IsFrameworkInvokedType(INamedTypeSymbol classSymbol)
    {
        if (classSymbol.Name.EndsWith("Controller", StringComparison.Ordinal))
            return true;

        if (classSymbol.GetAttributes().Any(a => a.AttributeClass?.Name == "ApiControllerAttribute"))
            return true;

        for (var baseType = classSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.Name is "ControllerBase" or "Controller" or "ApiController"
                or "PageModel" or "ComponentBase" or "Hub" or "ViewComponent")
            {
                return true;
            }
        }

        return false;
    }
}
