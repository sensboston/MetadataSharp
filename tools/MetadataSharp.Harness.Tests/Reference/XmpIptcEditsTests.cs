using MetadataSharp.Harness.Python;
using MetadataSharp.Harness.Reference;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Reference;

// Offline checks for the rule that the pinned tool prints every XMP property an edit wrote (formerly
// tests/reference/test_xmp_edits.py). The tool itself is not run here: its print-outs are authored strings.
public sealed class XmpEditsTests
{
    private const string Printed =
        "Exif.Image.Make                              Ascii      12  TestCompany\n"
        + "Xmp.xmp.Rating                               XmpText     1  4\n"
        + "Xmp.dc.subject                               XmpBag      2  harbour, dawn\n"
        + "Xmp.xmpMM.History                            XmpText    10  type=\"Seq\"\n"
        + "Xmp.xmpMM.History[1]/stEvt:action            XmpText     7  created\n"
        + "Xmp.xmpNote.HasExtendedXMP                   XmpText    32  0123456789ABCDEF0123456789ABCDEF\n";

    private static readonly string[] Expected =
    [
        "Xmp.xmp.Rating XmpText 1 4",
        "Xmp.dc.subject XmpBag 2 harbour, dawn",
        "Xmp.xmpMM.History XmpText 10 type=\"Seq\"",
        "Xmp.xmpMM.History[1]/stEvt:action XmpText 7 created",
        "Xmp.xmpNote.HasExtendedXMP XmpText 32 *",
    ];

    private const string Packet =
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
        + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\"/>"
        + "</rdf:RDF></x:xmpmeta>   \n<?xpacket end=\"w\"?>\n";

    private static string Without(string text, string part) => string.Join("\n", PyText.SplitLines(text).Where(line => !line.Contains(part, StringComparison.Ordinal)));

    [Fact]
    public void APrintOutOfExactlyTheWrittenPropertiesPasses() => Assert.Empty(XmpEdits.Compare(Printed, Expected));

    [Fact]
    public void AValueThatDiffersIsAProblem() =>
        Assert.Equal(["not printed: Xmp.dc.subject XmpBag 2 harbour, dawn"], XmpEdits.Compare(Printed.Replace("harbour, dawn", "harbour, dusk", StringComparison.Ordinal), Expected));

    [Fact]
    public void APropertyThatIsMissingIsAProblem() =>
        Assert.Equal(["not printed: Xmp.xmp.Rating XmpText 1 4"], XmpEdits.Compare(Without(Printed, "Rating"), Expected));

    [Fact]
    public void APropertyNobodyWroteIsAProblem() =>
        Assert.Equal(["printed, not written: Xmp.dc.format XmpText 10 image/jpeg"], XmpEdits.Compare(Printed + "Xmp.dc.format  XmpText  10  image/jpeg\n", Expected));

    [Fact]
    public void AnyValueStillNeedsTheKeyTheTypeAndTheCount() =>
        Assert.Equal(["not printed: Xmp.xmpNote.HasExtendedXMP XmpText 32 *"], XmpEdits.Compare(Without(Printed, "xmpNote"), Expected));

    [Fact]
    public void OtherFamiliesAreNotLookedAt() => Assert.Empty(XmpEdits.Compare(Printed + "Iptc.Application2.City  String  6  Boston\n", Expected));

    [Fact]
    public void APacketWithItsWrapperPasses() => Assert.Null(XmpEdits.PacketProblem(Packet));

    [Fact]
    public void ABrokenPacketIsAProblem()
    {
        Assert.Contains("not well-formed", XmpEdits.PacketProblem(Packet.Replace("</rdf:RDF>", "", StringComparison.Ordinal)));
        Assert.Contains("not well-formed", XmpEdits.PacketProblem(""));
    }

    [Fact]
    public void XmlWithoutRdfIsAProblem() => Assert.Contains("no rdf:RDF", XmpEdits.PacketProblem("<a/>"));

    private static (int Code, string Out) RunMain(string manifest, TextRunner runner)
    {
        using var directory = Temp();
        Write(directory["manifest.tsv"], manifest);
        var (code, stdout, _) = Capture(() => XmpEdits.Execute([directory.Path, "--exiv2", "exiv2"], runner));
        return (code, stdout);
    }

    private static readonly string Row = "a.jpg\t" + string.Join("|", Expected) + "\n";

