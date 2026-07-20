using System.Text.Json.Nodes;

namespace LegacyModernizationAgent.Web;

// Dashboard, API'yi HTTP üzerinden tüketen bağımsız bir istemcidir (sunucu iç
// katmanlarına proje referansı yok) — mimarideki "Dashboard → API" okuna sadık.

public record JobDto(Guid Id, string RepoUrlOrPath, string Status, DateTime CreatedAt, DateTime? CompletedAt, string? ErrorMessage);

public record FindingDto(
    Guid Id, string RuleName, string Severity, string Confidence, string FilePath,
    int LineNumber, string? ClassName, string Description, string? LimitationNote, string? MetricsJson);

public record DependencyDto(string FromClass, string ToClass);

public record ChatMessageDto(string Role, string Content, DateTime CreatedAt);

public record ChatResponseDto(string Answer, List<string> SourceFiles);

public class ApiClient(HttpClient http)
{
    public async Task<List<JobDto>> ListJobsAsync() =>
        await http.GetFromJsonAsync<List<JobDto>>("api/analysis") ?? [];

    public async Task<JobDto?> GetJobAsync(Guid id) =>
        await http.GetFromJsonAsync<JobDto>($"api/analysis/{id}");

    public async Task<JobDto?> StartAsync(string source)
    {
        var response = await http.PostAsJsonAsync("api/analysis", new { source });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobDto>();
    }

    public async Task<JobDto?> UploadZipAsync(Stream zipStream, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(zipStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", fileName);

        var response = await http.PostAsync("api/analysis/upload", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobDto>();
    }

    public async Task<List<FindingDto>> GetFindingsAsync(Guid id) =>
        await http.GetFromJsonAsync<List<FindingDto>>($"api/analysis/{id}/findings") ?? [];

    public async Task<List<DependencyDto>> GetDependenciesAsync(Guid id) =>
        await http.GetFromJsonAsync<List<DependencyDto>>($"api/analysis/{id}/dependencies") ?? [];

    public async Task<JsonNode?> GetReportAsync(Guid id)
    {
        var response = await http.GetAsync($"api/analysis/{id}/report");
        if (!response.IsSuccessStatusCode)
            return null;
        return JsonNode.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task<List<ChatMessageDto>> GetChatHistoryAsync(Guid id) =>
        await http.GetFromJsonAsync<List<ChatMessageDto>>($"api/analysis/{id}/chat") ?? [];

    public async Task<ChatResponseDto?> SendChatAsync(Guid id, string message)
    {
        var response = await http.PostAsJsonAsync($"api/analysis/{id}/chat", new { message });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatResponseDto>();
    }
}
