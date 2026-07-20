namespace LegacyModernizationAgent.Analyzer.Models;

/// <summary>
/// Bulgunun tahmini önem derecesi. Bu bir öneri sinyalidir, kesin bir yargı değildir
/// (bkz. CLAUDE.md Bölüm 4.1 — her kuralın yanılma payı vardır).
/// </summary>
public enum Severity
{
    Low,
    Medium,
    High
}