    [Fact]
    public void BothPrintOutsAreAskedForEveryFile()
    {
        var asked = new List<(string, string)>();
        var (code, output) = RunMain(Row, (command, path) =>
        {
            asked.Add((command[1], PyPath.Name(path)));
            return new(0, command[1] == "-pX" ? Packet : Printed, "");
        });
        if (code != 0) Assert.Fail(output);
        Assert.Equal([("-pa", "a.jpg"), ("-pX", "a.jpg")], asked);
        Assert.Contains("1 files", output);
    }

    [Fact]
    public void ASidecarIsThePacketAndIsNotAskedForIt()
    {
        var asked = new List<string>();
        using var directory = Temp();
        Write(directory["manifest.tsv"], "a.xmp\t" + string.Join("|", Expected) + "\nb.xmp\t" + string.Join("|", Expected) + "\n");
        Write(directory["a.xmp"], Packet);
        Write(directory["b.xmp"], "<a/>");
        var (problems, count) = XmpEdits.Check(directory.Path, "exiv2", (command, _) =>
        {
            asked.Add(command[1]);
            return new(0, Printed, "");
        });
        Assert.Equal(["-pa", "-pa"], asked);
        Assert.Equal(2, count);
        Assert.Equal([("b.xmp", "the sidecar: the packet holds no rdf:RDF element")], problems);
    }

    [Fact]
    public void AFileTheToolDoesNotReadFails()
    {
        var (code, output) = RunMain(Row, (_, _) => new(1, "", "no"));
        Assert.Equal(1, code);
        Assert.Contains("-pa: not read (exit 1)", output);
        Assert.Contains("-pX: not read (exit 1)", output);
    }

    [Fact]
    public void AProblemCarriesThePrintOut()
    {
        var (code, output) = RunMain(Row, (command, _) => new(0, command[1] == "-pX" ? Packet : Printed.Replace("  4\n", "  5\n", StringComparison.Ordinal), ""));
        Assert.Equal(1, code);
        Assert.Contains("not printed: Xmp.xmp.Rating XmpText 1 4", output);
        Assert.Contains("Xmp.xmp.Rating XmpText 1 5", output);
    }

    [Fact]
    public void AManifestWithoutRowsIsRefused()
    {
        Assert.Equal("ValueError", Assert.Throws<PyException>(() => XmpEdits.ParseManifest("\n")).Type);
        Assert.Equal("ValueError", Assert.Throws<PyException>(() => XmpEdits.ParseManifest("only one column\n")).Type);
    }
}

// Offline checks for the rule that the pinned tool prints every IPTC dataset an edit wrote (formerly
// tests/reference/test_iptc_edits.py). The tool itself is not run here: its print-outs are authored strings.
public sealed class IptcEditsTests
{
    private const string Printed =
        "Exif.Image.Make                              Ascii      12  TestCompany\n"
        + "Iptc.Envelope.CharacterSet                   String      3  \u001b%G\n"
        + "Iptc.Application2.RecordVersion              Short       1  4\n"
        + "Iptc.Application2.Keywords                   String      7  harbour\n"
        + "Iptc.Application2.Keywords                   String      4  dawn\n"
        + "Iptc.Application2.DateCreated                Date        8  2026-11-12\n"
        + "Iptc.Application2.Caption                    String      9  two words\n"
        + "Xmp.xmp.Rating                               XmpText     1  4\n";

    private static readonly string[] Expected =
    [
        "Iptc.Envelope.CharacterSet String 3 *",
        "Iptc.Application2.RecordVersion Short 1 4",
        "Iptc.Application2.Keywords String 7 harbour",
        "Iptc.Application2.Keywords String 4 dawn",
        "Iptc.Application2.DateCreated Date 8 2026-11-12",
        "Iptc.Application2.Caption String 9 two words",
    ];

    private static string Without(string text, string part) => string.Join("\n", PyText.SplitLines(text).Where(line => !line.Contains(part, StringComparison.Ordinal)));

    [Fact]
    public void APrintOutOfExactlyTheWrittenDatasetsPasses() => Assert.Empty(IptcEdits.Compare(Printed, Expected));

    [Fact]
    public void TheOrderOfTheLinesIsNotCompared() => Assert.Empty(IptcEdits.Compare(Printed, Expected.Reverse()));

    [Fact]
    public void AValueThatDiffersIsAProblemOnBothSides() =>
        Assert.Equal(["not printed: Iptc.Application2.Keywords String 4 dawn", "printed, not written: Iptc.Application2.Keywords String 4 dusk"],
                     IptcEdits.Compare(Printed.Replace("dawn", "dusk", StringComparison.Ordinal), Expected));

