using System.Text;
using MetaTool.Cli;
using Xunit;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.MakerNotes;

namespace MetadataSharp.Tests.Samples;

/// <summary>
/// The argument grammar of the MetaTool command line. The grammar, not the usage text, is the
/// contract: repeatable filters, attached and separate option arguments, bundled flags, <c>--</c>
/// file lists, the target letters of extract/insert/delete, and the exit code of every outcome.
/// </summary>
public sealed class MetaToolGrammarTests
{
    [Fact]
    public void Attached_and_separate_option_arguments_read_the_same()
    {
        ParseOutcome attached = CommandLine.Parse(["-pa", "photo.jpg"]);
        ParseOutcome separate = CommandLine.Parse(["-p", "a", "photo.jpg"]);

        Assert.Null(attached.Error);
        Assert.Null(separate.Error);
        Assert.Equal(PrintFamilies.All, attached.Options.Families);
        Assert.Equal(attached.Options.Families, separate.Options.Families);
        Assert.Equal(attached.Options.Columns, separate.Options.Columns);
        Assert.Equal(new[] { "photo.jpg" }, attached.Options.Files);
    }

    [Fact]
    public void Flags_without_arguments_bundle_after_one_dash()
    {
        ParseOutcome parsed = CommandLine.Parse(["-fkq", "-p", "s", "photo.jpg"]);

        Assert.Null(parsed.Error);
        Assert.True(parsed.Options.Force);
        Assert.True(parsed.Options.PreserveTimestamps);
        Assert.True(parsed.Options.Quiet);
        Assert.Equal(PrintMode.Summary, parsed.Options.Mode);
    }

    [Fact]
    public void Filters_repeat()
    {
        ParseOutcome parsed = CommandLine.Parse([
            "-p", "a",
            "-K", "Exif.Image.Make",
            "-K", "Exif.Image.Model",
            "-g", "^Exif.Photo",
            "-g", @"Xmp\.dc",
            "photo.jpg"]);

        Assert.Null(parsed.Error);
        Assert.Equal(new[] { "Exif.Image.Make", "Exif.Image.Model" }, parsed.Options.ExactKeys);
        Assert.Equal(new[] { "^Exif.Photo", @"Xmp\.dc" }, parsed.Options.Patterns);
    }

    [Fact]
    public void Two_dashes_end_the_options_whatever_the_files_begin_with()
    {
        ParseOutcome parsed = CommandLine.Parse(["-p", "a", "--", "-not-an-option.jpg", "--plain.jpg"]);

        Assert.Null(parsed.Error);
        Assert.Equal(new[] { "-not-an-option.jpg", "--plain.jpg" }, parsed.Options.Files);
    }

    [Theory]
    [InlineData("pr", "Print")]
    [InlineData("print", "Print")]
    [InlineData("ex", "Extract")]
    [InlineData("extract", "Extract")]
    [InlineData("in", "Insert")]
    [InlineData("insert", "Insert")]
    [InlineData("rm", "Erase")]
    [InlineData("delete", "Erase")]
    [InlineData("ad", "Adjust")]
    [InlineData("adjust", "Adjust")]
    [InlineData("mo", "Modify")]
    [InlineData("modify", "Modify")]
    [InlineData("mv", "Rename")]
    [InlineData("rename", "Rename")]
    [InlineData("fi", "FixIso")]
    [InlineData("fixiso", "FixIso")]
    [InlineData("fc", "FixCom")]
    [InlineData("fixcom", "FixCom")]
    [InlineData("fixcomment", "FixCom")]
    public void Every_action_has_its_short_and_its_long_name(string word, string expected)
    {
        // The actions that need an amount, a command or a target get one, so that the word is what the
        // test measures and nothing else.
        var args = new List<string>();
        switch (expected)
        {
            case "Adjust":
                args.AddRange(["-a", "1:00"]);
                break;
            case "Modify":
                args.AddRange(["-M", "set Exif.Image.Artist Ada"]);
                break;
            case "Extract":
                args.AddRange(["-e", "e"]);
                break;
            case "Insert":
                args.AddRange(["-i", "a"]);
                break;
            case "Erase":
                args.AddRange(["-d", "e"]);
                break;
            default:
                break;
        }
        args.Add(word);
        args.Add("photo.jpg");

        ParseOutcome parsed = CommandLine.Parse(args);

        Assert.Null(parsed.Error);
        Assert.Equal(expected, parsed.Options.Action.ToString());
        Assert.Equal(new[] { "photo.jpg" }, parsed.Options.Files);
    }

