using System.Security.Cryptography;
using MetadataSharp.Harness.Parity;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;
using Expectation = MetadataSharp.Harness.Parity.Icc.Expectation;

namespace MetadataSharp.Harness.Tests.Parity;

// Offline rules of the ICC comparison: how the three expectations and the dump are read, and how a file's
// profile is judged. No external tool runs here. (Formerly tests/parity/test_icc_parity.py.)
internal static class IccSamples
{
    public static readonly string A = new('a', 32), B = new('b', 32), C = new('c', 32);

    public static readonly string BashTest = """

        class TestCases(unittest.TestCase):
            def geotag_test(self):
                for img in ['other.jpg']:
                    pass

            def icc_test(self):
                def test1120(img):
                    return img
                out = BT.Output()
                for img in ['one.jpg'
                            ,'two.webp'
                            ]:
                    stub = img.split('.')[0]
                    for i in ['large.icc', 'small.icc', img]:
                        BT.copyTestFile(i)

        """.ReplaceLineEndings("\n");

    public static readonly string BashOutput = string.Join("\n", "STRUCTURE OF JPEG FILE: one.jpg", A, B, B, C, C, C, "abcdefg", "Exif.Image.Make",
                                                           B, C, C, A, A, A, "");

    public static readonly string Md5Case = $$"""

        import system_tests

        class IccProfileInApp2Segment(metaclass=system_tests.CaseMeta):
            num = 1074
            filenames = [
                system_tests.path("$data_path/" + fname)
                for fname in ("bug$num.png", "three.tiff", "one.jpg")
            ]
            commands = ["$exiv2 -pC " + fname for fname in filenames]
            stderr = [bytes()] * len(filenames)
            stdout = ["{{C}}", "{{B}}", "{{A}}"]
            retval = [0] * len(filenames)

        """.ReplaceLineEndings("\n");

    public static readonly byte[] Profile = Enumerable.Range(0, 200).Select(value => (byte)value).ToArray();

    public static readonly string BinaryCase = """

        import system_tests
        bSkip = False

        class with_a_profile(metaclass=system_tests.CaseMeta):
            filename = "$data_path/four.heic"
            if bSkip:
                commands=[]
                stdout=[]
            else:
                commands = ["$exiv2 -pS          $filename"
                           ,"$exiv2 -pC --binary $filename"
                           ]
                retval = [ 0  ] * len(commands)
                stdout = [\"\"\"a structure
        \"\"\",\"\"\"data:@PROFILE
        \"\"\"]

        class without_one(metaclass=system_tests.CaseMeta):
            filename = "$data_path/five.avif"
            if bSkip:
                commands=[]
                stdout=[]
            else:
                commands = ["$exiv2 -pC $filename", "$exiv2 -pX $filename"]
                stdout = ["", "a packet"]

        """.ReplaceLineEndings("\n").Replace("\\\"", "\"", StringComparison.Ordinal).Replace("@PROFILE", Wrapped(), StringComparison.Ordinal);

    public static readonly string ProfileSum = Convert.ToHexStringLower(MD5.HashData(Profile));

    private static string Wrapped()
    {
        string text = Convert.ToBase64String(Profile);
        var lines = new List<string>();
        for (int start = 0; start < 268; start += 59) lines.Add(text[start..Math.Min(text.Length, start + 59)]);
        return string.Join("\n", lines);
    }

    public static OrderedDictionary<string, Expectation?> Expect(params (string Name, Expectation? Value)[] entries)
    {
        var result = new OrderedDictionary<string, Expectation?>(StringComparer.Ordinal);
        foreach (var (name, value) in entries) result[name] = value;
        return result;
    }

    public static Dictionary<string, Expectation?> Plain(OrderedDictionary<string, Expectation?> expected) =>
        expected.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}

