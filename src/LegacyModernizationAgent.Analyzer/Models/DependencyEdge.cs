namespace LegacyModernizationAgent.Analyzer.Models;

/// <summary>
/// Class → class bağımlılık grafiğinin bir kenarı. Hem circular dependency tespiti
/// hem de dashboard'daki bağımlılık grafiği görselleştirmesi bu veriden beslenir.
/// </summary>
public record DependencyEdge(string FromClass, string ToClass);
