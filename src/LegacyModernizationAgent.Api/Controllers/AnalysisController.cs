using LegacyModernizationAgent.Application.Dtos;
using LegacyModernizationAgent.Application.Services;
using LegacyModernizationAgent.Core.Abstractions;
using LegacyModernizationAgent.Core.Entities;
using LegacyModernizationAgent.Infrastructure.Sources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LegacyModernizationAgent.Api.Controllers;

[ApiController]
[Route("api/analysis")]
public class AnalysisController(
    IAnalysisJobRepository jobs,
    IFindingRepository findings,
    IReportRepository reports,
    IChatRepository chatHistory,
    IChatService chatService,
    IAnalysisQueue queue) : ControllerBase
{
    /// <summary>Yeni analiz başlatır. Source: lokal .sln/.csproj/klasör yolu veya git URL'i.</summary>
    [HttpPost]
    public async Task<ActionResult<AnalysisJobDto>> Start([FromBody] StartAnalysisRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Source))
            return BadRequest(new { error = "Source boş olamaz (lokal yol veya git URL'i bekleniyor)." });

        var job = await jobs.CreateAsync(new AnalysisJob { RepoUrlOrPath = request.Source.Trim() }, ct);
        await queue.EnqueueAsync(job.Id, ct);

        return AcceptedAtAction(nameof(GetStatus), new { id = job.Id }, AnalysisJobDto.From(job));
    }

    /// <summary>Zip upload ile analiz başlatır.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(200_000_000)]
    public async Task<ActionResult<AnalysisJobDto>> Upload(IFormFile file, [FromServices] IOptions<StorageOptions> storage, CancellationToken ct)
    {
        if (file.Length == 0 || !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Geçerli bir .zip dosyası bekleniyor." });

        var job = new AnalysisJob();
        var uploadDir = Path.Combine(storage.Value.WorkRoot, "uploads");
        Directory.CreateDirectory(uploadDir);
        var zipPath = Path.Combine(uploadDir, $"{job.Id}.zip");

        await using (var stream = System.IO.File.Create(zipPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        job.RepoUrlOrPath = zipPath;
        await jobs.CreateAsync(job, ct);
        await queue.EnqueueAsync(job.Id, ct);

        return AcceptedAtAction(nameof(GetStatus), new { id = job.Id }, AnalysisJobDto.From(job));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AnalysisJobDto>>> List(CancellationToken ct) =>
        Ok((await jobs.ListAsync(ct)).Select(AnalysisJobDto.From).ToList());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AnalysisJobDto>> GetStatus(Guid id, CancellationToken ct)
    {
        var job = await jobs.GetAsync(id, ct);
        return job is null ? NotFound() : Ok(AnalysisJobDto.From(job));
    }

    [HttpGet("{id:guid}/findings")]
    public async Task<ActionResult<IReadOnlyList<FindingDto>>> GetFindings(Guid id, CancellationToken ct)
    {
        if (await jobs.GetAsync(id, ct) is null)
            return NotFound();

        return Ok((await findings.GetByJobAsync(id, ct)).Select(FindingDto.From).ToList());
    }

    [HttpGet("{id:guid}/report")]
    public async Task<IActionResult> GetReport(Guid id, CancellationToken ct)
    {
        var job = await jobs.GetAsync(id, ct);
        if (job is null)
            return NotFound();

        var report = await reports.GetLatestAsync(id, ct);
        if (report is null)
        {
            return NotFound(new
            {
                error = "Rapor henüz üretilmedi.",
                jobStatus = job.Status.ToString()
            });
        }

        return Content(report.ReportJson, "application/json");
    }

    [HttpGet("{id:guid}/dependencies")]
    public async Task<ActionResult<IReadOnlyList<DependencyDto>>> GetDependencies(Guid id, CancellationToken ct)
    {
        if (await jobs.GetAsync(id, ct) is null)
            return NotFound();

        return Ok((await findings.GetDependenciesAsync(id, ct))
            .Select(d => new DependencyDto(d.FromClass, d.ToClass)).ToList());
    }

    [HttpPost("{id:guid}/chat")]
    public async Task<ActionResult<ChatResponse>> Chat(Guid id, [FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Mesaj boş olamaz." });

        var job = await jobs.GetAsync(id, ct);
        if (job is null)
            return NotFound();

        if (job.Status != JobStatus.Completed)
            return Conflict(new { error = "Analiz henüz tamamlanmadı; chat için analiz sonuçları gerekli.", jobStatus = job.Status.ToString() });

        var answer = await chatService.AskAsync(id, request.Message, ct);
        return Ok(new ChatResponse(answer.Content, answer.SourceFiles));
    }

    [HttpGet("{id:guid}/chat")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetChatHistory(Guid id, CancellationToken ct)
    {
        if (await jobs.GetAsync(id, ct) is null)
            return NotFound();

        return Ok((await chatHistory.GetByJobAsync(id, ct))
            .Select(m => new ChatMessageDto(m.Role, m.Content, m.CreatedAt)).ToList());
    }
}