public sealed class IccBashTestTests
{
    [Fact]
    public void TheFirstSumOfEachSixIsTheProfileOfTheUntouchedImage()
    {
        Assert.Equal(["one.jpg", "two.webp"], Icc.BashImages(IccSamples.BashTest));
        Assert.Equal([[IccSamples.A, IccSamples.B, IccSamples.B, IccSamples.C, IccSamples.C, IccSamples.C],
                      [IccSamples.B, IccSamples.C, IccSamples.C, IccSamples.A, IccSamples.A, IccSamples.A]], Icc.BashSums(IccSamples.BashOutput));
        Assert.Equal(new Dictionary<string, Expectation?> { ["one.jpg"] = new(IccSamples.A, null), ["two.webp"] = new(IccSamples.B, null) },
                     IccSamples.Plain(Icc.ParseBash(IccSamples.BashTest, IccSamples.BashOutput)));
    }

    public static TheoryData<string, string> Damage => new()
    {
        { IccSamples.BashTest.Replace("icc_test", "other_test", StringComparison.Ordinal), IccSamples.BashOutput },
        { IccSamples.BashTest.Replace("for img in ['one.jpg'", "for img in images + ['one.jpg'", StringComparison.Ordinal), IccSamples.BashOutput },
        { IccSamples.BashTest.Replace(",'two.webp'", "", StringComparison.Ordinal), IccSamples.BashOutput },
        { IccSamples.BashTest.Replace("'two.webp'", "'one.jpg'", StringComparison.Ordinal), IccSamples.BashOutput },
        { IccSamples.BashTest, IccSamples.BashOutput.Replace(IccSamples.C + "\nabcdefg", "abcdefg", StringComparison.Ordinal) },
        { IccSamples.BashTest + "    def broken(:\n", IccSamples.BashOutput },
    };

    [Theory]
    [MemberData(nameof(Damage))]
    public void DamageIsAnErrorAndNotASmallerExpectation(string test, string output)
    {
        Assert.Throws<IccParityError>(() => Icc.ParseBash(test, output));
    }
}

public sealed class IccMd5CaseTests
{
    [Fact]
    public void EachFileNameTakesTheSumAtItsPlace()
    {
        Assert.Equal(new Dictionary<string, Expectation?>
                     {
                         ["bug1074.png"] = new(IccSamples.C, null), ["three.tiff"] = new(IccSamples.B, null), ["one.jpg"] = new(IccSamples.A, null),
                     },
                     IccSamples.Plain(Icc.ParseMd5Case(IccSamples.Md5Case)));
    }

    public static TheoryData<string> Damage => new()
    {
        IccSamples.Md5Case.Replace("\"$exiv2 -pC \"", "\"$exiv2 -pX \"", StringComparison.Ordinal),
        IccSamples.Md5Case.Replace(", \"one.jpg\"", "", StringComparison.Ordinal),
        IccSamples.Md5Case.Replace(IccSamples.C, "not a sum", StringComparison.Ordinal),
        IccSamples.Md5Case.Replace("    num = 1074\n", "", StringComparison.Ordinal),
        IccSamples.Md5Case.Replace("\"three.tiff\"", "\"one.jpg\"", StringComparison.Ordinal),
        "import system_tests\n",
    };

    [Theory]
    [MemberData(nameof(Damage))]
    public void DamageIsAnError(string text)
    {
        Assert.Throws<IccParityError>(() => Icc.ParseMd5Case(text));
    }
}

public sealed class IccBinaryCaseTests
{
    [Fact]
    public void TheOutputAtThePlaceOfTheCommandIsAProfileOrNothing()
    {
        Assert.Equal(new Dictionary<string, Expectation?> { ["four.heic"] = new(IccSamples.ProfileSum, 200), ["five.avif"] = null },
                     IccSamples.Plain(Icc.ParseBinaryCase(IccSamples.BinaryCase, "case.py")));
    }

    public static TheoryData<string> Damage => new()
    {
        IccSamples.BinaryCase.Replace("\"data:", "\"date:", StringComparison.Ordinal),
        IccSamples.BinaryCase.Replace("data:AAEC", "data:A*EC", StringComparison.Ordinal),
        IccSamples.BinaryCase.Replace(",\"$exiv2 -pC --binary $filename\"\n", "", StringComparison.Ordinal),
        IccSamples.BinaryCase.Replace("-pC --binary", "-pX --binary", StringComparison.Ordinal),
        IccSamples.BinaryCase.Replace("five.avif", "four.heic", StringComparison.Ordinal),
        IccSamples.BinaryCase.Replace("[\"\", \"a packet\"]", "[\"\"]", StringComparison.Ordinal),
        "import system_tests\n",
    };

