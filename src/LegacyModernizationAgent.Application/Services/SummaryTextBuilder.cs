using System.Text;
using CoreEntities = LegacyModernizationAgent.Core.Entities;

namespace LegacyModernizationAgent.Application.Services;

/// <summary>
/// Embedding'e gönderilecek class özet metnini CLAUDE.md 4.2'deki şablona göre üretir:
/// Class / Namespace / Public Metodlar / XML Doc / İlişkili Bulgular.
/// </summary>
public static class SummaryTextBuilder
{
    public static string Build(
        Analyzer.Models.ClassSummary summary,
        IReadOnlyList<CoreEntities.Finding> findingsForClass)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Class: {summary.ClassName}");
        sb.AppendLine($"Namespace: {summary.Namespace}");
        sb.AppendLine($"Public Metodlar: {string.Join("; ", summary.PublicMethodSignatures)}");

        if (!string.IsNullOrWhiteSpace(summary.XmlDocSummary))
            sb.AppendLine($"XML Doc: {summary.XmlDocSummary}");

        if (findingsForClass.Count > 0)
        {
            sb.AppendLine("İlişkili Bulgular:");
            foreach (var finding in findingsForClass)
                sb.AppendLine($"- [{finding.RuleName}/{finding.Severity}] {finding.Description}");
        }

        return sb.ToString().TrimEnd();
    }
}
