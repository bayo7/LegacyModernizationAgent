using LegacyModernizationAgent.Analyzer.Abstractions;
using LegacyModernizationAgent.Analyzer.Graph;
using LegacyModernizationAgent.Analyzer.Models;
using Microsoft.CodeAnalysis;

namespace LegacyModernizationAgent.Analyzer.Rules;

/// <summary>
/// Circular Dependency kuralı: class → class bağımlılık grafiğinde DFS tabanlı
/// (Tarjan SCC) döngü tespiti.
///
/// Severity ayrımı (CLAUDE.md 4.1): domain modelinde parent-child gibi kasıtlı
/// karşılıklı referanslar doğaldır → aynı katman içindeki döngüler Low; farklı
/// katmanlar arası döngüler (ör. Infrastructure ↔ Application) mimari sınır ihlali
/// işaret ettiği için High alır. Katman tespiti namespace segmentlerindeki bilinen
/// katman adlarına dayanır.
/// </summary>
public sealed class CircularDependencyAnalyzer : ISolutionAnalyzer
{
    public string RuleName => "CircularDependency";
    public Severity DefaultSeverity => Severity.Medium;

    private static readonly HashSet<string> KnownLayerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Api", "Application", "Core", "Domain", "Entities", "Infrastructure",
        "Data", "DataAccess", "Persistence", "Repository", "Repositories",
        "Web", "UI", "Presentation", "Business", "BLL", "DAL", "Services"
    };

    private const string Limitation =
        "Domain modelindeki karşılıklı referanslar (ör. Order ↔ OrderLine) kasıtlı ve zararsız olabilir. " +
        "Özellikle aynı katman içindeki döngüler için tasarımın bilinçli olup olmadığı kontrol edilmelidir.";

    public async Task<IReadOnlyList<Finding>> AnalyzeAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        var edges = new HashSet<DependencyEdge>();
        var classLocations = new Dictionary<string, (string FilePath, int Line)>(StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
                continue;

            foreach (var edge in DependencyGraphBuilder.Build(compilation))
                edges.Add(edge);

            foreach (var classSymbol in DependencyGraphBuilder.GetSourceClasses(compilation))
            {
                var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource);
                if (location is not null)
                {
                    var span = location.GetLineSpan();
                    classLocations[DependencyGraphBuilder.FullName(classSymbol)] =
                        (span.Path, span.StartLinePosition.Line + 1);
                }
            }
        }

        var findings = new List<Finding>();

        foreach (var cycle in FindCycles(edges))
        {
            var isCrossLayer = cycle.Select(LayerOf).Where(l => l is not null).Distinct().Count() > 1;
            var firstClass = cycle[0];
            var location = classLocations.TryGetValue(firstClass, out var loc) ? loc : ("", 0);

            findings.Add(new Finding(
                RuleName,
                isCrossLayer ? Severity.High : Severity.Low,
                Confidence.High, // döngünün varlığı kesindir; zararlı olup olmadığı yorumu severity'de
                location.Item1,
                location.Item2,
                firstClass[(firstClass.LastIndexOf('.') + 1)..],
                (isCrossLayer
                    ? "Farklı katmanlar arasında döngüsel bağımlılık tespit edildi — mimari sınırların ihlal edilmiş olabileceğine işaret eder: "
                    : "Aynı katman içinde döngüsel bağımlılık tespit edildi (domain modelinde kasıtlı olabilir): ") +
                string.Join(" → ", cycle.Append(cycle[0])) + ". Bağımlılık yönünün gözden geçirilmesi önerilir.",
                Limitation,
                new Dictionary<string, object>
                {
                    ["CycleMembers"] = cycle.ToArray(),
                    ["IsCrossLayer"] = isCrossLayer
                }));
        }

        return findings;
    }

    /// <summary>Namespace segmentlerinden bilinen katman adını bulur (yoksa null).</summary>
    private static string? LayerOf(string fullClassName)
    {
        var segments = fullClassName.Split('.');
        // Son segment sınıf adıdır, namespace segmentlerine bakılır.
        for (var i = segments.Length - 2; i >= 0; i--)
        {
            if (KnownLayerNames.Contains(segments[i]))
                return segments[i].ToUpperInvariant();
        }

        return null;
    }

    /// <summary>
    /// Tarjan'ın strongly connected components algoritması: 1'den büyük her SCC bir
    /// döngü grubudur; self-loop'lar da döngü sayılır.
    /// </summary>
    internal static List<List<string>> FindCycles(IReadOnlyCollection<DependencyEdge> edges)
    {
        var adjacency = edges
            .GroupBy(e => e.FromClass)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ToClass).ToList());
        var nodes = edges.SelectMany(e => new[] { e.FromClass, e.ToClass }).Distinct().ToList();

        var index = 0;
        var indices = new Dictionary<string, int>();
        var lowLinks = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var stack = new Stack<string>();
        var cycles = new List<List<string>>();

        foreach (var node in nodes)
        {
            if (!indices.ContainsKey(node))
                StrongConnect(node);
        }

        return cycles;

        void StrongConnect(string node)
        {
            indices[node] = index;
            lowLinks[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            if (adjacency.TryGetValue(node, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!indices.ContainsKey(neighbor))
                    {
                        StrongConnect(neighbor);
                        lowLinks[node] = Math.Min(lowLinks[node], lowLinks[neighbor]);
                    }
                    else if (onStack.Contains(neighbor))
                    {
                        lowLinks[node] = Math.Min(lowLinks[node], indices[neighbor]);
                    }
                }
            }

            if (lowLinks[node] == indices[node])
            {
                var component = new List<string>();
                string member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                } while (member != node);

                var isSelfLoop = component.Count == 1 &&
                                 adjacency.TryGetValue(component[0], out var selfNeighbors) &&
                                 selfNeighbors.Contains(component[0]);

                if (component.Count > 1 || isSelfLoop)
                {
                    component.Reverse();
                    cycles.Add(component);
                }
            }
        }
    }
}