    [Fact]
    public void A_file_name_alone_prints_the_summary()
    {
        ParseOutcome parsed = CommandLine.Parse(["photo.jpg"]);

        Assert.Null(parsed.Error);
        Assert.Equal(ToolAction.Print, parsed.Options.Action);
        Assert.False(parsed.Options.ModeSet);
        Assert.Equal(new[] { "photo.jpg" }, parsed.Options.Files);
    }

    [Fact]
    public void Preview_numbers_are_read_after_the_target_letter()
    {
        ParseOutcome parsed = CommandLine.Parse(["-ep2,3", "photo.jpg"]);

        Assert.Null(parsed.Error);
        Assert.True(parsed.Options.Targets.HasFlag(Targets.Preview));
        Assert.Equal(new[] { 2, 3 }, parsed.Options.PreviewNumbers);
    }

    [Fact]
    public void A_double_x_target_is_the_raw_packet_and_not_the_families()
    {
        ParseOutcome parsed = CommandLine.Parse(["-e", "XX", "photo.jpg"]);

        Assert.Null(parsed.Error);
        Assert.True(parsed.Options.Targets.HasFlag(Targets.XmpRaw));
        Assert.False(parsed.Options.Targets.HasFlag(Targets.Exif));
        Assert.False(parsed.Options.Targets.HasFlag(Targets.XmpSidecar));
    }

    [Fact]
    public void The_single_x_target_converts_and_carries_the_families()
    {
        ParseOutcome parsed = CommandLine.Parse(["-eX", "photo.jpg"]);

        Assert.Null(parsed.Error);
        Assert.True(parsed.Options.Targets.HasFlag(Targets.XmpSidecar));
        Assert.True(parsed.Options.Targets.HasFlag(Targets.Exif));
        Assert.False(parsed.Options.Targets.HasFlag(Targets.XmpRaw));
    }

