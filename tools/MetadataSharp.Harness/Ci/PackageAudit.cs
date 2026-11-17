using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Ci;

// Reproducible local-only package/content/offline-consumer audit. No publish command. The measurements of
// tools/MetadataSharp.Audit for the target framework of AUDIT_TFM, the public API of both targets (which has to be
// the same), the packed MetadataSharp.Core and MetadataSharp.Formats candidates and their symbol packages, and an
// offline consumer that restores only from the packed feed: artifacts/audit/<tfm>/packages.json, and both reports
// as compressed annotations (M18Z) for tools that read the run without its artifacts.
// (Formerly tools/audit/run.py; the commands, the checks, the reports and the annotations are unchanged.)
internal static class PackageAudit
{
    public static readonly string[] Targets = ["net8.0", "net10.0"];
    public const string Repository = "https://github.com/sensboston/MetadataSharp";

    // The offline consumer: a project that references only the packed library.
    public static readonly string ConsumerProject = """
        <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>AUDIT_TARGET</TargetFramework><ImplicitUsings>enable</ImplicitUsings><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><PackageReference Include="MetadataSharp" Version="[0.0.0-audit]"/></ItemGroup></Project>
        """.ReplaceLineEndings("\n");

    public static readonly string ConsumerProgram = """
        #pragma warning disable CS0618 // Exercise the intentionally retained compatibility facade.
        using MetadataSharp.Formats.Tiff; using MetadataSharp.Core; using System.Reflection; using System.Runtime.Versioning;
        using var doc = TiffMetadataDocument.Open(args[0]);
        var edit = new TiffMetadataEdit(); edit.Set(new(doc.Layout.FirstIfdOffset, 305), ExifValue.FromRaw(TiffFieldType.Ascii, 6, Endianness.LittleEndian, "audit\0"u8));
        using var output = new MemoryStream(); doc.Save(output, edit); output.Position = 0;
        using var reopened = TiffMetadataDocument.Open(output);
        if (reopened.ReadValue(new(reopened.Layout.FirstIfdOffset, 305))?.Count != 6) throw new Exception("Package consumer mismatch.");
        if (System.Environment.Version.Major != EXPECTED_MAJOR) throw new Exception("Consumer ran on wrong runtime.");
        string expectedTarget = ".NETCoreApp,Version=v" + System.Environment.Version.Major + ".0";
        if (typeof(ExifValue).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName != expectedTarget ||
            typeof(TiffLayout).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName != expectedTarget)
            throw new Exception("Consumer loaded wrong library target asset.");
        Console.WriteLine("Offline package consumer passed.");
        """.ReplaceLineEndings("\n");

    private static PyException Failed(object? message = null) => new("AssertionError", message is null ? "" : Py.Str(message));

    private static void Assert(bool condition, object? message = null)
    {
        if (!condition) throw Failed(message);
    }