    [Theory]
    [MemberData(nameof(Damage))]
    public void DamageIsAnError(string text)
    {
        Assert.Throws<IccParityError>(() => Icc.ParseBinaryCase(text, "case.py"));
    }
}

public sealed class IccMergeTests
{
    [Fact]
    public void TestsThatAgreeGiveOneExpectationAndTheLengthWhereOneKnowsIt()
    {
        var (expected, origin) = Icc.Merge(new Dictionary<string, OrderedDictionary<string, Expectation?>>
        {
            ["b"] = IccSamples.Expect(("one.jpg", new(IccSamples.A, null)), ("none.avif", null)),
            ["a"] = IccSamples.Expect(("one.jpg", new(IccSamples.A, 560)), ("two.png", new(IccSamples.B, null))),
            ["c"] = IccSamples.Expect(("none.avif", null), ("one.jpg", new(IccSamples.A, null))),
        });
        Assert.Equal(new Dictionary<string, Expectation?> { ["one.jpg"] = new(IccSamples.A, 560), ["two.png"] = new(IccSamples.B, null), ["none.avif"] = null },
                     IccSamples.Plain(expected));
        Assert.Equal(new Dictionary<string, List<string>> { ["one.jpg"] = ["a", "b", "c"], ["two.png"] = ["a"], ["none.avif"] = ["b", "c"] }, origin);
    }

    [Fact]
    public void TestsThatDisagreeAreDamage()
    {
        Assert.Throws<IccParityError>(() => Icc.Merge(new Dictionary<string, OrderedDictionary<string, Expectation?>>
        {
            ["a"] = IccSamples.Expect(("one.jpg", new(IccSamples.A, null))), ["b"] = IccSamples.Expect(("one.jpg", new(IccSamples.B, null))),
        }));
        Assert.Throws<IccParityError>(() => Icc.Merge(new Dictionary<string, OrderedDictionary<string, Expectation?>>
        {
            ["a"] = IccSamples.Expect(("one.jpg", new(IccSamples.A, null))), ["b"] = IccSamples.Expect(("one.jpg", null)),
        }));
    }
}

public sealed class IccDumpTests
{
    [Fact]
    public void RecordsAreGroupedByFile()
    {
        var blocks = Icc.ParseDump($"FILE\t/c/one.jpg\nICC\t560\t{IccSamples.A}\nSTATUS\tok\nFILE\t/c/two.png\nSTATUS\tok\nFILE\t/c/x\nSTATUS\trefused\tInvalidIccProfile\n");
        Assert.Equal(((IccSamples.A, 560L), "ok"), (blocks["/c/one.jpg"].Profile!.Value, blocks["/c/one.jpg"].Status));
        Assert.Equal((false, "ok"), (blocks["/c/two.png"].Profile.HasValue, blocks["/c/two.png"].Status));
        Assert.Equal(("refused", "InvalidIccProfile"), (blocks["/c/x"].Status, blocks["/c/x"].Detail));
    }

    public static TheoryData<string> Malformed => new()
    {
        $"ICC\t560\t{IccSamples.A}\n", "FILE\t/a\n", $"FILE\t/a\nICC\tmany\t{IccSamples.A}\nSTATUS\tok\n", "FILE\t/a\nICC\t560\tA\nSTATUS\tok\n",
        $"FILE\t/a\nICC\t560\t{IccSamples.A}\nICC\t560\t{IccSamples.A}\nSTATUS\tok\n", $"FILE\t/a\nSTATUS\tok\nICC\t560\t{IccSamples.A}\n",
        "FILE\t/a\nFILE\t/b\nSTATUS\tok\n", "FILE\t/a\nPREVIEW\t1\timage/jpeg\t1x1\t9\tx\nSTATUS\tok\n",
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedDumpIsRefused(string text)
    {
        Assert.Throws<IccParityError>(() => Icc.ParseDump(text));
    }
}

public sealed class IccComparisonTests
{
    private static readonly (string, long)? NoProfile = null;