    [Fact]
    public void AnOccurrenceThatIsMissingIsAProblem() =>
        Assert.Equal(["not printed: Iptc.Application2.Keywords String 7 harbour"], IptcEdits.Compare(Without(Printed, "harbour"), Expected));

    [Fact]
    public void AnOccurrencePrintedTwiceIsAProblem() =>
        Assert.Equal(["printed, not written: Iptc.Application2.Keywords String 4 dawn"], IptcEdits.Compare(Printed + "Iptc.Application2.Keywords  String  4  dawn\n", Expected));

    [Fact]
    public void ADatasetNobodyWroteIsAProblem() =>
        Assert.Equal(["printed, not written: Iptc.Application2.City String 4 Riga"], IptcEdits.Compare(Printed + "Iptc.Application2.City  String  4  Riga\n", Expected));

    [Fact]
    public void AnyValueStillNeedsTheKeyTheTypeAndTheCount() =>
        Assert.Equal(["not printed: Iptc.Envelope.CharacterSet String 3 *"], IptcEdits.Compare(Without(Printed, "CharacterSet"), Expected));

    [Fact]
    public void OtherFamiliesAreNotLookedAt() => Assert.Empty(IptcEdits.Compare(Printed + "Xmp.dc.format  XmpText  10  image/jpeg\n", Expected));

    [Fact]
    public void RowsAreAFileAndItsLines()
    {
        var entry = Assert.Single(IptcEdits.ParseManifest("a.jpg\tIptc.Application2.City String 4 Riga|Iptc.Application2.Urgency String 1 5\n"));
        Assert.Equal("a.jpg", entry.Name);
        Assert.Equal(["Iptc.Application2.City String 4 Riga", "Iptc.Application2.Urgency String 1 5"], entry.Expected);
    }

    [Fact]
    public void AManifestWithoutFilesOrColumnsIsRefused()
    {
        Assert.Equal("ValueError", Assert.Throws<PyException>(() => IptcEdits.ParseManifest("")).Type);
        Assert.Equal("ValueError", Assert.Throws<PyException>(() => IptcEdits.ParseManifest("a.jpg\n")).Type);
    }

    private static TempDirectory Folder()
    {
        var folder = Temp();
        Write(folder["manifest.tsv"], "a.jpg\t" + string.Join("|", Expected) + "\n");
        return folder;
    }

    [Fact]
    public void BothPrintModesAreRunForEveryFile()
    {
        var calls = new List<(string, string)>();
        using var folder = Folder();
        var (problems, count) = IptcEdits.Check(folder.Path, "exiv2", (command, path) =>
        {
            calls.Add((string.Join(" ", command.Skip(1)), PyPath.Name(path)));
            return new(0, Printed, "");
        });
        Assert.Equal((0, 1), (problems.Count, count));
        Assert.Equal([("-u -pi", "a.jpg"), ("-u -pa", "a.jpg")], calls);
    }

    [Fact]
    public void AFileTheToolRefusesIsAProblem()
    {
        using var folder = Folder();
        var (problems, _) = IptcEdits.Check(folder.Path, "exiv2", (_, _) => new(1, "", "corrupted"));
        Assert.Equal([("a.jpg", "-pi: not read (exit 1): corrupted"), ("a.jpg", "-pa: not read (exit 1): corrupted")], problems);
    }

    [Fact]
    public void TheFirstProblemOfAFileCarriesThePrintOut()
    {
        using var folder = Folder();
        var (problems, _) = IptcEdits.Check(folder.Path, "exiv2", (_, _) => new(0, "Iptc.Application2.City  String  4  Riga\n", ""));
        Assert.Contains("printed:\nIptc.Application2.City String 4 Riga", problems[0].Message);
    }

    [Fact]
    public void MainReportsAndFailsOrNotesAndPasses()
    {
        using var folder = Folder();
        var passed = Capture(() => IptcEdits.Execute([folder.Path, "--exiv2", "exiv2"], (_, _) => new(0, Printed, "")));
        Assert.Equal(0, passed.Code);
        Assert.Contains("::notice title=IPTC edits::1 files", passed.Stdout);
        var failed = Capture(() => IptcEdits.Execute([folder.Path, "--exiv2", "exiv2"], (_, _) => new(1, "", "no")));
        Assert.Equal(1, failed.Code);
        Assert.Contains("::error title=IPTC edits a.jpg::", failed.Stdout);
    }
}
