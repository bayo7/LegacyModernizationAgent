using LegacyModernizationAgent.Web;
using LegacyModernizationAgent.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5210");
    client.Timeout = TimeSpan.FromMinutes(3); // chat LLM cevabı bekleyebilir

    // API'de Auth:ApiKey ayarlıysa aynı değer burada Api:ApiKey olarak verilmelidir.
    var apiKey = builder.Configuration["Api:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