    [Fact]
    public void AProfileIsJudgedByItsSumAndByItsLengthWhereTheTestGivesOne()
    {
        Assert.Equal("match", Icc.Judge(new(IccSamples.A, null), (IccSamples.A, 560)));
        Assert.Equal("match", Icc.Judge(new(IccSamples.A, 560), (IccSamples.A, 560)));
        Assert.Equal("match", Icc.Judge(null, NoProfile));
        Assert.Equal("differs", Icc.Judge(new(IccSamples.A, null), (IccSamples.B, 560)));
        Assert.Equal("differs", Icc.Judge(new(IccSamples.A, 560), (IccSamples.A, 561)));
        Assert.Equal("missing", Icc.Judge(new(IccSamples.A, null), NoProfile));
        Assert.Equal("extra", Icc.Judge(null, (IccSamples.A, 560)));
    }

    [Fact]
    public void TotalsCountEveryFileAndAFileThatWasNotReadIsItsStatus()
    {
        var expected = IccSamples.Expect(("one.jpg", new(IccSamples.A, null)), ("two.png", new(IccSamples.B, 9)), ("none.avif", null),
                                         ("gone.jpg", new(IccSamples.C, null)), ("bad.jpg", new(IccSamples.C, null)));
        var origin = expected.Keys.ToDictionary(name => name, _ => new List<string> { "a" });
        var paths = expected.Keys.ToDictionary(name => name, name => name == "gone.jpg" ? null : PyPath.Join("/c", name));
        var dumped = new Dictionary<string, Icc.DumpBlock>
        {
            [Icc.Key("/c/one.jpg")] = new() { Profile = (IccSamples.A, 560), Status = "ok" },
            [Icc.Key("/c/two.png")] = new() { Status = "ok" },
            [Icc.Key("/c/none.avif")] = new() { Status = "ok" },
            [Icc.Key("/c/bad.jpg")] = new() { Status = "refused", Detail = "InvalidIccProfile" },
        };
        var report = Icc.Compare(expected, origin, dumped, paths);
        Assert.Equal("Icc", report.Str("kind"));
        AssertJson("""{"files": 5, "profiles": 4, "match": 2, "missing": 1, "no-input": 1, "refused": 1}""", report["totals"]);
        AssertJson("""{"ok": 3, "no-input": 1, "refused": 1}""", report["statuses"]);
        Assert.Equal(0.4, (double)report["rate"]!);
        var differing = Icc.Summary(report).List("differing").Cast<PyDict>().Select(entry => entry.Str("file")).ToList();
        Assert.Equal(["bad.jpg", "gone.jpg", "two.png"], differing);
        string text = Icc.TextReport(report);
        Assert.Contains("bad.jpg [refused] refused", text);
        Assert.Contains("InvalidIccProfile", text);
        Assert.Contains("two.png [ok] missing", text);
    }

    [Fact]
    public void AFileIsLookedUpUnderTheForwardSlashPathTheDumpToolWasGiven()
    {
        // A native Windows path would come back from the tool with its backslashes escaped.
        if (OperatingSystem.IsWindows()) Assert.Equal("C:/corpus/one.jpg", Icc.Key(@"C:\corpus\one.jpg"));
        Assert.Equal("/c/one.jpg", Icc.Key("/c/one.jpg"));
        var dumped = Icc.ParseDump($"FILE\t/c/one.jpg\nICC\t560\t{IccSamples.A}\nSTATUS\tok\n");
        var report = Icc.Compare(IccSamples.Expect(("one.jpg", new(IccSamples.A, null))), [], dumped,
                                 new Dictionary<string, string?> { ["one.jpg"] = PyPath.Join("/c", "one.jpg") });
        AssertJson("""{"files": 1, "profiles": 1, "match": 1}""", report["totals"]);
    }

    [Fact]
    public void AFileTheDumpDidNotAnswerForIsAStructuralFailure()
    {
        Assert.Throws<IccParityError>(() => Icc.Compare(IccSamples.Expect(("one.jpg", new(IccSamples.A, null))), [], [],
                                                        new Dictionary<string, string?> { ["one.jpg"] = "/c/one.jpg" }));
    }
}
