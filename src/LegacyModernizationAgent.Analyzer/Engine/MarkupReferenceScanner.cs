using System.Text.RegularExpressions;

namespace LegacyModernizationAgent.Analyzer.Engine;

/// <summary>
/// .razor / .cshtml dosyalarında geçen tanımlayıcıları toplar.
///
/// Neden var: Razor dosyalarındaki C# kodu, Razor kaynak üreticisi çalışmadığında
/// compilation'a girmez; SymbolFinder bu dosyalardan yapılan çağrıları göremez ve
/// yalnızca bir .razor bileşeninden çağrılan metodlar "kullanılmayan kod" olarak
/// işaretlenir (eShopOnWeb/BlazorAdmin'de gözlenen yanlış pozitif). Üreticinin çalışıp
/// çalışmadığına güvenmek yerine metinsel bir güvenlik ağı kullanılır: metod adı herhangi
/// bir işaretleme dosyasında geçiyorsa bulgu üretilmez.
///
/// Sınırı: ad eşleşmesidir, sembol çözümlemesi değil — aynı adlı başka bir metodun
/// çağrısı da muafiyet sağlar. Bu kural için yanlış negatif, yanlış pozitiften ucuzdur.
/// </summary>
internal static class MarkupReferenceScanner
{
    private static readonly string[] MarkupPatterns = ["*.razor", "*.cshtml"];
    private static readonly Regex Identifier = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    public static HashSet<string> CollectIdentifiers(IEnumerable<string> projectDirectories)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var directory in projectDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var pattern in MarkupPatterns)
            {
                foreach (var file in EnumerateFilesSafe(directory, pattern))
                {
                    if (IsBuildOutput(file))
                        continue;

                    try
                    {
                        AddIdentifiers(File.ReadAllText(file), identifiers);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Okunamayan dosya yalnızca muafiyet kapsamını daraltır; analizi durdurmaz.
                    }
                }
            }
        }

        return identifiers;
    }

    internal static void AddIdentifiers(string text, HashSet<string> identifiers)
    {
        foreach (Match match in Identifier.Matches(text))
            identifiers.Add(match.Value);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsBuildOutput(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }
}
