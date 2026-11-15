namespace LegacyModernizationAgent.Application.Services;

internal static class SourcePaths
{
    /// <summary>
    /// Analiz edilen dosyanın yolunu kaynak köküne göre göreli hale getirir ('/' ayırıcıyla).
    /// Klonlanan repolar geçici bir klasörde analiz edilip silindiği için mutlak yol hem
    /// anlamsız hem de makineye özgüdür; rapor ve dashboard'da 'src/Proje/Sinif.cs' görünmelidir.
    /// Kökün dışında kalan dosyalar (ör. dışarıdaki bir proje referansı) olduğu gibi bırakılır.
    /// </summary>
    public static string ToRelative(string root, string path)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root) || !Path.IsPathRooted(path))
            return path;

        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return path;

        return relative.Replace('\\', '/');
    }
}
