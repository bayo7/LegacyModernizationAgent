using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// API'nin sunucudaki keyfi lokal yolları analiz etmesine izin verir. Lokal demo için
    /// varsayılan true; API başkalarının erişebildiği bir yerde çalışacaksa false yapılmalı
    /// (o zaman yalnızca git URL'i ve zip upload kabul edilir).
    /// </summary>
    public bool AllowLocalPaths { get; set; } = true;

    /// <summary>
    /// Klonlanan/açılan kaynakta analizden önce 'dotnet restore' çalıştırılsın mı.
    /// Kapalıyken paket referansları çözümlenemez ve özellikle UnusedCode kuralı
    /// test metodlarını / arayüz implementasyonlarını yanlışlıkla işaretler.
    /// </summary>
    public bool RestoreBeforeAnalysis { get; set; } = true;

    /// <summary>Analiz bitince klon/zip çalışma alanı silinsin mi (hata ayıklarken false yapılabilir).</summary>
    public bool CleanupAfterAnalysis { get; set; } = true;
}

/// <summary>
/// Analiz kaynağını hazırlar: git URL → shallow clone; .zip → aç; lokal yol → doğrula.
/// </summary>
public partial class GitSourcePreparer(IOptions<StorageOptions> options, ILogger<GitSourcePreparer> logger) : ISourcePreparer
{
    private readonly StorageOptions _options = options.Value;

    public async Task<string> PrepareAsync(string repoUrlOrPath, Guid jobId, CancellationToken ct = default)
    {
        if (LooksLikeGitUrl(repoUrlOrPath))
        {
            if (!IsSafeGitUrl(repoUrlOrPath))
                throw new ArgumentException($"Geçersiz git URL'i: {repoUrlOrPath}");

            return await CloneAsync(repoUrlOrPath, jobId, ct);
        }

        if (!_options.AllowLocalPaths && !IsUnderWorkRoot(repoUrlOrPath))
            throw new UnauthorizedAccessException(
                "Lokal yol analizi bu sunucuda kapalı (Storage:AllowLocalPaths=false). Git URL'i veya zip upload kullanın.");

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

    public async Task RestoreDependenciesAsync(string solutionOrProjectPath, CancellationToken ct = default)
    {
        // Yalnızca kendi çalışma alanımızdaki kaynaklar: kullanıcının lokal klasörüne yazmayız.
        if (!_options.RestoreBeforeAnalysis || !IsUnderWorkRoot(solutionOrProjectPath))
            return;

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            // Reponun global.json'ı (SDK sabitlemesi) geçerli olsun diye proje klasöründen çalıştırılır.
            WorkingDirectory = Path.GetDirectoryName(solutionOrProjectPath) ?? _options.WorkRoot
        };
        foreach (var arg in new[] { "restore", solutionOrProjectPath, "--nologo", "--verbosity", "quiet" })
            psi.ArgumentList.Add(arg);

        // MSBuildLocator bu süreçte MSBuild'i belirli bir SDK'ya sabitleyen ortam değişkenleri
        // ayarlar; alt süreç farklı bir SDK seçerse (global.json) bunlar çakışır.
        foreach (var variable in new[] { "MSBUILD_EXE_PATH", "MSBuildSDKsPath", "MSBuildExtensionsPath" })
            psi.Environment.Remove(variable);

        logger.LogInformation("Bağımlılıklar geri yükleniyor (dotnet restore): {Path}", solutionOrProjectPath);

        Process? process = null;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("dotnet süreci başlatılamadı (dotnet PATH'te mi?).");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = await stdoutTask;
            await process.WaitForExitAsync(timeout.Token);

            if (process.ExitCode != 0)
            {
                var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                logger.LogWarning(
                    "dotnet restore başarısız (exit {ExitCode}); analiz çözümlenemeyen referanslarla sürecek ve yanlış pozitif artabilir: {Output}",
                    process.ExitCode, output.Length <= 2000 ? output : output[..2000]);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Zaman aşımı, dotnet'in bulunamaması vb. — restore opsiyonel bir iyileştirmedir.
            logger.LogWarning(ex, "dotnet restore tamamlanamadı; analiz restore olmadan sürecek.");
            try
            {
                if (process is { HasExited: false })
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Süreç bu arada kapanmış.
            }
        }
        finally
        {
            process?.Dispose();
        }
    }

    public void Cleanup(Guid jobId)
    {
        if (!_options.CleanupAfterAnalysis)
            return;

        try
        {
            var workspace = Path.Combine(_options.WorkRoot, jobId.ToString());
            if (Directory.Exists(workspace))
            {
                // git, pack dosyalarını salt-okunur yazar; Windows'ta Directory.Delete bunlarda
                // takılır. Önce öznitelikleri normale çekmek gerekir.
                foreach (var file in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(workspace, recursive: true);
            }

            var uploadedZip = Path.Combine(_options.WorkRoot, "uploads", $"{jobId}.zip");
            if (File.Exists(uploadedZip))
                File.Delete(uploadedZip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "[{JobId}] Çalışma alanı temizlenemedi.", jobId);
        }
    }

    internal static bool LooksLikeGitUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("git@", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// URL git'e argüman olarak gider; boşluk/tırnak/kontrol karakteri içeren ya da
    /// beklenen biçime uymayan değerler reddedilir (argüman enjeksiyonuna karşı).
    /// </summary>
    internal static bool IsSafeGitUrl(string source)
    {
        if (source.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\''))
            return false;

        if (source.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            return ScpLikeGitUrl().IsMatch(source);

        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host);
    }

    [GeneratedRegex(@"^git@[A-Za-z0-9.\-]+:[A-Za-z0-9._\-/~]+$")]
    private static partial Regex ScpLikeGitUrl();

    private bool IsUnderWorkRoot(string path)
    {
        var root = Path.GetFullPath(_options.WorkRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> CloneAsync(string url, Guid jobId, CancellationToken ct)
    {
        var targetDir = Path.Combine(_options.WorkRoot, jobId.ToString());
        Directory.CreateDirectory(_options.WorkRoot);

        logger.LogInformation("Repo klonlanıyor: {Url} → {Dir}", url, targetDir);

        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        // Argümanlar tek string yerine ArgumentList ile verilir: kaçış/tırnak hatası olmaz.
        // "--" sonrası değerler git tarafından seçenek olarak yorumlanmaz.
        // Windows'ta 260 karakter MAX_PATH sınırı nedeniyle uzun asset yollu repolarda
        // ("Filename too long") clone başarısız olabiliyor; core.longpaths bunu aşar.
        foreach (var arg in new[] { "-c", "core.longpaths=true", "clone", "--depth", "1", "--", url, targetDir })
            psi.ArgumentList.Add(arg);

        // Özel repolarda git kimlik bilgisi sormak için beklemesin; hemen hata versin.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("git süreci başlatılamadı (git PATH'te mi?).");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await stdoutTask;
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