    [Fact]
    public void A_time_adjustment_is_signed_and_nested()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), CommandLine.Parse(["-a", "1:30", "ad", "photo.jpg"]).Options.TimeAdjustment);
        Assert.Equal(TimeSpan.FromSeconds(-7215), CommandLine.Parse(["-a", "-2:00:15", "ad", "photo.jpg"]).Options.TimeAdjustment);
        Assert.NotNull(CommandLine.Parse(["-a", "1:75", "ad", "photo.jpg"]).Error);
    }

    [Fact]
    public void An_option_that_names_another_action_is_refused()
    {
        ParseOutcome parsed = CommandLine.Parse(["-p", "a", "-d", "e", "photo.jpg"]);

        Assert.NotNull(parsed.Error);
        Assert.Contains("not compatible", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognized_option_is_an_error()
    {
        ParseOutcome parsed = CommandLine.Parse(["-Z", "photo.jpg"]);

        Assert.NotNull(parsed.Error);
        Assert.Contains("-Z", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_action_that_needs_a_target_says_so()
    {
        Assert.Contains("target", CommandLine.Parse(["extract", "photo.jpg"]).Error!, StringComparison.Ordinal);
        Assert.Contains("target", CommandLine.Parse(["delete", "photo.jpg"]).Error!, StringComparison.Ordinal);
        Assert.Contains("command", CommandLine.Parse(["modify", "photo.jpg"]).Error!, StringComparison.Ordinal);
        Assert.Contains("amount", CommandLine.Parse(["adjust", "photo.jpg"]).Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Print_modes_the_tool_does_not_offer_are_named()
    {
        ParseOutcome parsed = CommandLine.Parse(["-pS", "photo.jpg"]);

        Assert.NotNull(parsed.Error);
        Assert.Contains("not offered", parsed.Error, StringComparison.Ordinal);
    }
}

/// <summary>
/// The actions and the reports, run end to end on an authored JPEG: the layout of a row, the exit
/// code of every outcome, and a modify script that comes from standard input.
/// </summary>
public sealed class MetaToolActionTests
{
    private static byte[] Image() => JpegFixture.Build(prefix: JpegFixture.Segment(0xE1, JpegFixture.Exif()));

    [Fact]
    public void A_row_holds_the_key_the_type_the_count_and_the_value()
    {
        string path = Temporary(Image());

        (string output, _, int code) = Run(["-pe", path]);

        Assert.Equal(ExitCodes.Ok, code);
        string line = Assert.Single(output.Split('\n'), row => row.StartsWith("Exif.Image.Orientation", StringComparison.Ordinal));
        Assert.StartsWith("Exif.Image.Orientation", line, StringComparison.Ordinal);
        Assert.Contains(" Short ", line, StringComparison.Ordinal);
        Assert.Contains("  1  1", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_columns_of_the_flags_are_written_in_the_order_of_the_reference()
    {
        string path = Temporary(Image());

        (string output, _, int code) = Run(["-P", "Exgky", path]);

        Assert.Equal(ExitCodes.Ok, code);
        string line = Assert.Single(output.Split('\n'), row => row.Contains("0x0112", StringComparison.Ordinal));
        Assert.StartsWith("0x0112 Image        Exif.Image.Orientation", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hex_column_dumps_the_value_on_its_own_line()
    {
        string path = Temporary(Image());

        (string output, _, int code) = Run(["-ph", path]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("  0000  01 00 ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_names_the_file_type_and_the_pixel_size()
    {
        string path = Temporary(Image());

        (string output, _, int code) = Run(["-ps", path]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("File name       : " + path, output, StringComparison.Ordinal);
        Assert.Contains("MIME type       : image/jpeg", output, StringComparison.Ordinal);
        Assert.Contains("Image size      : 8 x 8", output, StringComparison.Ordinal);
        Assert.Contains("Camera make", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filter_that_matches_nothing_exits_one()
    {
        string path = Temporary(Image());

        (string output, _, int code) = Run(["-pe", "-K", "Exif.Image.Artist", path]);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void A_file_that_cannot_be_read_exits_one_with_a_diagnostic()
    {
        (_, string error, int code) = Run(["-pe", Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".jpg")]);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("failed to open the file", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_option_exits_two()
    {
        (_, string error, int code) = Run(["-Z", "photo.jpg"]);

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("unrecognized option -Z", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_modify_script_reads_from_standard_input()
    {
        string path = Temporary(Image());

        (_, _, int code) = Run(["-m", "-", path], Encoding.UTF8.GetBytes("set Exif.Image.Artist Ada Lovelace\n"));
        Assert.Equal(ExitCodes.Ok, code);

        (string output, _, int artistCode) = Run(["-pe", "-K", "Exif.Image.Artist", path]);
        Assert.Equal(ExitCodes.Ok, artistCode);
        Assert.Contains("Ada Lovelace", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_modify_commands_apply_in_order()
    {
        string path = Temporary(Image());

        (_, _, int code) = Run([
            "-M", "set Exif.Image.Artist Ada",
            "-M", "set Exif.Image.Copyright \"Ada Lovelace\"",
            path]);

        Assert.Equal(ExitCodes.Ok, code);
        (string output, _, _) = Run(["-pe", "-K", "Exif.Image.Copyright", path]);
        Assert.Contains("Ada Lovelace", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Adjusting_moves_the_timestamp_of_the_file()
    {
        string path = Temporary(Image());

        (_, _, int set) = Run(["-M", "set Exif.Photo.DateTimeOriginal \"2024:01:01 10:00:00\"", path]);
        Assert.Equal(ExitCodes.Ok, set);

        (_, _, int adjusted) = Run(["-Y", "1", "-D", "1", "adjust", path]);
        Assert.Equal(ExitCodes.Ok, adjusted);

        (string output, _, _) = Run(["-pe", "-K", "Exif.Photo.DateTimeOriginal", path]);
        Assert.Contains("2025:01:02 10:00:00", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_a_family_leaves_the_others()
    {
        string path = Temporary(Image());

        (_, _, int code) = Run(["-d", "e", path]);
        Assert.Equal(ExitCodes.Ok, code);

        (string output, _, _) = Run(["-pe", path]);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void Extracting_a_thumbnail_says_when_there_is_none()
    {
        string path = Temporary(Image());

        (_, string error, int code) = Run(["-e", "t", path]);

        Assert.Equal(ExitCodes.Absent, code);
        Assert.Contains("does not contain an Exif thumbnail", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_and_insert_round_trip_an_exv_payload()
    {
        string path = Temporary(Image());

        (_, _, int set) = Run(["-M", "set Exif.Image.ImageDescription \"round trip\"", path]);
        Assert.Equal(ExitCodes.Ok, set);

        (_, _, int extracted) = Run(["-e", "a", path]);
        Assert.Equal(ExitCodes.Ok, extracted);
        string sidecar = Path.ChangeExtension(path, ".exv");
        Assert.True(File.Exists(sidecar), "the EXV payload was not written");

        (_, _, int deleted) = Run(["-d", "a", path]);
        Assert.Equal(ExitCodes.Ok, deleted);

        (_, _, int inserted) = Run(["-i", "a", path]);
        Assert.Equal(ExitCodes.Ok, inserted);

        (string output, _, _) = Run(["-pe", "-K", "Exif.Image.ImageDescription", path]);
        Assert.Contains("round trip", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MakerNote_tags_are_rows_of_the_print()
    {
        MakerNoteFixture.Vendor canon = MakerNoteFixture.Find("Canon");
        byte[] tiff = MakerNoteFixture.Build(canon, big: false, _ => [MakerNoteFixture.Short(0x0006, false, 1)]);
        string path = Temporary(JpegFixture.Build(prefix: JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff))));

        (string output, _, int code) = Run(["-pe", path]);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Exif.Canon", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_over_two_files_prefixes_every_line()
    {
        string first = Temporary(Image());
        string second = Temporary(Image());

        (string output, _, int code) = Run(["-pe", "-K", "Exif.Image.Orientation", first, second]);

        Assert.Equal(ExitCodes.Ok, code);
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith(first + "  Exif.Image.Orientation", lines[0], StringComparison.Ordinal);
        Assert.StartsWith(second + "  Exif.Image.Orientation", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_does_not_stop_the_next_one()
    {
        string path = Temporary(Image());
        string missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".jpg");

        (string output, string error, int code) = Run(["-pe", missing, path]);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("failed to open the file", error, StringComparison.Ordinal);
        Assert.Contains("Exif.Image.Orientation", output, StringComparison.Ordinal);
        File.Delete(path);
    }

    private static string Temporary(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), "metatool-" + Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static (string Output, string Error, int Code) Run(string[] args, byte[]? standardInput = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var input = new MemoryStream(standardInput ?? []);
        using var binary = new MemoryStream();
        int code = Runner.Run(args, output, error, input, binary);
        return (output.ToString(), error.ToString(), code);
    }
}

/// <summary>The parts of a run that are not a report: exit codes, numbered names, and the payload policy.</summary>
public sealed class MetaToolSupportTests
{
    [Fact]
    public void Exit_codes_are_the_documented_ones()
    {
        string path = Temporary();

        Assert.Equal(ExitCodes.Ok, Run(["-pe", path], out _, out _));
        Assert.Equal(ExitCodes.Failed, Run(["-pe", "-K", "Exif.Image.Artist", path], out _, out _));
        Assert.Equal(ExitCodes.Usage, Run(["-Z", path], out _, out _));
        Assert.Equal(ExitCodes.Absent, Run(["-e", "t", path], out _, out _));
        Assert.Equal(ExitCodes.Refused, Run(["-i", "I", path], out _, out _));
    }

    [Fact]
    public void A_name_that_is_taken_is_numbered_and_never_replaced()
    {
        string directory = Path.Combine(Path.GetTempPath(), "metatool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string payload = Path.Combine(directory, "photo.exv");
        File.WriteAllBytes(payload, [1]);
        Session session = Session();

        string first = Payloads.Free(session, payload);
        Assert.Equal(Path.Combine(directory, "photo_1.exv"), first);
        File.WriteAllBytes(first, [2]);
        Assert.Equal(Path.Combine(directory, "photo_2.exv"), Payloads.Free(session, payload));

        session.Options.Force = true;
        Assert.Equal(payload, Payloads.Free(session, payload));
        Directory.Delete(directory, recursive: true);
    }

    private static Session Session() =>
        new(new Options(), new StringWriter(), new StringWriter(), new MemoryStream(), new MemoryStream());

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), "metatool-" + Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(path, JpegFixture.Build(prefix: JpegFixture.Segment(0xE1, JpegFixture.Exif())));
        return path;
    }

    private static int Run(string[] args, out string output, out string error)
    {
        using var writer = new StringWriter();
        using var failure = new StringWriter();
        using var input = new MemoryStream();
        using var binary = new MemoryStream();
        int code = Runner.Run(args, writer, failure, input, binary);
        output = writer.ToString();
        error = failure.ToString();
        return code;
    }
}
