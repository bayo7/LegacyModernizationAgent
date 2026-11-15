using System.Security.Cryptography;
using System.Text;
using Hangfire;
using LegacyModernizationAgent.Analyzer.Engine;
using LegacyModernizationAgent.Application;
using LegacyModernizationAgent.Infrastructure;
using LegacyModernizationAgent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// KRİTİK: MSBuildWorkspace kullanılmadan (hatta MSBuild assembly'leri yüklenmeden)
// önce MSBuildLocator.RegisterDefaults() çağrılmalı — bu yüzden en başta duruyor.
MsBuildSolutionLoader.EnsureMsBuildRegistered();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// CORS varsayılan olarak kapalı: dashboard (Blazor Server) API'yi sunucu tarafından çağırdığı
// için tarayıcıdan çapraz köken erişimine ihtiyaç yok. Bir SPA eklenirse Cors:AllowedOrigins doldurulur.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length > 0)
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

// Şema migration'ı açılışta uygulanır — demo kurulumunda elle 'dotnet ef database update' gerekmesin.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// Basit API anahtarı koruması: Auth:ApiKey doluysa /api altındaki tüm istekler
// 'X-Api-Key' başlığını taşımak zorundadır. Boşsa (lokal demo) koruma kapalıdır.
var apiKey = builder.Configuration["Auth:ApiKey"];
if (!string.IsNullOrWhiteSpace(apiKey))
{
    var expectedKey = Encoding.UTF8.GetBytes(apiKey);
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var providedKey = Encoding.UTF8.GetBytes(context.Request.Headers["X-Api-Key"].ToString());
            // Sabit zamanlı karşılaştırma: anahtar, yanıt süresinden tahmin edilemesin.
            if (!CryptographicOperations.FixedTimeEquals(providedKey, expectedKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Geçerli bir X-Api-Key başlığı gerekli." });
                return;
            }
        }

        await next(context);
    });
}
app.MapControllers();

if (builder.Configuration.GetValue("Jobs:UseHangfire", defaultValue: true))
{
    // Job durumlarını görselleştiren hazır dashboard — demo sırasında gösterilebilir.
    // Hangfire varsayılan yetkilendirmesi yalnızca localhost isteklerine izin verir.
    app.UseHangfireDashboard("/hangfire");
}

app.Run();
