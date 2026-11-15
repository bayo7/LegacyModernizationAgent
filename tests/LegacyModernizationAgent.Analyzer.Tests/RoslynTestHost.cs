using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace LegacyModernizationAgent.Analyzer.Tests;

/// <summary>
/// Testler için Roslyn altyapısı: kaynak kodu string olarak alır, gerçek bir
/// compilation/semantic model (dosya kuralları) veya AdhocWorkspace solution'ı
/// (solution kuralları) üretir. MSBuild'e ihtiyaç duymaz — testler hızlı ve izole kalır.
/// </summary>
internal static class RoslynTestHost
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Private.CoreLib", "System.Runtime", "System.Linq",
            "System.Collections", "netstandard", "System.Linq.Expressions",
            "System.Runtime.Extensions", "System.ComponentModel.Primitives"
        };

        return trustedAssemblies
            .Where(p => needed.Contains(Path.GetFileNameWithoutExtension(p)))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    });

    public static (SyntaxNode Root, SemanticModel SemanticModel) Compile(string source, string filePath = "Test.cs")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (syntaxTree.GetRoot(), compilation.GetSemanticModel(syntaxTree));
    }

    public static Solution CreateSolution(params (string FileName, string Source)[] documents) =>
        CreateSolutionAt(null, documents);

    /// <summary>Proje dosya yolu verilen sürüm: diskteki komşu dosyalara (ör. .razor) bakan kurallar için.</summary>
    public static Solution CreateSolutionAt(string? projectFilePath, params (string FileName, string Source)[] documents)
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "TestProject",
            "TestProject",
            LanguageNames.CSharp,
            filePath: projectFilePath,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: References.Value);

        var project = workspace.AddProject(projectInfo);

        foreach (var (fileName, source) in documents)
        {
            workspace.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(project.Id),
                fileName,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())),
                filePath: fileName));
        }

        return workspace.CurrentSolution;
    }
}
