using LegacyModernizationAgent.Analyzer.Models;
using LegacyModernizationAgent.Analyzer.Rules;

namespace LegacyModernizationAgent.Analyzer.Tests;

public class UnusedCodeAnalyzerTests
{
    private readonly UnusedCodeAnalyzer _analyzer = new();

    [Fact]
    public async Task Hic_referansi_olmayan_public_metod_bulgu_uretir()
    {
        var solution = RoslynTestHost.CreateSolution(("Service.cs", """
            public class PriceService
            {
                public decimal CalculateDiscount(decimal price) => price * 0.9m;
            }
            """));

        var findings = await _analyzer.AnalyzeAsync(solution);

        var finding = Assert.Single(findings);
        Assert.Equal("UnusedCode", finding.RuleName);
        Assert.Equal(Confidence.Low, finding.Confidence);
        Assert.Equal("CalculateDiscount", finding.Metrics!["MethodName"]);
    }

    [Fact]
    public async Task Referans_edilen_metod_bulgu_uretmez()
    {
        var solution = RoslynTestHost.CreateSolution(("Service.cs", """
            public class PriceService
            {
                public decimal CalculateDiscount(decimal price) => price * 0.9m;
            }

            public class CheckoutService
            {
                private readonly PriceService _prices = new();
                public decimal Total(decimal price) => _prices.CalculateDiscount(price);
            }
            """));

        var findings = await _analyzer.AnalyzeAsync(solution);

        // CalculateDiscount referanslı; Total referanssız olduğundan tek bulgu Total olmalı.
        var finding = Assert.Single(findings);
        Assert.Equal("Total", finding.Metrics!["MethodName"]);
    }

    [Fact]
    public async Task Interface_implementasyonu_muaf_tutulur()
    {
        var solution = RoslynTestHost.CreateSolution(("Service.cs", """
            public interface INotifier
            {
                void Notify(string message);
            }

            public class EmailNotifier : INotifier
            {
                public void Notify(string message) { }
            }
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }

    [Fact]
    public async Task Controller_action_lari_muaf_tutulur()
    {
        // Routing ile çağrılır; C# kodundan referans görünmemesi normaldir.
        var solution = RoslynTestHost.CreateSolution(("Controller.cs", """
            public class OrdersController
            {
                public string GetAll() => "[]";
            }
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }

    [Fact]
    public async Task Virtual_ve_override_metodlar_muaf_tutulur()
    {
        var solution = RoslynTestHost.CreateSolution(("Service.cs", """
            public class BaseHandler
            {
                public virtual void Handle() { }
            }

            public class SpecialHandler : BaseHandler
            {
                public override void Handle() { }
            }
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }

    [Fact]
    public async Task Test_metodlari_muaf_tutulur()
    {
        var solution = RoslynTestHost.CreateSolution(("Tests.cs", """
            public class FactAttribute : System.Attribute { }

            public class PriceTests
            {
                [Fact]
                public void Discount_works() { }
            }
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }

    [Fact]
    public async Task Record_larin_derleyici_uretimi_metodlari_muaf_tutulur()
    {
        // Record'lar public Deconstruct gibi implicit metodlar üretir; bunlar
        // kullanıcı kodu olmadığı için "kullanılmayan kod" sayılmamalıdır.
        var solution = RoslynTestHost.CreateSolution(("Model.cs", """
            public record Money(decimal Amount, string Currency);
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }

    [Fact]
    public async Task Razor_PageModel_handler_lari_muaf_tutulur()
    {
        // OnGetAsync/OnPostAsync gibi handler'lar routing ile çağrılır.
        var solution = RoslynTestHost.CreateSolution(("Page.cs", """
            public abstract class PageModel { }

            public class IndexModel : PageModel
            {
                public void OnGet() { }
                public void OnPostAsync() { }
            }
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }

    [Fact]
    public async Task Middleware_InvokeAsync_muaf_tutulur()
    {
        var solution = RoslynTestHost.CreateSolution(("Middleware.cs", """
            public class ExceptionMiddleware
            {
                public System.Threading.Tasks.Task InvokeAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }

    [Fact]
    public async Task Private_metodlar_bu_kuralin_kapsami_disindadir()
    {
        var solution = RoslynTestHost.CreateSolution(("Service.cs", """
            public class Worker
            {
                private void Helper() { }
            }
            """));

        Assert.Empty(await _analyzer.AnalyzeAsync(solution));
    }
}