    private static string Escape(string text) =>
        text.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);

    private static string Tail(string text, int length) => Py.Len(text) <= length ? text : string.Concat(Py.CodePoints(text).Skip(Py.Len(text) - length).Select(Py.FromCodePoint));

    /// <summary>A command whose output goes to the log; a failure is also an annotation with the last of it.</summary>
    private static void RunCommand(string? cwd, params string[] args)
    {
        var result = PyProcess.Run(args, cwd: cwd ?? Repo.Root, strict: true, stderr: Pipe.Stdout);
        Out.Print(result.Stdout);
        if (result.ReturnCode != 0)
        {
            Out.Print("::error title=M18 command failure::" + Escape(Tail(result.Stdout, 12000)));
            throw new PyException("subprocess.CalledProcessError", $"Command '{Py.Repr(new PyTuple([.. args]))}' returned non-zero exit status {result.ReturnCode}.");
        }
    }

    private static string CheckOutput(string? cwd, params string[] args) => PyProcess.Run(args, cwd: cwd, strict: true, stderr: Pipe.Inherit, check: true).Stdout;

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    // ElementTree's view of an element: the local name of its tag, its text before the first child, its attributes.
    private static string LocalName(XmlElement element) => element.LocalName;

    private static string? Text(XmlElement element)
    {
        string? text = null;
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement) break;
            if (child is XmlText or XmlCDataSection or XmlWhitespace or XmlSignificantWhitespace) text = (text ?? "") + child.Value;
        }
        return text;
    }

    private static string? Get(XmlElement element, string name) => element.HasAttribute(name) ? element.GetAttribute(name) : null;

    private static XmlElement Parse(byte[] data)
    {
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using var stream = new MemoryStream(data);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        document.Load(reader);
        return document.DocumentElement!;
    }

    private static List<XmlElement> Iter(XmlElement root) => [root, .. root.GetElementsByTagName("*").Cast<XmlElement>()];

    private static List<XmlElement> Local(XmlElement root, string tag) => Iter(root).Where(element => LocalName(element) == tag).ToList();

    private static List<XmlElement> Children(XmlElement element) => element.ChildNodes.OfType<XmlElement>().ToList();

    private static byte[] Read(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new PyException("KeyError", $"\"There is no item named {Py.ReprString(name)} in the archive\"");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool Contains(byte[] data, byte[] part) => data.AsSpan().IndexOf(part) >= 0;

    private static PyDict Json(string path) => (PyDict)PyJson.Loads(PyFile.ReadText(path))!;

    public static int Run(string[] argv)
    {
        string target = Environment.GetEnvironmentVariable("AUDIT_TFM") ?? "net8.0";
        if (!Targets.Contains(target)) throw new PyException("ValueError", "Unsupported audit target framework");
        try
        {
            Audit(target);
            return 0;
        }
        catch (Exception error)
        {
            Out.Print("::error title=M18 audit failure::" + Escape(Tail(PyException.TraceOf(error), 12000)));
            throw;
        }
    }

    private static void Audit(string target)
    {
        string output = Repo.Path("artifacts", "audit", target);
        string feed = PyPath.Join(output, "packages"), symbolsDirectory = PyPath.Join(output, "symbols");
        PyFile.MakeDirs(feed);
        RunCommand(null, "dotnet", "run", "--project", "tools/MetadataSharp.Audit", "-c", "Release", "--framework", target, "--", output);
        var baseline = Json(PyPath.Join(output, "baseline.json"));
        Assert(Py.Eq(baseline["targetFramework"], $".NETCoreApp,Version=v{target[3..]}"), baseline["targetFramework"]);
        Assert(baseline["runtime"] is string runtime && runtime.StartsWith($".NET {target[3..]}.", StringComparison.Ordinal), baseline["runtime"]);
        var surfaces = new Dictionary<string, PyDict>(StringComparer.Ordinal);
        foreach (string tfm in Targets)
        {
            string path = PyPath.Join(output, $"api-{tfm}.json");
            RunCommand(null, "dotnet", "run", "--project", "tools/MetadataSharp.Audit", "-c", "Release", "--framework", tfm, "--", "--api", path);
            surfaces[tfm] = Json(path);
            Assert(Py.Eq(surfaces[tfm]["runtimeMajor"], long.Parse(tfm[3..].Split('.')[0], System.Globalization.CultureInfo.InvariantCulture)));
        }
        Assert(Py.Eq(surfaces["net8.0"]["signatures"], surfaces["net10.0"]["signatures"]), "Cross-target public signatures differ");
        string signatureHash = Sha256(PyText.Utf8(PyJson.Compact(surfaces["net8.0"]["signatures"])));
        string head = PyText.Strip(CheckOutput(Repo.Root, "git", "rev-parse", "HEAD"));
        Directory.CreateDirectory(symbolsDirectory);
        var reports = new List<object?>();
        foreach ((string name, string id) in new[] { ("MetadataSharp.Core", "MetadataSharp.Core"), ("MetadataSharp.Formats", "MetadataSharp") })
        {
            // The project and its assembly keep their name; the package of MetadataSharp.Formats is "MetadataSharp".
            RunCommand(null, "dotnet", "pack", $"src/{name}/{name}.csproj", "-c", "Release", "-p:MetadataSharpAuditPackage=true", "-o", feed);
            string package = PyPath.Join(feed, $"{id}.0.0.0-audit.nupkg");
            List<string> names;
            PyDict markers, groups, docs, repository;
            using (var archive = ZipFile.OpenRead(package))
            {
                names = Py.Sorted(archive.Entries.Select(entry => entry.FullName));
                var required = new List<string> { "LICENSE", "README.md" };
                foreach (string tfm in Targets) required.AddRange(new[] { "dll", "xml" }.Select(extension => $"lib/{tfm}/{name}.{extension}"));
                Assert(required.All(names.Contains), names);
                Assert(Read(archive, "LICENSE").AsSpan().SequenceEqual(PyFile.ReadBytes(Repo.Path("LICENSE"))));
                Assert(Read(archive, "README.md").AsSpan().SequenceEqual(PyFile.ReadBytes(Repo.Path("README.md"))));
                var dlls = names.Where(entry => entry.EndsWith(".dll", StringComparison.Ordinal)).ToList();
                Assert(dlls.SequenceEqual(Py.Sorted(Targets.Select(tfm => $"lib/{tfm}/{name}.dll"))), dlls);
                // IsAotCompatible reached the packaged build: the SDK that turns on the trim and AOT analyzers
                // for it also writes this assembly metadata, which a trimming consumer reads.
                markers = new PyDict();
                foreach (string dll in dlls)
                {
                    byte[] data = Read(archive, dll);
                    markers[dll.Split('/')[1]] = new[] { "IsTrimmable", "IsAotCompatible" }.Where(marker => Contains(data, PyText.Utf8(marker))).Cast<object?>().ToList();
                }
                Assert(markers.Values.All(found => ((List<object?>)found!).Contains("IsTrimmable")), markers);
                string[] refused = [".exe", ".cs", ".pdb", ".pdf", ".tif", ".jpg", ".png", ".webp"];
                Assert(!names.Any(entry => refused.Any(suffix => entry.EndsWith(suffix, StringComparison.Ordinal))), names);
                var nuspec = Parse(Read(archive, $"{id}.nuspec"));
                var license = Local(nuspec, "license")[0];
                Assert(Text(license) == "MIT" && Get(license, "type") == "expression");
                // The package names the repository and the commit it was built from.
                repository = new PyDict();
                foreach (XmlAttribute attribute in Local(nuspec, "repository")[0].Attributes)
                {
                    if (attribute.Name == "xmlns" || attribute.Prefix == "xmlns") continue;
                    repository[attribute.NamespaceURI.Length > 0 ? $"{{{attribute.NamespaceURI}}}{attribute.LocalName}" : attribute.LocalName] = attribute.Value;
                }
                Assert(Py.Eq(new PyTuple(repository.Get("type"), repository.Get("url"), repository.Get("commit")), new PyTuple("git", Repository, head)), repository);
                Assert(Local(nuspec, "projectUrl").Select(Text).SequenceEqual([Repository]) && Local(nuspec, "readme").Select(Text).SequenceEqual(["README.md"]));
                groups = new PyDict();
                foreach (var group in Local(nuspec, "group"))
                {
                    string? declared = Get(group, "targetFramework");
                    string? tfm = Targets.FirstOrDefault(candidate => declared == candidate || declared == ".NETCoreApp" + candidate[3..] || declared == ".NETCoreApp,Version=v" + candidate[3..]);
                    Assert(tfm is not null && !groups.ContainsKey(tfm), declared);
                    var dependencies = Children(group).Where(element => LocalName(element) == "dependency")
                                                      .Select(element => (object?)new PyTuple(Get(element, "id"), Get(element, "version"))).ToList();
                    var expected = name.EndsWith("Core", StringComparison.Ordinal) ? new List<object?>() : [new PyTuple("MetadataSharp.Core", "0.0.0-audit")];
                    Assert(Py.Eq(dependencies, expected), dependencies);
                    groups[tfm!] = dependencies;
                }
                Assert(groups.Keys.Cast<string>().ToHashSet().SetEquals(Targets), groups);
                docs = new PyDict();
                foreach (string tfm in Targets)
                {
                    var root = Parse(Read(archive, $"lib/{tfm}/{name}.xml"));
                    docs[tfm] = (long)Children(root).Where(element => element.Name == "members").SelectMany(Children).Count(element => element.Name == "member");
                }
            }
            // The symbols are a package of their own. Each PDB carries the Source Link map from the build's
            // source root to this commit of the repository, as plain JSON.
            string symbols = PyPath.Join(feed, $"{id}.0.0.0-audit.snupkg");
            List<string> symbolNames;
            using (var archive = ZipFile.OpenRead(symbols))
            {
                symbolNames = Py.Sorted(archive.Entries.Select(entry => entry.FullName));
                var pdbs = symbolNames.Where(entry => entry.StartsWith("lib/", StringComparison.Ordinal)).ToList();
                Assert(pdbs.SequenceEqual(Py.Sorted(Targets.Select(tfm => $"lib/{tfm}/{name}.pdb"))), symbolNames);
                byte[] link = PyText.Utf8($"\"https://raw.githubusercontent.com/sensboston/MetadataSharp/{head}/*\"");
                foreach (string pdb in pdbs)
                {
                    byte[] data = Read(archive, pdb);
                    int at = data.AsSpan().IndexOf("{\"documents\""u8);
                    if (!Contains(data, link))
                        throw Failed(new PyTuple(pdb, at >= 0 ? data[at..Math.Min(data.Length, at + 300)] : "no Source Link map"));
                }
            }
            // Kept out of the feed the offline consumer restores from.
            string kept = PyPath.Join(symbolsDirectory, PyPath.Name(symbols));
            File.Move(symbols, kept, overwrite: true);
            reports.Add(PyDict.Of(("package", PyPath.Name(package)), ("sha256", Sha256(PyFile.ReadBytes(package))), ("entries", names.Cast<object?>().ToList()),
                                  ("dependencyGroups", groups), ("documentedMembers", docs), ("repository", repository), ("assemblyMetadata", markers),
                                  ("symbols", PyDict.Of(("package", PyPath.Name(symbols)), ("entries", symbolNames.Cast<object?>().ToList()), ("sourceLink", "verified")))));
        }
        string consumer = PyPath.Join(output, "consumer");
        Directory.CreateDirectory(consumer);
        string major = target[3..].Split('.')[0];
        PyFile.WriteText(PyPath.Join(consumer, "global.json"),
                         PyJson.Dumps(PyDict.Of(("sdk", PyDict.Of(("version", major + ".0.100"), ("rollForward", "latestFeature"), ("allowPrerelease", false))))));
        string consumerSdk = PyText.Strip(CheckOutput(consumer, "dotnet", "--version"));
        Assert(consumerSdk.StartsWith(major + ".", StringComparison.Ordinal), consumerSdk);
        PyFile.WriteText(PyPath.Join(consumer, "NuGet.Config"),
                         $"<configuration><packageSources><clear/><add key=\"audit\" value=\"{PyPath.Separators(feed)}\"/></packageSources></configuration>");
        // A unique cache and cleared sources ensure the smoke cannot use project references or an internet feed.
        PyFile.WriteText(PyPath.Join(consumer, "consumer.csproj"), ConsumerProject.Replace("AUDIT_TARGET", target, StringComparison.Ordinal));
        PyFile.WriteText(PyPath.Join(consumer, "Program.cs"), ConsumerProgram.Replace("EXPECTED_MAJOR", major, StringComparison.Ordinal));
        RunCommand(consumer, "dotnet", "restore", PyPath.Join(consumer, "consumer.csproj"), "--configfile", PyPath.Join(consumer, "NuGet.Config"), "--packages", PyPath.Join(output, "isolated-cache"));
        RunCommand(consumer, "dotnet", "run", "--project", PyPath.Join(consumer, "consumer.csproj"), "-c", "Release", "--no-restore", "--", PyPath.Join(output, "consumer.tif"));
        var report = PyDict.Of(("schema", 3L), ("targetFramework", target), ("consumerSdk", consumerSdk), ("publicSignatureSha256", signatureHash),
                               ("publicSignatureCount", (long)((System.Collections.IList)surfaces["net8.0"]["signatures"]!).Count),
                               ("sourceCommit", Environment.GetEnvironmentVariable("AUDIT_SOURCE_COMMIT")), ("sdkInfo", CheckOutput(null, "dotnet", "--info")),
                               ("packages", reports), ("offlineConsumer", "passed"), ("offlineConsumerAssets", "target-matched"), ("publication", "not attempted"),
                               ("docCaveat", "documentedMembers counts the members of the packaged XML files; CS1591 is not suppressed, so a public member without a comment fails every build of the libraries"));
        PyFile.WriteText(PyPath.Join(output, "packages.json"), PyJson.Dumps(report, indent: 2));
        // Persist small machine-readable evidence in Actions logs as well as artifacts.
        foreach (string file in new[] { "baseline.json", "packages.json" })
        {
            object? value = PyJson.Loads(PyFile.ReadText(PyPath.Join(output, file)));
            string encoded = Out.Compressed(PyJson.Compact(value));
            var chunks = Enumerable.Range(0, (encoded.Length + 2499) / 2500).Select(i => encoded.Substring(i * 2500, Math.Min(2500, encoded.Length - i * 2500))).ToList();
            for (int i = 0; i < chunks.Count; i++) Out.Print($"::notice title=M18Z {file} {i}/{chunks.Count}::" + chunks[i]);
        }
    }
}
