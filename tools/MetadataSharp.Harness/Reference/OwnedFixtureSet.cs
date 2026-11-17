using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>
/// What every owned fixture writer of the former tools/reference shared: a catalog of named fixtures, the manifest of their
/// SHA-256 and lengths, a provenance document, generate / verify / print-manifest / print-provenance, and the
/// messages they print.
/// </summary>
internal sealed class OwnedFixtureSet(string command, string manifest, string pattern, string defaultOutdir,
                                      Func<SortedDictionary<string, OwnedFixture>> catalog, Func<List<object?>, PyDict> provenance)
{
    public string Manifest { get; } = manifest;

    public SortedDictionary<string, OwnedFixture> Fixtures => catalog();

    public List<object?> ManifestEntries() => JpegOwnedFixtures.ManifestOf(Fixtures);

    public PyDict Provenance() => provenance(ManifestEntries());

    public void VerifyDir(string outdir)
    {
        var pinned = new SortedDictionary<string, PyDict>(StringComparer.Ordinal);
        foreach (var item in (List<object?>)PyJson.Loads(PyFile.ReadText(Manifest))!) pinned[((PyDict)item!).Str("file")] = (PyDict)item!;
        var present = PyPath.Glob(outdir, pattern).Select(PyPath.Name).ToHashSet(StringComparer.Ordinal);
        var missing = pinned.Keys.Where(name => !present.Contains(name)).ToList();
        if (missing.Count > 0) throw new PyException("ValueError", $"missing fixtures: {Py.Repr(missing.Cast<object?>().ToList())}");
        foreach (var (name, entry) in pinned)
        {
            byte[] data = PyFile.ReadBytes(PyPath.Join(outdir, name));
            if (JpegOwnedFixtures.Sha256(data) != entry.Str("sha256") || data.Length != entry.Long("length"))
                throw new PyException("ValueError", $"fixture drifted: {name}");
        }
        Out.Print($"verified {pinned.Count} fixtures against {Manifest}");
    }

    public int Generate(string outdir)
    {
        PyFile.MakeDirs(outdir);
        foreach (var (name, spec) in Fixtures) PyFile.WriteBytes(PyPath.Join(outdir, name), spec.Bytes);
        PyFile.WriteText(PyPath.Join(outdir, "manifest.json"), PyJson.Dumps(ManifestEntries(), indent: 2) + "\n");
        return Fixtures.Count;
    }

    public int Run(string[] argv)
    {
        var parser = new ArgParser(command)
            .Add("command", choices: ["generate", "verify", "print-manifest", "print-provenance"])
            .Add("outdir", nargs: "?");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string? outdir = args.StrOrNull("outdir");
        switch (args.Str("command"))
        {
            case "generate":
                string target = outdir ?? defaultOutdir;
                Out.Print($"wrote {Generate(target)} fixtures to {target}");
                break;
            case "verify":
                VerifyDir(outdir ?? defaultOutdir);
                break;
            case "print-manifest":
                Console.Out.Write(PyJson.Dumps(ManifestEntries(), indent: 2) + "\n");
                break;
            case "print-provenance":
                Console.Out.Write(PyJson.Dumps(Provenance(), indent: 2) + "\n");
                break;
        }
        return 0;
    }
}
