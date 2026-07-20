using System.Diagnostics;
using System.IO.Compression;
using LegacyModernizationAgent.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyModernizationAgent.Infrastructure.Sources;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Klonlanan repoların ve açılan zip'lerin kök klasörü.
    /// Sistem temp dizini altında tutulur — Api projesinin kendi kök dizini altında olsaydı,
    /// SDK-style .csproj'un varsayılan derleme glob'u klonlanan reponun .cs dosyalarını
    /// Api derlemesine dahil edip build'i kırardı (yaşanmış bir hata).
    /// </summary>
    public string WorkRoot { get; set; } = Path.Combine(Path.GetTempPath(), "LegacyModernizationAgent", "workspaces");
}

/// <summary>
/// Analiz kaynağını hazırlar: git URL → shallow clone; .zip → aç; lokal yol → doğrula.
/// </summary>
public class GitSourcePreparer(IOptions<StorageOptions> options, ILogger<GitSourcePreparer> logger) : ISourcePreparer
{
    private readonly StorageOptions _options = options.Value;

    public async Task<string> PrepareAsync(string repoUrlOrPath, Guid jobId, CancellationToken ct = default)
    {
        if (IsGitUrl(repoUrlOrPath))
            return await CloneAsync(repoUrlOrPath, jobId, ct);

        if (File.Exists(repoUrlOrPath))
        {
            if (Path.GetExtension(repoUrlOrPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return ExtractZip(repoUrlOrPath, jobId);

            return repoUrlOrPath; // .sln veya .csproj doğrudan
        }

        if (Directory.Exists(repoUrlOrPath))
            return repoUrlOrPath;

        throw new FileNotFoundException($"Analiz kaynağı bulunamadı: {repoUrlOrPath}");
    }

    private static bool IsGitUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("git@", StringComparison.OrdinalIgnoreCase);

    private async Task<string> CloneAsync(string url, Guid jobId, CancellationToken ct)
    {
        var targetDir = Path.Combine(_options.WorkRoot, jobId.ToString());
        Directory.CreateDirectory(_options.WorkRoot);

        logger.LogInformation("Repo klonlanıyor: {Url} → {Dir}", url, targetDir);

        // Windows'ta 260 karakter MAX_PATH sınırı nedeniyle uzun asset yollu repolarda
        // ("Filename too long") clone başarısız olabiliyor; core.longpaths bunu aşar.
        var psi = new ProcessStartInfo("git", $"-c core.longpaths=true clone --depth 1 \"{url}\" \"{targetDir}\"")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("git süreci başlatılamadı (git PATH'te mi?).");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git clone başarısız (exit {process.ExitCode}): {stderr}");

        return targetDir;
    }

    private string ExtractZip(string zipPath, Guid jobId)
    {
        var targetDir = Path.Combine(_options.WorkRoot, jobId.ToString());
        logger.LogInformation("Zip açılıyor: {Zip} → {Dir}", zipPath, targetDir);
        ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
        return targetDir;
    }
}
