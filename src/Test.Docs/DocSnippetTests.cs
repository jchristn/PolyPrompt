namespace Test.Docs;

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

/// <summary>
/// Compiles every C# code block in the documentation against the built library, so the examples people and coding agents
/// copy are known to build. A block preceded by <c>&lt;!-- no-compile --&gt;</c> is skipped. Each block becomes the body
/// of an async method; its <c>using</c> directives are hoisted.
/// </summary>
[TestFixture]
public sealed class DocSnippetTests
{
    private static readonly string[] _Documents = { "README.md", "STREAMING.md", "MIGRATION_V3_TO_V4.md" };

    private static readonly string[] _DefaultUsings =
    {
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Text", "System.Text.Json",
        "System.Threading", "System.Threading.Tasks", "PolyPrompt.Clients", "PolyPrompt.Models", "PolyPrompt.Options", "PolyPrompt.Auth", "PolyPrompt.Helpers",
    };

    public sealed record Snippet(string Document, int Line, string Code)
    {
        public override string ToString() => Document + ":" + Line;
    }

    public static IEnumerable<Snippet> Snippets()
    {
        string root = FindRepositoryRoot();
        foreach (string document in _Documents)
        {
            string path = Path.Combine(root, document);
            if (!File.Exists(path)) continue;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "```csharp") continue;
                bool skip = i > 0 && Previous(lines, i).Contains("no-compile", StringComparison.Ordinal);
                int start = i + 1;
                int end = start;
                while (end < lines.Length && lines[end].Trim() != "```") end++;
                if (!skip) yield return new Snippet(document, start + 1, string.Join("\n", lines[start..end]));
                i = end;
            }
        }
    }

    [Test]
    public void DocumentsHaveSnippets()
    {
        Assert.That(Snippets().Count(s => s.Document == "README.md"), Is.GreaterThan(10));
    }

    [TestCaseSource(nameof(Snippets))]
    public void SnippetCompiles(Snippet snippet)
    {
        List<string> usings = new List<string>(_DefaultUsings);
        StringBuilder body = new StringBuilder();
        StringBuilder types = new StringBuilder();
        bool inTypes = false;
        foreach (string line in snippet.Code.Split('\n'))
        {
            Match match = Regex.Match(line, @"^\s*using\s+([A-Za-z_][\w.]*)\s*;\s*$");
            if (match.Success) { usings.Add(match.Groups[1].Value); continue; }
            // Type declarations at column 0 (and everything after them) go to namespace scope.
            if (Regex.IsMatch(line, @"^(public |internal )?((sealed|static|abstract|partial) )*(class|record|struct|interface) ")) inTypes = true;
            (inTypes ? types : body).AppendLine(line);
        }

        string source = string.Join("\n", usings.Distinct().Select(u => "using " + u + ";")) + "\n"
            + "namespace DocSnippets\n{\n    internal static class Snippet\n    {\n        internal static async Task RunAsync()\n        {\n"
            + body + "\n            await Task.CompletedTask;\n        }\n    }\n" + types + "\n}\n";

        CSharpCompilation compilation = CSharpCompilation.Create(
            "DocSnippet",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        List<Diagnostic> errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.That(errors, Is.Empty, snippet + " does not compile:\n" + string.Join("\n", errors.Select(e => e.ToString())) + "\n\n" + snippet.Code);
    }

    private static List<MetadataReference>? _References;

    private static List<MetadataReference> References()
    {
        if (_References != null) return _References;
        List<MetadataReference> references = new List<MetadataReference>();
        string? platform = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        foreach (string path in (platform ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            references.Add(MetadataReference.CreateFromFile(path));
        string directory = Path.GetDirectoryName(typeof(PolyPrompt.Clients.ProviderClientBase).Assembly.Location)!;
        foreach (string name in new[] { "PolyPrompt.dll", "SyslogLogging.dll" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path) && !references.OfType<PortableExecutableReference>().Any(r => Path.GetFileName(r.FilePath) == name))
                references.Add(MetadataReference.CreateFromFile(path));
        }
        return _References = references;
    }

    private static string Previous(string[] lines, int index)
    {
        for (int j = index - 1; j >= 0; j--)
            if (!string.IsNullOrWhiteSpace(lines[j])) return lines[j];
        return "";
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "README.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("README.md not found above " + AppContext.BaseDirectory);
    }
}
