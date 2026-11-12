using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Operations;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using Xunit;

namespace MetadataSharp.Tests.Operations;

/// <summary>The operations of the exiv2 command line as library calls: keys, timestamp adjustment, file names, modify scripts.</summary>
public sealed class OperationsTests
{
    private const ExifDirectoryKind Ifd0 = ExifDirectoryKind.Ifd0, ExifDir = ExifDirectoryKind.Exif;

    private static ExifValue Ascii(string text)
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes(text), 0];
        return ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes);
    }

    private static ExifMetadata Block(params (ExifDirectoryKind Directory, ushort Tag, ExifValue Value)[] tags) =>
        new(Endianness.LittleEndian, tags.Select(t => new ExifEntry(new ExifTagKey(t.Directory, t.Tag), t.Value)).ToArray());

    /// <summary>What an edit sets a tag to: the text of an ASCII value, else the bytes in hexadecimal; "removed" or "untouched".</summary>
    private static string Change(ExifEdit edit, ExifDirectoryKind directory, ushort tag)
    {
        foreach (KeyValuePair<ExifTagKey, ExifValue?> change in edit.Snapshot().Changes)
        {
            if (change.Key != new ExifTagKey(directory, tag)) continue;
            if (change.Value is null) return "removed";
            if (change.Value.FieldType == TiffFieldType.Ascii && change.Value.TryGetString(out string? text)) return text;
            byte[] bytes = new byte[change.Value.ByteLength];
            change.Value.CopyRawTo(bytes);
            return change.Value.FieldType + ":" + Convert.ToHexString(bytes);
        }
        return "untouched";
    }

    private static XmpPacket Xmp(Action<XmpEdit> change)
    {
        var edit = new XmpEdit();
        change(edit);
        return XmpPacket.CreateEmpty().Apply(edit)!;
    }

    private static IptcPacket Iptc(Action<IptcEdit> change)
    {
        var edit = new IptcEdit();
        change(edit);
        return IptcPacket.CreateEmpty().Apply(edit)!;
    }

    private static string[] Texts(XmpPacket packet, string path)
    {
        XmpEntry? entry = packet.Entries.FirstOrDefault(e => e.Key == path);
        if (entry is null) return [];
        return entry.Kind == XmpEntryKind.LangAlt ? entry.Languages.Select(l => l.Language + "=" + l.Value).ToArray() : entry.Values.ToArray();
    }

    private static string[] Texts(IptcPacket packet, byte dataset) =>
        Enumerable.Range(0, packet.Datasets.Count).Where(i => packet.Datasets[i].Key == new IptcDatasetKey(2, dataset)).Select(packet.GetText).ToArray();

    private static string Notes(SyncEdits edits) => string.Join("\n", edits.Notes.Select(n => $"{n.Source}: {n.Reason}"));

    // ---- Keys ----

    [Fact]
    public void KeysAreTheKeysExiv2Prints()
    {
        Assert.Equal("Exif.Image.Make", MetadataKeys.Format(new ExifTagKey(Ifd0, 0x010F)));
        Assert.Equal("Exif.Photo.DateTimeOriginal", MetadataKeys.Format(new ExifTagKey(ExifDir, 0x9003)));
        Assert.Equal("Exif.Photo.ISOSpeedRatings", MetadataKeys.Format(new ExifTagKey(ExifDir, 0x8827)));
        Assert.Equal("Exif.GPSInfo.GPSLatitude", MetadataKeys.Format(new ExifTagKey(ExifDirectoryKind.Gps, 2)));
        Assert.Equal("Exif.Iop.InteroperabilityIndex", MetadataKeys.Format(new ExifTagKey(ExifDirectoryKind.Interop, 1)));
        Assert.Equal("Exif.Thumbnail.Compression", MetadataKeys.Format(new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0103)));
        Assert.Equal("Exif.Photo.0xfffe", MetadataKeys.Format(new ExifTagKey(ExifDir, 0xFFFE)));
        Assert.Equal("Iptc.Application2.Keywords", MetadataKeys.Format(new IptcDatasetKey(2, 25)));
        Assert.Equal("Iptc.Envelope.CharacterSet", MetadataKeys.Format(new IptcDatasetKey(1, 90)));
        Assert.Equal("Iptc.Application2.0x00fa", MetadataKeys.Format(new IptcDatasetKey(2, 250)));
        Assert.Null(MetadataKeys.Format(new IptcDatasetKey(7, 10)));

        foreach (ExifDirectoryKind directory in Enum.GetValues<ExifDirectoryKind>())
        {
            foreach (ExifTagDefinition definition in ExifTags.Definitions(directory))
            {
                var key = new ExifTagKey(directory, definition.Id);
                Assert.True(MetadataKeys.TryParseExif(MetadataKeys.Format(key), out ExifTagKey back), MetadataKeys.Format(key));
                Assert.Equal(key, back);
            }
        }
        Assert.True(MetadataKeys.TryParseExif("Exif.Photo.PhotographicSensitivity", out ExifTagKey iso));
        Assert.Equal(new ExifTagKey(ExifDir, 0x8827), iso);
        Assert.True(MetadataKeys.TryParseExif("Exif.Image.0x9c9b", out ExifTagKey numbered));
        Assert.Equal(new ExifTagKey(Ifd0, 0x9C9B), numbered);
        Assert.True(MetadataKeys.TryParseIptc("Iptc.Application2.Byline", out IptcDatasetKey byline));
        Assert.Equal(new IptcDatasetKey(2, 80), byline);
        Assert.True(MetadataKeys.TryParseIptc("Iptc.Envelope.0x005a", out IptcDatasetKey set));
        Assert.Equal(new IptcDatasetKey(1, 90), set);

        foreach (string wrong in new[] { "", "Exif", "Exif.Photo", "Exif.Photo.", "Exif.Canon.ModelID", "exif.photo.FNumber", "Exif.Photo.fnumber", "Exif.Photo.0x12", "Exif.Photo.FNumber.x", "Xmp.dc.title" })
            Assert.False(MetadataKeys.TryParseExif(wrong, out _), wrong);
        foreach (string wrong in new[] { "", "Iptc.Application2", "Iptc.Record7.X", "Iptc.Application2.keywords", "Iptc.Application2.0x0100", "Exif.Image.Make" })
            Assert.False(MetadataKeys.TryParseIptc(wrong, out _), wrong);
        Assert.False(MetadataKeys.TryParseExif(null, out _));
        Assert.False(MetadataKeys.TryParseIptc(null, out _));
    }

    // ---- Timestamps ----

    [Fact]
    public void AShiftMovesTheThreeExifTimestampsAndLeavesTheirCompanions()
    {
        ExifMetadata exif = Block((Ifd0, 0x0132, Ascii("2026:11:12 09:05:07")), (ExifDir, 0x9003, Ascii("2026:12:31 23:30:00")),
            (ExifDir, 0x9004, Ascii("2024:02:28 12:00:00")), (ExifDir, 0x9291, Ascii("25")), (ExifDir, 0x9011, Ascii("+02:00")));
        SyncEdits edits = MetadataTimestamps.Adjust(exif, null, null, new TimestampShift(TimeSpan.FromMinutes(45)));
        Assert.Equal(string.Empty, Notes(edits));
        Assert.Equal("2026:11:12 09:50:07", Change(edits.Exif, Ifd0, 0x0132));
        Assert.Equal("2027:01:01 00:15:00", Change(edits.Exif, ExifDir, 0x9003));
        Assert.Equal("2024:02:28 12:45:00", Change(edits.Exif, ExifDir, 0x9004));
        Assert.Equal("untouched", Change(edits.Exif, ExifDir, 0x9291));
        Assert.Equal("untouched", Change(edits.Exif, ExifDir, 0x9011));
        Assert.True(edits.Xmp.IsEmpty);
        Assert.True(edits.Iptc.IsEmpty);

        SyncEdits back = MetadataTimestamps.Adjust(exif, null, null, new TimestampShift(0, 0, -1, -new TimeSpan(10, 0, 0)));
        Assert.Equal("2026:11:10 23:05:07", Change(back.Exif, Ifd0, 0x0132));
        Assert.Equal("2024:02:27 02:00:00", Change(back.Exif, ExifDir, 0x9004));

        Assert.True(MetadataTimestamps.Adjust(exif, null, null, default).IsEmpty);
        Assert.Throws<ArgumentException>(() => MetadataTimestamps.Adjust(exif, null, null, new TimestampShift(TimeSpan.FromMilliseconds(1500))));
    }

    [Fact]
    public void YearsAndMonthsAreCountedOnTheCalendarAndADayTheMonthLacksRunsOver()
    {
        ExifMetadata exif = Block((Ifd0, 0x0132, Ascii("2026:01:31 10:00:00")), (ExifDir, 0x9003, Ascii("2024:02:29 10:00:00")),
            (ExifDir, 0x9004, Ascii("2026:11:15 10:00:00")));
        SyncEdits month = MetadataTimestamps.Adjust(exif, null, null, new TimestampShift(0, 1, 0, TimeSpan.Zero));
        Assert.Equal("2026:03:03 10:00:00", Change(month.Exif, Ifd0, 0x0132));
        Assert.Equal("2024:03:29 10:00:00", Change(month.Exif, ExifDir, 0x9003));
        Assert.Equal("2026:12:15 10:00:00", Change(month.Exif, ExifDir, 0x9004));

        SyncEdits year = MetadataTimestamps.Adjust(exif, null, null, new TimestampShift(1, -2, 0, TimeSpan.Zero));
        Assert.Equal("2026:12:01 10:00:00", Change(year.Exif, Ifd0, 0x0132));
        Assert.Equal("2024:12:29 10:00:00", Change(year.Exif, ExifDir, 0x9003));
        Assert.Equal("2027:09:15 10:00:00", Change(year.Exif, ExifDir, 0x9004));

        // What is not a timestamp, and what would leave the calendar, is named and not written.
        ExifMetadata odd = Block((Ifd0, 0x0132, Ascii("                   ")), (ExifDir, 0x9003, Ascii("9999:12:31 23:59:59")), (ExifDir, 0x9004, Ascii("2026:11:15 10:00:00")));
        SyncEdits edits = MetadataTimestamps.Adjust(odd, null, null, new TimestampShift(TimeSpan.FromSeconds(1)));
        Assert.Equal("Exif.Image.DateTime: The value is not a timestamp.\nExif.Photo.DateTimeOriginal: The shifted timestamp is outside the years 1 to 9999.", Notes(edits));
        Assert.Equal("untouched", Change(edits.Exif, Ifd0, 0x0132));
        Assert.Equal("untouched", Change(edits.Exif, ExifDir, 0x9003));
        Assert.Equal("2026:11:15 10:00:01", Change(edits.Exif, ExifDir, 0x9004));
    }

    [Fact]
    public void XmpAndIptcMoveTheirOwnValuesAndKeepTheirForm()
    {
        XmpPacket xmp = Xmp(e =>
        {
            e.Set("Xmp.xmp.CreateDate", "2026-11-12T23:30:00.250+02:00");
            e.Set("Xmp.photoshop.DateCreated", "2026-11-12");
            e.Set("Xmp.xmp.ModifyDate", "2026-11");
            e.Set("Xmp.exif.DateTimeOriginal", "2026-11-12T23:30Z");
            e.Set("Xmp.xmp.MetadataDate", "2026-11-12T23:30:00Z");
            e.Set("Xmp.tiff.DateTime", "yesterday");
        });
        IptcPacket iptc = Iptc(e =>
        {
            e.Set(new IptcDatasetKey(2, 55), "20261112"); e.Set(new IptcDatasetKey(2, 60), "233000-0500");
            e.Set(new IptcDatasetKey(2, 62), "20261112");
        });
        SyncEdits edits = MetadataTimestamps.Adjust(null, iptc, xmp, new TimestampShift(0, 0, 1, TimeSpan.FromHours(1)));
        Assert.Equal("Xmp.tiff.DateTime: The value is not a timestamp.", Notes(edits));
        Assert.True(edits.Exif.IsEmpty);

        XmpPacket moved = xmp.Apply(edits.Xmp)!;
        Assert.Equal(new[] { "2026-11-14T00:30:00.250+02:00" }, Texts(moved, "Xmp.xmp.CreateDate"));
        Assert.Equal(new[] { "2026-11-13" }, Texts(moved, "Xmp.photoshop.DateCreated"));
        Assert.Equal(new[] { "2026-11" }, Texts(moved, "Xmp.xmp.ModifyDate"));
        Assert.Equal(new[] { "2026-11-14T00:30:00Z" }, Texts(moved, "Xmp.exif.DateTimeOriginal"));
        Assert.Equal(new[] { "2026-11-12T23:30:00Z" }, Texts(moved, "Xmp.xmp.MetadataDate"));
        Assert.Equal(new[] { "yesterday" }, Texts(moved, "Xmp.tiff.DateTime"));

        IptcPacket shifted = iptc.Apply(edits.Iptc)!;
        Assert.Equal(new[] { "20261114" }, Texts(shifted, 55));
        Assert.Equal(new[] { "003000-0500" }, Texts(shifted, 60));
        Assert.Equal(new[] { "20261113" }, Texts(shifted, 62));
        Assert.Empty(Texts(shifted, 63));
    }

    // ---- File names ----

    [Fact]
    public void AFileNameIsTheFormatWithItsTokensAndTheExtensionOfTheFile()
    {
        var taken = new DateTime(2026, 11, 2, 9, 5, 7);   // a Monday
        Assert.Equal("20261102_090507.jpg", MetadataFileNames.Format(MetadataFileNames.DefaultFormat, taken, "/photos/trip/IMG_0001.jpg"));
        Assert.Equal("20261102_090507.JPG", MetadataFileNames.Format("%Y%m%d_%H%M%S", taken, @"C:\photos\IMG_0001.JPG"));
        Assert.Equal("2026-11-02 09.05.07 IMG_0001.jpg", MetadataFileNames.Format("%F %H.%M.%S :basename:", taken, "IMG_0001.jpg"));
        Assert.Equal("trip-photos-IMG_0001-26.jpg", MetadataFileNames.Format(":dirname:-:parentname:-:basename:-%y", taken, "/photos/trip/IMG_0001.jpg"));
        Assert.Equal("x.edit.v2.tif", MetadataFileNames.Format("x:basesuffix:", taken, "scan.edit.v2.tif"));
        Assert.Equal("x.tif", MetadataFileNames.Format("x:basesuffix:", taken, "scan.tif"));
        Assert.Equal("--a", MetadataFileNames.Format(":dirname:-:parentname:-a", taken, "noextension"));
        Assert.Equal("a", MetadataFileNames.Format("a", taken, ".hidden"));
        Assert.Equal("20 26 11 02  2 306 09 09 05 07 AM November Nov Nov Monday Mon 1 1 09:05 100%.jpg",
            MetadataFileNames.Format("%C %y %m %d %e %j %H %I %M %S %p %B %b %h %A %a %u %w %R 100%%", taken, "a.jpg"));
        Assert.Equal("12 PM 7 0 09.jpg", MetadataFileNames.Format("%I %p %u %w %S", new DateTime(2026, 11, 1, 12, 0, 9), "a.jpg"));
        Assert.Equal("12 AM.jpg", MetadataFileNames.Format("%I %p", new DateTime(2026, 11, 1, 0, 0, 0), "a.jpg"));
        Assert.Equal("09:05:07.jpg", MetadataFileNames.Format("%T", taken, "a.jpg"));

        foreach (string wrong in new[] { "%", "name%", "%Z", "%c", "%x", "%E", "%D", "", ":dirname:", "a/b", @"a\b" })
            Assert.Throws<ArgumentException>(() => MetadataFileNames.Format(wrong, taken, "a.jpg"));
    }

    // ---- Modify scripts ----

    [Fact]
    public void AScriptBecomesEditsOfTheThreeFamilies()
    {
        ModifyScript script = ModifyScript.Parse("""
            # A comment, then an empty line.

            set Exif.Image.Artist Ana Li
            set Exif.Photo.ISOSpeedRatings 200
            set Exif.Photo.ExposureTime 1/250
            set Exif.Image.XResolution Rational 300/1
            add Exif.Photo.SubjectArea Short 10 20 30
            set Exif.Photo.UserComment charset=Ascii A comment
            set Exif.Image.0x9c9b Byte 65 0 66 0
            set Exif.GPSInfo.GPSAltitudeRef Byte 1
            set Exif.Photo.ExposureBiasValue -2/3
            del Exif.Image.Software
            set Iptc.Application2.Headline "  Quoted, with its spaces  "
            add Iptc.Application2.Keywords sea
            add Iptc.Application2.Keywords String sky
            set Iptc.Application2.DateCreated 2026-11-12
            set Iptc.Application2.TimeCreated Time 09:05:07+02:00
            set Iptc.Application2.Urgency 5
            del Iptc.Application2.Caption
            reg shop http://example.com/shop/1.0/
            set Xmp.shop.price 12
            set Xmp.dc.subject sea
            add Xmp.dc.subject XmpBag sky
            set Xmp.dc.title lang=de Ein Titel
            set Xmp.dc.title A title
            set Xmp.dc.description 'two\nlines \u00e9'
            del Xmp.xmp.Rating
            """);
        Assert.Equal(25, script.Commands.Count);
        Assert.False(script.IsEmpty);
        Assert.Equal(new ModifyCommand(3, ModifyCommandKind.Set, "Exif.Image.Artist", null, "Ana Li"), script.Commands[0]);
        Assert.Equal(new ModifyCommand(6, ModifyCommandKind.Set, "Exif.Image.XResolution", "Rational", "300/1"), script.Commands[3]);
        Assert.Equal(new ModifyCommand(20, ModifyCommandKind.Register, "shop", null, "http://example.com/shop/1.0/"), script.Commands[17]);

        Assert.Equal("Ana Li", Change(script.Exif, Ifd0, 0x013B));
        Assert.Equal("Short:C800", Change(script.Exif, ExifDir, 0x8827));
        Assert.Equal("Rational:01000000FA000000", Change(script.Exif, ExifDir, 0x829A));
        Assert.Equal("Rational:2C01000001000000", Change(script.Exif, Ifd0, 0x011A));
        Assert.Equal("Short:0A0014001E00", Change(script.Exif, ExifDir, 0x9214));
        Assert.Equal("Undefined:" + Convert.ToHexString("ASCII\0\0\0A comment"u8), Change(script.Exif, ExifDir, 0x9286));
        Assert.Equal("Byte:41004200", Change(script.Exif, Ifd0, 0x9C9B));
        Assert.Equal("Byte:01", Change(script.Exif, ExifDirectoryKind.Gps, 5));
        Assert.Equal("SRational:FEFFFFFF03000000", Change(script.Exif, ExifDir, 0x9204));
        Assert.Equal("removed", Change(script.Exif, Ifd0, 0x0131));

        // set replaces the first occurrence and leaves the others, as an Exiv2 command file does.
        IptcPacket several = Iptc(e => e.SetKeywords(new[] { "one", "two", "three" })).Apply(ModifyScript.Parse("set Iptc.Application2.Keywords first\nadd Iptc.Application2.Headline H").Iptc)!;
        Assert.Equal(new[] { "first", "two", "three" }, Texts(several, 25));
        Assert.Equal(new[] { "H" }, Texts(several, 105));

        IptcPacket iptc = Iptc(e => { e.SetCaption("old"); e.AddKeyword("land"); }).Apply(script.Iptc)!;
        Assert.Equal(new[] { "  Quoted, with its spaces  " }, Texts(iptc, 105));
        Assert.Equal(new[] { "land", "sea", "sky" }, Texts(iptc, 25));
        Assert.Equal(new[] { "20261112" }, Texts(iptc, 55));
        Assert.Equal(new[] { "090507+0200" }, Texts(iptc, 60));
        Assert.Empty(Texts(iptc, 120));
        Assert.Contains(iptc.Datasets, d => d.Key == new IptcDatasetKey(2, 10));

        XmpPacket xmp = Xmp(e => e.Set("Xmp.xmp.Rating", "3")).Apply(script.Xmp)!;
        Assert.Equal(new[] { "12" }, Texts(xmp, "Xmp.shop.price"));
        Assert.Equal(new[] { "sea", "sky" }, Texts(xmp, "Xmp.dc.subject"));
        Assert.Equal(XmpEntryKind.Bag, xmp.Entries.First(e => e.Key == "Xmp.dc.subject").Kind);
        Assert.Equal(new[] { "de=Ein Titel", "x-default=A title" }, Texts(xmp, "Xmp.dc.title").Order().ToArray());
        Assert.Equal(new[] { "x-default=two\nlines \u00e9" }, Texts(xmp, "Xmp.dc.description"));
        Assert.Empty(Texts(xmp, "Xmp.xmp.Rating"));

        Assert.True(ModifyScript.Parse("# nothing\r\n\r\n   \t").IsEmpty);
        // A Unicode comment takes the byte order of the block it is for.
        Assert.Equal("Undefined:" + Convert.ToHexString("UNICODE\0"u8) + "00680069",
            Change(ModifyScript.Parse("set Exif.Photo.UserComment charset=Unicode hi", Endianness.BigEndian).Exif, ExifDir, 0x9286));
        Assert.Equal("Undefined:" + Convert.ToHexString("\0\0\0\0\0\0\0\0hi"u8),
            Change(ModifyScript.Parse(new[] { "set Exif.Photo.UserComment Comment hi" }).Exif, ExifDir, 0x9286));
    }

    [Theory]
    [InlineData("set Exif.Image.Artist A\nfoo Exif.Image.Artist B", 2, "Invalid command `foo'.")]
    [InlineData("set", 1, "The command has no key.")]
    [InlineData("\n\nset Exif.Canon.ModelID 1", 3, "Invalid key `Exif.Canon.ModelID'.")]
    [InlineData("set Exif.Photo.FNumber Rational", 1, null)]
    [InlineData("set Exif.Photo.FNumber Rational abc", 1, null)]
    [InlineData("set Exif.Photo.ISOSpeedRatings 70000", 1, null)]
    [InlineData("set Exif.Photo.FNumber XmpBag 1", 1, "`XmpBag' is not a type of an EXIF value.")]
    [InlineData("set Exif.Image.Artist Jos\\u00e9", 1, "An Ascii value holds seven-bit characters and no NUL.")]
    [InlineData("set Exif.Image.ExifTag 8", 1, null)]
    [InlineData("set Exif.Photo.UserComment charset=Latin1 x", 1, "The character code `Latin1' is not Ascii, Unicode or Undefined.")]
    [InlineData("set Iptc.Application2.DateCreated tomorrow", 1, "A Date is YYYY-MM-DD.")]
    [InlineData("set Iptc.Application2.TimeCreated noon", 1, "A Time is HH:MM:SS with an optional +HH:MM or -HH:MM.")]
    [InlineData("add Iptc.Application2.RecordVersion 5", 1, "A Short dataset is set, not added: it does not repeat.")]
    [InlineData("set Iptc.Application2.Headline Short 5x", 1, null)]
    [InlineData("reg shop", 1, "Empty value for key `shop'.")]
    [InlineData("reg rdf http://example.com/", 1, null)]
    [InlineData("set Xmp.nosuchprefix.name value\n", 1, "Invalid key `Xmp.nosuchprefix.name'.")]
    [InlineData("reg shop http://example.com/shop/\nset Xmp.shop value", 2, "Invalid key `Xmp.shop'.")]
    [InlineData("set Xmp.dc.title Short 5", 1, "`Short' is not a type of an XMP value.")]
    [InlineData("set Xmp.dc.title lang=?? x", 1, null)]
    public void ALineThatCannotBeCarriedOutIsRefusedWithItsNumber(string script, int line, string? reason)
    {
        ModifyScriptException refusal = Assert.Throws<ModifyScriptException>(() => ModifyScript.Parse(script));
        Assert.Equal(line, refusal.Line);
        Assert.NotEqual(string.Empty, refusal.Reason);
        if (reason is not null) Assert.Equal(reason, refusal.Reason);
        Assert.Equal($"Line {line}: {refusal.Reason}", refusal.Message);
    }
}
