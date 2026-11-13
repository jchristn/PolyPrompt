namespace Test.Shared
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using Touchstone.Core;

    /// <summary>
    /// The tests that make "the docs are the contract" true: the README capability matrix and default-model table match the
    /// code, every provider client's XML summary names its supported capabilities, defaults sections match capabilities, and
    /// no OpenAI or shared Responses code depends on an xAI type.
    /// </summary>
    internal static class LocalContractCases
    {
        private static readonly string[] _Columns = { "Chat", "ToolChat", "Generate", "Embed", "SparseEmbed", "Rerank", "Classify", "Decide", "Models" };

        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            (string, string, Func<CancellationToken, Task>)[] cases =
            {
                ("contract_readme_matrix", "The README capability matrix equals every client's Capabilities", MatrixAsync),
                ("contract_default_models", "The README default-model table equals every client's default models", DefaultModelsAsync),
                ("contract_defaults_sections", "Each defaults type declares exactly the sections for its supported capabilities", SectionsAsync),
                ("contract_xml_docs", "Each provider client's XML summary names every supported capability and links the matrix", XmlDocsAsync),
                ("contract_xai_dependency_rule", "No OpenAI or shared Responses type references an xAI type, in signatures or source", DependencyAsync),
            };
            foreach (var (id, name, run) in cases) yield return new TestCaseDescriptor(suite, id, name, run, new[] { "local", "contract" });
        }

        private static List<Type> ProviderTypes() => typeof(ProviderClientBase).Assembly.GetExportedTypes()
            .Where(t => !t.IsAbstract && typeof(ProviderClientBase).IsAssignableFrom(t)).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

        internal static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !(File.Exists(Path.Combine(directory.FullName, "README.md")) && Directory.Exists(Path.Combine(directory.FullName, "src")))) directory = directory.Parent;
            return directory?.FullName ?? throw new TestFailureException("Repository root not found above " + AppContext.BaseDirectory);
        }

        private static List<string[]> Table(string marker)
        {
            string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));
            Match block = Regex.Match(readme, "<!-- " + marker + ":start -->(.*?)<!-- " + marker + ":end -->", RegexOptions.Singleline);
            if (!block.Success) throw new TestFailureException("README.md has no " + marker + " block.");
            return block.Groups[1].Value.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("|", StringComparison.Ordinal) && !l.StartsWith("|---", StringComparison.Ordinal))
                .Skip(1).Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToArray()).ToList();
        }

        private static string Name(string cell) => cell.Trim('`');

        private static Task MatrixAsync(CancellationToken token)
        {
            List<string[]> rows = Table("capability-matrix");
            List<Type> types = ProviderTypes();
            SharedAssert.Equal(string.Join(", ", types.Select(t => t.Name)), string.Join(", ", rows.Select(r => Name(r[0])).OrderBy(n => n, StringComparer.Ordinal)), "Every provider client has exactly one matrix row.");
            foreach (string[] row in rows)
            {
                Type type = types.Single(t => t.Name == Name(row[0]));
                using ProviderClientBase client = LocalV4Cases.Construct(type);
                Capability documented = Capability.None;
                for (int i = 0; i < _Columns.Length; i++)
                {
                    string cell = row[i + 1];
                    SharedAssert.True(cell == "" || cell == "✅", type.Name + " " + _Columns[i] + " cell must be blank or ✅, not '" + cell + "'.");
                    if (cell == "✅") documented |= Enum.Parse<Capability>(_Columns[i]);
                }
                SharedAssert.Equal(client.Capabilities, documented, type.Name + " matrix row should equal Capabilities.");
            }
            return Task.CompletedTask;
        }

        private static Task DefaultModelsAsync(CancellationToken token)
        {
            List<string[]> rows = Table("default-models");
            foreach (Type type in ProviderTypes())
            {
                using ProviderClientBase client = LocalV4Cases.Construct(type);
                foreach (string section in new[] { "Completion", "Embedding", "SparseEmbedding", "Rerank", "Classification", "Decision" })
                {
                    object? options = typeof(ClientDefaults).GetProperty(section)!.GetValue(client.Defaults);
                    string[]? row = rows.FirstOrDefault(r => Name(r[0]) == type.Name && r[1] == section);
                    if (options == null)
                    {
                        SharedAssert.True(row == null, type.Name + " " + section + " is unsupported and must not be in the table.");
                        continue;
                    }
                    SharedAssert.NotNull(row, type.Name + " " + section + " must be in the default-model table.");
                    string? model = (string?)options.GetType().GetProperty("Model")!.GetValue(options);
                    string documented = row![2] == "none" ? "" : Name(row[2]);
                    SharedAssert.Equal(documented, model ?? "", type.Name + " " + section + " default model.");
                }
            }
            return Task.CompletedTask;
        }

        private static Task SectionsAsync(CancellationToken token)
        {
            (string Section, Capability Capability)[] sections =
            {
                ("Completion", Capability.Chat), ("Embedding", Capability.Embed), ("SparseEmbedding", Capability.SparseEmbed),
                ("Rerank", Capability.Rerank), ("Classification", Capability.Classify), ("Decision", Capability.Decide),
            };
            foreach (Type type in ProviderTypes())
            {
                using ProviderClientBase client = LocalV4Cases.Construct(type);
                Type defaults = client.Defaults.GetType();
                SharedAssert.True(defaults != typeof(ClientDefaults) && typeof(ClientDefaults).IsAssignableFrom(defaults), type.Name + " has its own defaults type.");
                Type? owner = type;
                PropertyInfo? declaredDefaults = null;
                while (owner != null && (declaredDefaults = owner.GetProperty("Defaults", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) == null) owner = owner.BaseType;
                SharedAssert.Equal(defaults, declaredDefaults!.PropertyType, type.Name + " Defaults is typed covariantly.");
                foreach (var (section, capability) in sections)
                {
                    PropertyInfo[] declared = defaults.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name == section).ToArray();
                    PropertyInfo visible = declared.First(p => p.DeclaringType == defaults || declared.Length == 1);
                    bool hidden = visible.GetCustomAttribute<ObsoleteAttribute>()?.IsError == true;
                    if (client.Supports(capability))
                    {
                        SharedAssert.False(hidden, type.Name + " Defaults." + section + " is available.");
                        SharedAssert.NotNull(visible.GetValue(client.Defaults), type.Name + " Defaults." + section + " is non-null.");
                    }
                    else
                    {
                        SharedAssert.True(hidden, type.Name + " Defaults." + section + " is a compile error on " + defaults.Name + ".");
                        SharedAssert.True(typeof(ClientDefaults).GetProperty(section)!.GetValue(client.Defaults) == null, type.Name + " Defaults." + section + " is null through ClientDefaults.");
                    }
                }
            }
            return Task.CompletedTask;
        }

        private static Task XmlDocsAsync(CancellationToken token)
        {
            string xmlPath = Path.ChangeExtension(typeof(ProviderClientBase).Assembly.Location, ".xml");
            SharedAssert.True(File.Exists(xmlPath), "The XML documentation file ships next to the assembly: " + xmlPath);
            XDocument xml = XDocument.Load(xmlPath);
            foreach (Type type in ProviderTypes())
            {
                XElement? member = xml.Descendants("member").FirstOrDefault(m => (string?)m.Attribute("name") == "T:" + type.FullName);
                SharedAssert.NotNull(member, type.Name + " has XML docs.");
                string summary = Regex.Replace(member!.Element("summary")!.Value, @"\s+", " ");
                int supported = summary.IndexOf("Supported:", StringComparison.Ordinal);
                int unsupported = summary.IndexOf("Not supported", StringComparison.Ordinal);
                SharedAssert.True(supported >= 0, type.Name + " summary lists supported capabilities.");
                string supportedText = unsupported > supported ? summary.Substring(supported, unsupported - supported) : summary.Substring(supported);
                using ProviderClientBase client = LocalV4Cases.Construct(type);
                foreach (string column in _Columns)
                {
                    Capability capability = Enum.Parse<Capability>(column);
                    bool mentioned = Regex.IsMatch(supportedText, @"\b" + column + @"\b");
                    if (client.Supports(capability)) SharedAssert.True(mentioned, type.Name + " summary should name supported capability " + column + ": " + supportedText);
                    else SharedAssert.False(mentioned, type.Name + " summary should not list unsupported " + column + " as supported: " + supportedText);
                }
                SharedAssert.True(member.Elements("seealso").Any(e => ((string?)e.Attribute("href") ?? "").EndsWith("#capability-matrix", StringComparison.Ordinal)), type.Name + " links the capability matrix.");
            }

            XElement? baseChat = xml.Descendants("member").FirstOrDefault(m => ((string?)m.Attribute("name") ?? "").StartsWith("M:PolyPrompt.Clients.ProviderClientBase.EmbedAsync(System.String", StringComparison.Ordinal));
            SharedAssert.True(baseChat?.Elements("exception").Any(e => ((string?)e.Attribute("cref") ?? "").Contains("CapabilityNotSupportedException", StringComparison.Ordinal)) == true, "Base operations document CapabilityNotSupportedException.");
            return Task.CompletedTask;
        }

        private static Task DependencyAsync(CancellationToken token)
        {
            Assembly assembly = typeof(ProviderClientBase).Assembly;
            List<Type> checkedTypes = assembly.GetTypes().Where(t => t == typeof(OpenAiClient) || t == typeof(OpenAiCompatibleClient) || t == typeof(AzureOpenAiClient)
                || t.Name.StartsWith("ResponsesApi", StringComparison.Ordinal) || t.Name.StartsWith("OpenAi", StringComparison.Ordinal)).ToList();
            SharedAssert.True(checkedTypes.Count >= 8, "Checked types found.");
            foreach (Type type in checkedTypes)
            {
                BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                IEnumerable<Type> referenced = type.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
                    .Concat(type.GetProperties(all).Select(p => p.PropertyType)).Concat(type.GetFields(all).Select(f => f.FieldType)).Append(type.BaseType ?? typeof(object));
                foreach (Type t in referenced.SelectMany(Expand))
                    SharedAssert.False(t.Name.StartsWith("XAi", StringComparison.Ordinal), type.Name + " references " + t.Name + ".");
            }

            string src = Path.Combine(RepositoryRoot(), "src", "PolyPrompt");
            List<string> files = Directory.GetFiles(Path.Combine(src, "Clients", "OpenAi"), "*.cs")
                .Concat(Directory.GetFiles(Path.Combine(src, "Clients", "Responses"), "*.cs"))
                .Concat(Directory.GetFiles(Path.Combine(src, "Models"), "ResponsesApi*.cs")).ToList();
            SharedAssert.True(files.Count >= 8, "Source files found.");
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                Match hit = Regex.Match(text, @"\bXAi\w*");
                SharedAssert.False(hit.Success, Path.GetFileName(file) + " references " + hit.Value + ".");
            }
            return Task.CompletedTask;
        }

        private static IEnumerable<Type> Expand(Type type)
        {
            yield return type;
            if (type.HasElementType) foreach (Type t in Expand(type.GetElementType()!)) yield return t;
            if (type.IsGenericType) foreach (Type arg in type.GetGenericArguments()) foreach (Type t in Expand(arg)) yield return t;
        }
    }
}
