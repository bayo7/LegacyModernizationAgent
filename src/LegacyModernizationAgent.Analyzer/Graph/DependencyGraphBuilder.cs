using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;

namespace LegacyModernizationAgent.Analyzer.Graph;

/// <summary>
/// Class → class bağımlılık grafiğini çıkarır. Bir sınıfın field'ları, property'leri,
/// metod parametreleri/dönüş tipleri ve base type'ı üzerinden, kaynak kodda tanımlı
/// diğer sınıflara giden kenarlar toplanır. Framework tipleri (List, string...) grafiğe girmez.
/// </summary>
public static class DependencyGraphBuilder
{
    public static IReadOnlyList<DependencyEdge> Build(Compilation compilation)
    {
        var edges = new HashSet<DependencyEdge>();

        foreach (var classSymbol in GetSourceClasses(compilation))
        {
            var fromName = FullName(classSymbol);

            foreach (var referenced in GetReferencedTypes(classSymbol))
            {
                var toName = FullName(referenced);
                if (fromName != toName)
                    edges.Add(new DependencyEdge(fromName, toName));
            }
        }

        return edges.ToList();
    }

    public static IEnumerable<INamedTypeSymbol> GetSourceClasses(Compilation compilation)
    {
        var stack = new Stack<INamespaceSymbol>();
        stack.Push(compilation.Assembly.GlobalNamespace);

        while (stack.Count > 0)
        {
            var ns = stack.Pop();
            foreach (var member in ns.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol childNs:
                        stack.Push(childNs);
                        break;
                    case INamedTypeSymbol { TypeKind: TypeKind.Class } type when type.Locations.Any(l => l.IsInSource):
                        yield return type;
                        break;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetReferencedTypes(INamedTypeSymbol classSymbol)
    {
        IEnumerable<ITypeSymbol> candidates = classSymbol.GetMembers().SelectMany(member => member switch
        {
            IFieldSymbol field => [field.Type],
            IPropertySymbol property => [property.Type],
            IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.Constructor } method =>
                method.Parameters.Select(p => p.Type).Append(method.ReturnType),
            _ => Enumerable.Empty<ITypeSymbol>()
        });

        if (classSymbol.BaseType is { SpecialType: SpecialType.None } baseType)
            candidates = candidates.Append(baseType);

        foreach (var candidate in candidates)
        {
            foreach (var unwrapped in Unwrap(candidate))
            {
                if (unwrapped is INamedTypeSymbol { TypeKind: TypeKind.Class } named &&
                    named.Locations.Any(l => l.IsInSource))
                {
                    yield return named;
                }
            }
        }
    }

    /// <summary>Koleksiyon/generic sarmalayıcıların içindeki asıl tipe iner (List&lt;Order&gt; → Order).</summary>
    private static IEnumerable<ITypeSymbol> Unwrap(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            foreach (var inner in Unwrap(array.ElementType))
                yield return inner;
            yield break;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } generic)
        {
            foreach (var argument in generic.TypeArguments)
                foreach (var inner in Unwrap(argument))
                    yield return inner;

            // Generic tipin kendisi de kaynak kodda tanımlıysa (ör. kendi Repository<T>'miz) kenar sayılır.
            if (generic.Locations.Any(l => l.IsInSource))
                yield return generic;

            yield break;
        }

        yield return type;
    }

    public static string FullName(INamedTypeSymbol symbol) =>
        symbol.ContainingNamespace.IsGlobalNamespace
            ? symbol.Name
            : $"{symbol.ContainingNamespace.ToDisplayString()}.{symbol.Name}";
}
