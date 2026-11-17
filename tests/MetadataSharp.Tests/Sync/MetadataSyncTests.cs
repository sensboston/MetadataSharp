using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using Xunit;

namespace MetadataSharp.Tests.Sync;

/// <summary>Conversion between EXIF, IPTC and XMP: every mapped field both ways, what is lost, and the MWG reconciliation.</summary>
public sealed class MetadataSyncTests
{
    private const ExifDirectoryKind Ifd0 = ExifDirectoryKind.Ifd0, ExifDir = ExifDirectoryKind.Exif, Gps = ExifDirectoryKind.Gps;
    private const string Dc = "http://purl.org/dc/elements/1.1/", Photoshop = "http://ns.adobe.com/photoshop/1.0/",
        Tiff = "http://ns.adobe.com/tiff/1.0/", ExifNs = "http://ns.adobe.com/exif/1.0/", XmpNs = "http://ns.adobe.com/xap/1.0/";

    // ---- Helpers ----

    private static ExifValue Ascii(string text)
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes(text), 0];
        return ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes);
    }

    private static ExifValue Bytes(TiffFieldType type, params byte[] bytes) => ExifValue.FromRaw(type, (uint)bytes.Length, Endianness.LittleEndian, bytes);

    private static ExifValue Numbers(TiffFieldType type, int width, int count, IEnumerable<long> values)
    {
        byte[] bytes = values.SelectMany(value => Enumerable.Range(0, width).Select(b => (byte)(value >> (8 * b)))).ToArray();
        return ExifValue.FromRaw(type, (uint)count, Endianness.LittleEndian, bytes);
    }

    private static ExifValue Shorts(params ushort[] values) => Numbers(TiffFieldType.Short, 2, values.Length, values.Select(v => (long)v));
    private static ExifValue Longs(params uint[] values) => Numbers(TiffFieldType.Long, 4, values.Length, values.Select(v => (long)v));
    /// <summary>Rationals from numerator, denominator pairs.</summary>
    private static ExifValue Rationals(TiffFieldType type, params int[] pairs) => Numbers(type, 4, pairs.Length / 2, pairs.Select(v => (long)v));

    private static ExifMetadata Block(params (ExifDirectoryKind Directory, ushort Tag, ExifValue Value)[] tags) =>
        new(Endianness.LittleEndian, tags.Select(t => new ExifEntry(new ExifTagKey(t.Directory, t.Tag), t.Value)).ToArray());

    /// <summary>The block after an edit, as a writer would leave it.</summary>
    private static ExifMetadata Apply(ExifMetadata? source, ExifEdit edit)
    {
        var entries = source?.Entries.ToDictionary(e => e.Key, e => e.Value) ?? new Dictionary<ExifTagKey, ExifValue>();
        foreach (KeyValuePair<ExifTagKey, ExifValue?> change in edit.Snapshot().Changes)
        {
            if (change.Value is null) entries.Remove(change.Key);
            else entries[change.Key] = change.Value;
        }
        return new(Endianness.LittleEndian, entries.Select(p => new ExifEntry(p.Key, p.Value)).ToArray());
    }

    private static XmpPacket Apply(XmpPacket? source, XmpEdit edit) => (source ?? XmpPacket.CreateEmpty()).Apply(edit)!;
    private static IptcPacket Apply(IptcPacket? source, IptcEdit edit) => (source ?? IptcPacket.CreateEmpty()).Apply(edit)!;

    private static XmpPacket Xmp(Action<XmpEdit> change)
    {
        var edit = new XmpEdit();
        change(edit);
        return Apply((XmpPacket?)null, edit);
    }

    private static IptcPacket Iptc(Action<IptcEdit> change)
    {
        var edit = new IptcEdit();
        change(edit);
        return Apply((IptcPacket?)null, edit);
    }

    private static XmpEntry? Entry(XmpPacket packet, string uri, string path) =>
        packet.Entries.FirstOrDefault(e => e.NamespaceUri == uri && e.Path == path);

    /// <summary>The texts of a top-level property: its value, its items, or its default language text.</summary>
    private static string[] Texts(XmpPacket packet, string uri, string name)
    {
        XmpEntry? entry = Entry(packet, uri, name);
        if (entry is null) return [];
        return entry.Kind == XmpEntryKind.LangAlt ? [entry.Languages.First(l => l.Language == "x-default").Value] : entry.Values.ToArray();
    }

    private static string[] Texts(IptcPacket packet, byte dataset) =>
        Enumerable.Range(0, packet.Datasets.Count).Where(i => packet.Datasets[i].Key == new IptcDatasetKey(2, dataset)).Select(packet.GetText).ToArray();

    private static byte[] Raw(ExifMetadata exif, ExifTagKey key)
    {
        Assert.True(exif.TryGetEntry(key, out ExifEntry? entry), $"missing {key}");
        byte[] bytes = new byte[entry!.Value.ByteLength];
        entry.Value.CopyRawTo(bytes);
        return bytes;
    }

    private static string? Text(ExifMetadata exif, ExifDirectoryKind directory, ushort tag) =>
        exif.TryGetEntry(new(directory, tag), out ExifEntry? entry) && entry.Value.TryGetString(out string? text) ? text : null;

    private static string Notes(SyncEdits edits) => string.Join("\n", edits.Notes.Select(n => $"{n.Source} -> {n.Target}: {n.Reason}"));

    // ---- The tables ----

    [Fact]
    public void TheTablesNameEveryFieldOnceAndTheirPathsAreOnesAnEditTakes()
    {
        Assert.Equal(MetadataSync.ExifMappings.Count, MetadataSync.ExifMappings.Select(m => m.Tag).Distinct().Count());
        Assert.Equal(MetadataSync.ExifMappings.Count, MetadataSync.ExifMappings.Select(m => m.XmpPath).Distinct().Count());
        Assert.Equal(MetadataSync.IptcMappings.Count, MetadataSync.IptcMappings.Select(m => m.Dataset).Distinct().Count());
        Assert.Equal(MetadataSync.IptcMappings.Count, MetadataSync.IptcMappings.Select(m => m.XmpPath).Distinct().Count());
        Assert.Equal(100, MetadataSync.ExifMappings.Count);
        Assert.Equal(22, MetadataSync.IptcMappings.Count);
        var edit = new XmpEdit();
        foreach (ExifXmpMapping mapping in MetadataSync.ExifMappings) edit.RemovePath(mapping.XmpPath);
        foreach (IptcXmpMapping mapping in MetadataSync.IptcMappings)
        {
            edit.RemovePath(mapping.XmpPath);
            Assert.True(IptcDatasets.TryGet(mapping.Dataset, out _));
            Assert.Equal(mapping.TimeDataset is not null, mapping.Dataset.Dataset is 55 or 62);
        }
        Assert.Equal(new XmpPropertyName(Tiff, "Make"), MetadataSync.ExifMappings.Single(m => m.XmpPath == "Xmp.tiff.Make").Property);
        Assert.Equal(new XmpPropertyName(Dc, "subject"), MetadataSync.IptcMappings.Single(m => m.Dataset.Dataset == 25).Property);
    }

    // ---- EXIF and XMP ----

    /// <summary>A value of the kind and count the tag is defined with.</summary>
    private static ExifValue Sample(ExifXmpMapping mapping)
    {
        ushort tag = mapping.Tag.TagId;
        bool gps = mapping.Tag.Directory == Gps;
        switch (mapping.Conversion)
        {
            case SyncConversion.Text:
                return Ascii(gps ? "K" : tag == 0xA004 ? "ABCD0001.WAV" : tag == 0xA420 ? "0123456789abcdef0123456789abcdef" : "Sample " + tag);
            case SyncConversion.TextList:
                return Ascii("Ana; Li Wei");
            case SyncConversion.Integer:
            {
                int count = !gps && tag is 0x0102 ? 3 : !gps && tag is 0x0212 or 0x9214 or 0xA214 ? 2 : !gps && tag == 0x012D ? 768 : !gps && tag == 0x9101 ? 4 : 1;
                long[] values = Enumerable.Range(0, count).Select(i => (long)((i % 3) + 1)).ToArray();
                return mapping.FieldType switch
                {
                    TiffFieldType.Long => Longs(values.Select(v => (uint)v).ToArray()),
                    TiffFieldType.Short => Shorts(values.Select(v => (ushort)v).ToArray()),
                    _ => Bytes(mapping.FieldType, values.Select(v => (byte)v).ToArray()),
                };
            }
            case SyncConversion.Rational:
            {
                int count = gps ? 1 : tag switch { 0x013E => 2, 0x013F or 0x0214 => 6, 0x0211 => 3, _ => 1 };
                int sign = mapping.FieldType == TiffFieldType.SRational ? -1 : 1;
                return Rationals(mapping.FieldType, Enumerable.Range(0, count).SelectMany(i => new[] { sign * (7 + i), 2 }).ToArray());
            }
            case SyncConversion.Date:
                return Ascii("2026:11:12 09:05:07");
            case SyncConversion.GpsTime:
                return Rationals(TiffFieldType.Rational, 9, 1, 5, 1, 725, 100);
            case SyncConversion.GpsCoordinate:
                return Rationals(TiffFieldType.Rational, 51, 1, 30, 1, 12300000, 1000000);
            case SyncConversion.Version:
                return Bytes(TiffFieldType.Undefined, "0232"u8.ToArray());
            case SyncConversion.GpsVersion:
                return Bytes(TiffFieldType.Byte, 2, 3, 0, 0);
            case SyncConversion.Flash:
                return Shorts(0x59);
            default:
                return Bytes(TiffFieldType.Undefined, "ASCII\0\0\0A comment"u8.ToArray());
        }
    }

    private static ExifMetadata EveryMappedTag()
    {
        var tags = MetadataSync.ExifMappings.Select(m => (m.Tag.Directory, m.Tag.TagId, Sample(m))).ToList();
        // The companion tags a value is read with.
        foreach (ushort tag in new ushort[] { 0x9290, 0x9291, 0x9292 }) tags.Add((ExifDir, tag, Ascii("25")));
        foreach (ushort tag in new ushort[] { 0x9010, 0x9011, 0x9012 }) tags.Add((ExifDir, tag, Ascii("+02:00")));
        tags.Add((Gps, 0x0001, Ascii("N"))); tags.Add((Gps, 0x0003, Ascii("E")));
        tags.Add((Gps, 0x0013, Ascii("S"))); tags.Add((Gps, 0x0015, Ascii("W")));
        tags.Add((Gps, 0x001D, Ascii("2026:11:12")));
        return Block(tags.ToArray());
    }

    [Fact]
    public void EveryMappedExifTagIsWrittenToXmpAndComesBackAsTheSameBytes()
    {
        ExifMetadata exif = EveryMappedTag();

        SyncEdits toXmp = MetadataSync.ExifToXmp(exif);
        Assert.Equal(string.Empty, Notes(toXmp));
        Assert.True(toXmp.Exif.IsEmpty);
        Assert.True(toXmp.Iptc.IsEmpty);
        XmpPacket xmp = Apply((XmpPacket?)null, toXmp.Xmp);
        foreach (ExifXmpMapping mapping in MetadataSync.ExifMappings)
            Assert.True(Entry(xmp, mapping.Property.NamespaceUri, mapping.Property.LocalName) is not null, mapping.XmpPath);

        SyncEdits toExif = MetadataSync.XmpToExif(xmp);
        Assert.Equal(string.Empty, Notes(toExif));
        Assert.True(toExif.Xmp.IsEmpty);
        ExifMetadata back = Apply(null, toExif.Exif);
        Assert.Equal(exif.Entries.Count, back.Entries.Count);
        foreach (ExifEntry entry in exif.Entries)
        {
            Assert.True(back.TryGetEntry(entry.Key, out ExifEntry? other), $"missing {entry.Key}");
            Assert.True(entry.Value.FieldType == other!.Value.FieldType, $"type of {entry.Key}");
            Assert.True(Raw(exif, entry.Key).AsSpan().SequenceEqual(Raw(back, entry.Key)), $"bytes of {entry.Key}");
        }
    }

    [Fact]
    public void ExifValuesTakeTheFormsXmpDefines()
    {
        XmpPacket xmp = Apply((XmpPacket?)null, MetadataSync.ExifToXmp(EveryMappedTag()).Xmp);

        Assert.Equal(new[] { "Sample 271" }, Texts(xmp, Tiff, "Make"));
        Assert.Equal(XmpEntryKind.Seq, Entry(xmp, Dc, "creator")!.Kind);
        Assert.Equal(new[] { "Ana", "Li Wei" }, Texts(xmp, Dc, "creator"));
        Assert.Equal(XmpEntryKind.LangAlt, Entry(xmp, Dc, "description")!.Kind);
        Assert.Equal(XmpEntryKind.LangAlt, Entry(xmp, Dc, "rights")!.Kind);
        Assert.Equal(new[] { "1", "2", "3" }, Texts(xmp, Tiff, "BitsPerSample"));
        Assert.Equal(XmpEntryKind.Seq, Entry(xmp, Tiff, "BitsPerSample")!.Kind);
        Assert.Equal(new[] { "1" }, Texts(xmp, Tiff, "Orientation"));
        Assert.Equal(new[] { "7/2" }, Texts(xmp, ExifNs, "ExposureTime"));
        Assert.Equal(new[] { "-7/2" }, Texts(xmp, ExifNs, "ExposureBiasValue"));
        Assert.Equal(new[] { "7/2", "8/2" }, Texts(xmp, Tiff, "WhitePoint"));
        Assert.Equal(new[] { "2026-11-12T09:05:07.25+02:00" }, Texts(xmp, Photoshop, "DateCreated"));
        Assert.Equal(new[] { "2026-11-12T09:05:07.25+02:00" }, Texts(xmp, XmpNs, "CreateDate"));
        Assert.Equal(new[] { "2026-11-12T09:05:07.25+02:00" }, Texts(xmp, XmpNs, "ModifyDate"));
        Assert.Equal(new[] { "2026-11-12T09:05:07.25Z" }, Texts(xmp, ExifNs, "GPSTimeStamp"));
        Assert.Equal(new[] { "51,30.2050000N" }, Texts(xmp, ExifNs, "GPSLatitude"));
        Assert.Equal(new[] { "51,30.2050000E" }, Texts(xmp, ExifNs, "GPSLongitude"));
        Assert.Equal(new[] { "51,30.2050000S" }, Texts(xmp, ExifNs, "GPSDestLatitude"));
        Assert.Equal(new[] { "0232" }, Texts(xmp, ExifNs, "ExifVersion"));
        Assert.Equal(new[] { "2.3.0.0" }, Texts(xmp, ExifNs, "GPSVersionID"));
        Assert.Equal(new[] { "1", "2", "3", "1" }, Texts(xmp, ExifNs, "ComponentsConfiguration"));
        Assert.Equal(new[] { "A comment" }, Texts(xmp, ExifNs, "UserComment"));
        Assert.Equal(XmpEntryKind.LangAlt, Entry(xmp, ExifNs, "UserComment")!.Kind);
        Assert.Equal(new[] { "A comment" }, Texts(xmp, ExifNs, "GPSProcessingMethod"));
        // 0x59: fired, no return information, mode 3 (auto), red-eye reduction.
        Assert.Equal(XmpEntryKind.Struct, Entry(xmp, ExifNs, "Flash")!.Kind);
        Assert.Equal(new[] { "True" }, Texts(xmp, ExifNs, "Flash/exif:Fired"));
        Assert.Equal(new[] { "0" }, Texts(xmp, ExifNs, "Flash/exif:Return"));
        Assert.Equal(new[] { "3" }, Texts(xmp, ExifNs, "Flash/exif:Mode"));
        Assert.Equal(new[] { "False" }, Texts(xmp, ExifNs, "Flash/exif:Function"));
        Assert.Equal(new[] { "True" }, Texts(xmp, ExifNs, "Flash/exif:RedEyeMode"));
    }

    [Fact]
    public void TextBeyondAsciiIsReadFromExifAndWrittenToItAsUtf8()
    {
        ExifMetadata exif = Block((Ifd0, 0x013B, Ascii("Ana Martín; 李伟")), (ExifDir, 0x9286, Bytes(TiffFieldType.Undefined,
            "UNICODE\0"u8.ToArray().Concat(Encoding.Unicode.GetBytes("Привет")).ToArray())));
        XmpPacket xmp = Apply((XmpPacket?)null, MetadataSync.ExifToXmp(exif).Xmp);
        Assert.Equal(new[] { "Ana Martín", "李伟" }, Texts(xmp, Dc, "creator"));
        Assert.Equal(new[] { "Привет" }, Texts(xmp, ExifNs, "UserComment"));

        ExifMetadata back = Apply(null, MetadataSync.XmpToExif(xmp).Exif);
        Assert.Equal(Encoding.UTF8.GetBytes("Ana Martín; 李伟\0"), Raw(back, new(Ifd0, 0x013B)));
        Assert.Equal(Raw(exif, new(ExifDir, 0x9286)), Raw(back, new(ExifDir, 0x9286)));
    }

    [Fact]
    public void AnXmpDateWritesTheExifDateWithItsCompanionsAndRemovesTheOnesItDoesNotName()
    {
        ExifMetadata exif = Block((ExifDir, 0x9003, Ascii("2020:01:01 00:00:00")), (ExifDir, 0x9291, Ascii("5")), (ExifDir, 0x9011, Ascii("+01:00")));

        XmpPacket plain = Xmp(e => e.Set("Xmp.photoshop.DateCreated", "2026-11-12T09:05"));
        ExifMetadata written = Apply(exif, MetadataSync.XmpToExif(plain, exif).Exif);
        Assert.Equal("2026:11:12 09:05:00", Text(written, ExifDir, 0x9003));
        Assert.Null(Text(written, ExifDir, 0x9291));
        Assert.Null(Text(written, ExifDir, 0x9011));

        XmpPacket full = Xmp(e => e.Set("Xmp.photoshop.DateCreated", "2026-11-12T09:05:07.250Z"));
        written = Apply(exif, MetadataSync.XmpToExif(full, exif).Exif);
        Assert.Equal("2026:11:12 09:05:07", Text(written, ExifDir, 0x9003));
        // The digits of the fraction are kept as they are written, both ways.
        Assert.Equal("250", Text(written, ExifDir, 0x9291));
        Assert.Equal("+00:00", Text(written, ExifDir, 0x9011));
        Assert.Equal(new[] { "2026-11-12T09:05:07.250+00:00" }, Texts(Apply((XmpPacket?)null, MetadataSync.ExifToXmp(written).Xmp), Photoshop, "DateCreated"));

        // EXIF holds a day and a time: a date without a time of day is not written, and says so.
        SyncEdits dayOnly = MetadataSync.XmpToExif(Xmp(e => e.Set("Xmp.photoshop.DateCreated", "2026-11-12")), exif);
        Assert.True(dayOnly.Exif.IsEmpty);
        SyncNote note = Assert.Single(dayOnly.Notes);
        Assert.Equal(("Xmp.photoshop.DateCreated", "Exif.Exif.0x9003"), (note.Source, note.Target));
    }

    [Fact]
    public void GpsValuesAreConvertedInBothNotationsAndATimeIsMovedToUtc()
    {
        XmpPacket xmp = Xmp(e =>
        {
            e.Set("Xmp.exif.GPSLatitude", "56,56,45.6N");
            e.Set("Xmp.exif.GPSLongitude", "24,6.5W");
            e.Set("Xmp.exif.GPSTimeStamp", "2026-11-12T01:05:07+02:00");
        });
        SyncEdits edits = MetadataSync.XmpToExif(xmp);
        Assert.Equal(string.Empty, Notes(edits));
        ExifMetadata exif = Apply(null, edits.Exif);
        Assert.Equal(Raw(Block((Gps, 2, Rationals(TiffFieldType.Rational, 56, 1, 56, 1, 45600000, 1000000))), new(Gps, 2)), Raw(exif, new(Gps, 2)));
        Assert.Equal("N", Text(exif, Gps, 1));
        Assert.Equal(Raw(Block((Gps, 4, Rationals(TiffFieldType.Rational, 24, 1, 6, 1, 30000000, 1000000))), new(Gps, 4)), Raw(exif, new(Gps, 4)));
        Assert.Equal("W", Text(exif, Gps, 3));
        Assert.Equal(Raw(Block((Gps, 7, Rationals(TiffFieldType.Rational, 23, 1, 5, 1, 7, 1))), new(Gps, 7)), Raw(exif, new(Gps, 7)));
        Assert.Equal("2026:11:11", Text(exif, Gps, 0x1D));

        // The seven decimals of a minute are kept to the millionth of a second.
        ExifMetadata fine = Apply(null, MetadataSync.XmpToExif(Xmp(e => e.Set("Xmp.exif.GPSLatitude", "37,47.1632683N"))).Exif);
        Assert.Equal(Raw(Block((Gps, 2, Rationals(TiffFieldType.Rational, 37, 1, 47, 1, 9796098, 1000000))), new(Gps, 2)), Raw(fine, new(Gps, 2)));

        // A coordinate without its reference letter, and a time without its date, are not values.
        SyncEdits lonely = MetadataSync.ExifToXmp(Block((Gps, 2, Rationals(TiffFieldType.Rational, 1, 1, 2, 1, 3, 1)), (Gps, 7, Rationals(TiffFieldType.Rational, 1, 1, 2, 1, 3, 1))));
        Assert.True(lonely.Xmp.IsEmpty);
        Assert.Equal(2, lonely.Notes.Count);

        SyncEdits wrong = MetadataSync.XmpToExif(Xmp(e => { e.Set("Xmp.exif.GPSLatitude", "56,56.5E"); e.Set("Xmp.exif.GPSLongitude", "181,0.0E"); }));
        Assert.True(wrong.Exif.IsEmpty);
        Assert.Equal(2, wrong.Notes.Count);
    }

    [Fact]
    public void ATagThatIsShortInTheFileStaysShortAndAValueTheTagCannotHoldIsNotWritten()
    {
        ExifMetadata exif = Block((Ifd0, 0x0100, Shorts(640)));
        XmpPacket xmp = Xmp(e => { e.Set("Xmp.tiff.ImageWidth", "800"); e.Set("Xmp.tiff.ImageLength", "600"); e.Set("Xmp.tiff.Orientation", "seven"); e.Set("Xmp.exif.FNumber", "28/10"); });
        SyncEdits edits = MetadataSync.XmpToExif(xmp, exif);
        ExifMetadata written = Apply(exif, edits.Exif);
        Assert.Equal(TiffFieldType.Short, written.Entries.Single(e => e.Key == new ExifTagKey(Ifd0, 0x0100)).Value.FieldType);
        Assert.Equal(TiffFieldType.Long, written.Entries.Single(e => e.Key == new ExifTagKey(Ifd0, 0x0101)).Value.FieldType);
        Assert.True(written.Entries.Single(e => e.Key == new ExifTagKey(ExifDir, 0x829D)).Value.TryGetRational(out UnsignedRational f));
        Assert.Equal(new UnsignedRational(28, 10), f);
        SyncNote note = Assert.Single(edits.Notes);
        Assert.Equal(("Xmp.tiff.Orientation", "Exif.Ifd0.0x0112"), (note.Source, note.Target));
    }

    [Fact]
    public void FillingLeavesWhatTheTargetHoldsAndErasingRemovesWhatWasConverted()
    {
        ExifMetadata exif = Block((Ifd0, 0x010F, Ascii("Maker")), (Ifd0, 0x0110, Ascii("Model")), (ExifDir, 0x9003, Ascii("2026:11:12 09:05:07")),
            (ExifDir, 0x9291, Ascii("5")), (Gps, 2, Rationals(TiffFieldType.Rational, 1, 1, 2, 1, 3, 1)), (Gps, 1, Ascii("N")),
            (ExifDir, 0x829A, Shorts(1)));
        XmpPacket xmp = Xmp(e => e.Set("Xmp.tiff.Make", "Other"));

        XmpPacket filled = Apply(xmp, MetadataSync.ExifToXmp(exif, xmp, new SyncOptions { Mode = SyncMode.FillMissing }).Xmp);
        Assert.Equal(new[] { "Other" }, Texts(filled, Tiff, "Make"));
        Assert.Equal(new[] { "Model" }, Texts(filled, Tiff, "Model"));
        XmpPacket overwritten = Apply(xmp, MetadataSync.ExifToXmp(exif, xmp).Xmp);
        Assert.Equal(new[] { "Maker" }, Texts(overwritten, Tiff, "Make"));

        // ExposureTime held as SHORT is not a value of the tag: it is not converted, so it is not erased.
        SyncEdits erasing = MetadataSync.ExifToXmp(exif, xmp, new SyncOptions { EraseSource = true });
        ExifMetadata left = Apply(exif, erasing.Exif);
        Assert.Equal(new ExifTagKey(ExifDir, 0x829A), Assert.Single(left.Entries).Key);
        Assert.Single(erasing.Notes);

        // The other way: filling skips the tag EXIF has, erasing removes the properties that were written.
        XmpPacket source = Xmp(e => { e.Set("Xmp.tiff.Make", "From XMP"); e.Set("Xmp.tiff.Software", "Tool"); e.Set("Xmp.xmp.Label", "kept"); });
        SyncEdits back = MetadataSync.XmpToExif(source, exif, new SyncOptions { Mode = SyncMode.FillMissing, EraseSource = true });
        ExifMetadata merged = Apply(exif, back.Exif);
        Assert.Equal("Maker", Text(merged, Ifd0, 0x010F));
        Assert.Equal("Tool", Text(merged, Ifd0, 0x0131));
        XmpPacket remaining = Apply(source, back.Xmp);
        Assert.Equal(new[] { "From XMP" }, Texts(remaining, Tiff, "Make"));
        Assert.Empty(Texts(remaining, Tiff, "Software"));
        Assert.Equal(new[] { "kept" }, Texts(remaining, XmpNs, "Label"));
    }

    // ---- IPTC and XMP ----

    private static IptcPacket EveryMappedDataset() => Iptc(e =>
    {
        foreach (IptcXmpMapping mapping in MetadataSync.IptcMappings)
        {
            byte id = mapping.Dataset.Dataset;
            if (mapping.TimeDataset is IptcDatasetKey time) { e.Set(mapping.Dataset, "20261112"); e.Set(time, "090507+0200"); }
            else if (mapping.XmpForm is XmpValueKind.Bag or XmpValueKind.Sequence) e.ReplaceText(mapping.Dataset, new[] { "first " + id, "second " + id });
            else e.Set(mapping.Dataset, id == 10 ? "5" : id == 15 ? "SPO" : id == 100 ? "LVA" : "Текст " + id);
        }
    });

    [Fact]
    public void EveryMappedDatasetIsWrittenToXmpAndComesBackAsTheSameStream()
    {
        IptcPacket iptc = EveryMappedDataset();

        SyncEdits toXmp = MetadataSync.IptcToXmp(iptc);
        Assert.Equal(string.Empty, Notes(toXmp));
        XmpPacket xmp = Apply((XmpPacket?)null, toXmp.Xmp);
        foreach (IptcXmpMapping mapping in MetadataSync.IptcMappings)
            Assert.True(Entry(xmp, mapping.Property.NamespaceUri, mapping.Property.LocalName) is not null, mapping.XmpPath);
        Assert.Equal(new[] { "first 25", "second 25" }, Texts(xmp, Dc, "subject"));
        Assert.Equal(XmpEntryKind.Bag, Entry(xmp, Dc, "subject")!.Kind);
        Assert.Equal(XmpEntryKind.Seq, Entry(xmp, Dc, "creator")!.Kind);
        Assert.Equal(XmpEntryKind.LangAlt, Entry(xmp, Dc, "title")!.Kind);
        Assert.Equal(new[] { "Текст 120" }, Texts(xmp, Dc, "description"));
        Assert.Equal(new[] { "2026-11-12T09:05:07+02:00" }, Texts(xmp, Photoshop, "DateCreated"));
        Assert.Equal(new[] { "2026-11-12T09:05:07+02:00" }, Texts(xmp, XmpNs, "CreateDate"));
        Assert.Equal(new[] { "5" }, Texts(xmp, Photoshop, "Urgency"));

        SyncEdits toIptc = MetadataSync.XmpToIptc(xmp);
        Assert.Equal(string.Empty, Notes(toIptc));
        IptcPacket back = Apply((IptcPacket?)null, toIptc.Iptc);
        Assert.Equal(iptc.Datasets.Count, back.Datasets.Count);
        foreach (IptcDataset dataset in iptc.Datasets.Where(d => d.Key.Record == 2 && d.Key.Dataset != 0))
            Assert.Equal(Texts(iptc, dataset.Key.Dataset), Texts(back, dataset.Key.Dataset));
    }

    [Fact]
    public void IimTextIsCutAtTheLengthOfItsDatasetAndADateLosesWhatIimCannotHold()
    {
        string longName = new string('é', 20) + "tail";   // 44 octets; a by-line holds 32
        XmpPacket xmp = Xmp(e =>
        {
            e.SetValue("Xmp.dc.creator", XmpValue.CreateArray(XmpValueKind.Sequence, new[] { longName, "Li" }));
            e.Set("Xmp.photoshop.DateCreated", "2026-11-12T09:05:07.25");
            e.Set("Xmp.xmp.CreateDate", "2026-11");
            e.Set("Xmp.photoshop.Urgency", "high");
            e.SetLanguage("Xmp.dc.title", "de", "Nur Deutsch");
        });
        IptcPacket existing = Iptc(e => e.Set(new IptcDatasetKey(2, 63), "101112+0000"));
        SyncEdits edits = MetadataSync.XmpToIptc(xmp, existing);
        IptcPacket iptc = Apply(existing, edits.Iptc);

        Assert.Equal(new[] { new string('é', 16), "Li" }, Texts(iptc, 80));
        Assert.Equal(new[] { "20261112" }, Texts(iptc, 55));
        Assert.Equal(new[] { "090507+0000" }, Texts(iptc, 60));
        Assert.Equal(new[] { "20261100" }, Texts(iptc, 62));
        Assert.Empty(Texts(iptc, 63));
        // Of a language alternative the default text is converted, or the first one when there is no default.
        Assert.Equal(new[] { "Nur Deutsch" }, Texts(iptc, 5));
        SyncNote note = Assert.Single(edits.Notes);
        Assert.Equal(("Xmp.photoshop.Urgency", "Iptc.2:10"), (note.Source, note.Target));

        // And back: the year and month alone, and a zone that XMP did not have.
        XmpPacket back = Apply((XmpPacket?)null, MetadataSync.IptcToXmp(iptc).Xmp);
        Assert.Equal(new[] { "2026-11" }, Texts(back, XmpNs, "CreateDate"));
        Assert.Equal(new[] { "2026-11-12T09:05:07+00:00" }, Texts(back, Photoshop, "DateCreated"));
    }

    [Fact]
    public void IptcFillsAndErasesLikeExifAndKeepsTheOtherLanguagesOfAnAlternative()
    {
        IptcPacket iptc = Iptc(e => { e.SetCaption("From IPTC"); e.SetHeadline("Headline"); e.SetKeywords(new[] { "a", "b" }); });
        XmpPacket xmp = Xmp(e => { e.SetLanguage("Xmp.dc.description", "x-default", "From XMP"); e.SetLanguage("Xmp.dc.description", "lv", "No XMP"); });

        XmpPacket filled = Apply(xmp, MetadataSync.IptcToXmp(iptc, xmp, new SyncOptions { Mode = SyncMode.FillMissing }).Xmp);
        Assert.Equal(new[] { "From XMP" }, Texts(filled, Dc, "description"));
        Assert.Equal(new[] { "Headline" }, Texts(filled, Photoshop, "Headline"));

        SyncEdits erasing = MetadataSync.IptcToXmp(iptc, xmp, new SyncOptions { EraseSource = true });
        XmpPacket overwritten = Apply(xmp, erasing.Xmp);
        Assert.Equal(new[] { ("x-default", "From IPTC"), ("lv", "No XMP") }, Entry(overwritten, Dc, "description")!.Languages.ToArray());
        IptcPacket left = Apply(iptc, erasing.Iptc);
        Assert.DoesNotContain(left.Datasets, d => d.Key.Record == 2 && d.Key.Dataset != 0);

        SyncEdits back = MetadataSync.XmpToIptc(overwritten, iptc, new SyncOptions { Mode = SyncMode.FillMissing, EraseSource = true });
        Assert.True(back.Iptc.IsEmpty);
        Assert.True(back.Xmp.IsEmpty);
    }

    // ---- Reconciliation (MWG) ----

    [Fact]
    public void ExifIsTakenForTheFieldsItSharesAndWrittenToTheOthers()
    {
        ExifMetadata exif = Block((Ifd0, 0x010E, Ascii("From EXIF")), (Ifd0, 0x013B, Ascii("Ana; Li")), (Ifd0, 0x010F, Ascii("Maker")),
            (ExifDir, 0x9003, Ascii("2026:11:12 09:05:07")));
        IptcPacket iptc = Iptc(e => { e.SetCaption("From IPTC"); e.SetBylines(new[] { "Someone" }); e.Set(new IptcDatasetKey(2, 55), "20200101"); });
        XmpPacket xmp = Xmp(e => { e.SetLanguage("Xmp.dc.description", "x-default", "From XMP"); e.Set("Xmp.photoshop.DateCreated", "2020-01-01"); });

        SyncEdits edits = MetadataSync.Reconcile(exif, iptc, xmp, IptcDigestState.Current);
        Assert.Equal(string.Empty, Notes(edits));
        Assert.True(edits.Exif.IsEmpty);
        Assert.Equal(new[] { new SyncChoice("Xmp.photoshop.DateCreated", SyncSource.Exif), new SyncChoice("Xmp.dc.creator", SyncSource.Exif), new SyncChoice("Xmp.dc.description", SyncSource.Exif) },
            edits.Choices.ToArray());
        XmpPacket newXmp = Apply(xmp, edits.Xmp);
        IptcPacket newIptc = Apply(iptc, edits.Iptc);
        Assert.Equal(new[] { "From EXIF" }, Texts(newXmp, Dc, "description"));
        Assert.Equal(new[] { "Ana", "Li" }, Texts(newXmp, Dc, "creator"));
        Assert.Equal(new[] { "2026-11-12T09:05:07" }, Texts(newXmp, Photoshop, "DateCreated"));
        Assert.Equal(new[] { "From EXIF" }, Texts(newIptc, 120));
        Assert.Equal(new[] { "Ana", "Li" }, Texts(newIptc, 80));
        Assert.Equal(new[] { "20261112" }, Texts(newIptc, 55));
        Assert.Equal(new[] { "090507+0000" }, Texts(newIptc, 60));
        // A tag that is EXIF's alone is not copied into XMP.
        Assert.Empty(Texts(newXmp, Tiff, "Make"));

        // What was reconciled is left alone.
        Assert.True(MetadataSync.Reconcile(exif, newIptc, newXmp, IptcDigestState.Current).IsEmpty);
    }

    [Theory]
    [InlineData(IptcDigestState.Absent, SyncSource.Xmp)]
    [InlineData(IptcDigestState.Current, SyncSource.Xmp)]
    [InlineData(IptcDigestState.Stale, SyncSource.Iptc)]
    public void BetweenIptcAndXmpTheDigestDecides(IptcDigestState digest, SyncSource expected)
    {
        IptcPacket iptc = Iptc(e => { e.SetKeywords(new[] { "iptc" }); e.SetCity("Riga"); });
        XmpPacket xmp = Xmp(e => { e.SetValue("Xmp.dc.subject", XmpValue.CreateArray(XmpValueKind.Bag, new[] { "xmp", "more" })); e.Set("Xmp.photoshop.Headline", "Only XMP"); });

        SyncEdits edits = MetadataSync.Reconcile(null, iptc, xmp, digest);
        Assert.Equal(new[] { new SyncChoice("Xmp.dc.subject", expected) }, edits.Choices.ToArray());
        Assert.True(edits.Exif.IsEmpty);
        XmpPacket newXmp = Apply(xmp, edits.Xmp);
        IptcPacket newIptc = Apply(iptc, edits.Iptc);
        string[] keywords = expected == SyncSource.Xmp ? new[] { "xmp", "more" } : new[] { "iptc" };
        Assert.Equal(keywords, Texts(newXmp, Dc, "subject"));
        Assert.Equal(keywords, Texts(newIptc, 25));
        // A field only one of them holds is copied to the other, whatever the digest says.
        Assert.Equal(new[] { "Riga" }, Texts(newXmp, Photoshop, "City"));
        Assert.Equal(new[] { "Only XMP" }, Texts(newIptc, 105));
        Assert.True(MetadataSync.Reconcile(null, newIptc, newXmp, IptcDigestState.Current).IsEmpty);
    }

    [Fact]
    public void IptcThatIsTheCutStartOfTheXmpTextAndDatesThatDifferInWhatOneLacksSayTheSame()
    {
        string longName = new string('n', 40);
        ExifMetadata exif = Block((ExifDir, 0x9003, Ascii("2026:11:12 09:05:07")));
        IptcPacket iptc = Iptc(e => { e.SetBylines(new[] { longName[..32] }); e.Set(new IptcDatasetKey(2, 55), "20261112"); e.Set(new IptcDatasetKey(2, 60), "090507+0200"); });
        XmpPacket xmp = Xmp(e =>
        {
            e.SetValue("Xmp.dc.creator", XmpValue.CreateArray(XmpValueKind.Sequence, new[] { longName }));
            e.Set("Xmp.photoshop.DateCreated", "2026-11-12T09:05:07.5+02:00");
        });

        SyncEdits edits = MetadataSync.Reconcile(exif, iptc, xmp, IptcDigestState.Stale);
        Assert.Equal(string.Empty, Notes(edits));
        Assert.True(edits.Iptc.IsEmpty);
        Assert.True(edits.Xmp.IsEmpty);
        // EXIF exists and lacks the creator: it is given the whole name, which is all the edits do.
        ExifMetadata written = Apply(exif, edits.Exif);
        Assert.Equal(2, written.Entries.Count);
        Assert.Equal(longName, Text(written, Ifd0, 0x013B));
        Assert.Contains(new SyncChoice("Xmp.dc.creator", SyncSource.Xmp), edits.Choices);
        Assert.Contains(new SyncChoice("Xmp.photoshop.DateCreated", SyncSource.Exif), edits.Choices);
    }

    [Fact]
    public void BlocksTheFileLacksAreCreatedOnlyOnRequestAndXmpAlways()
    {
        XmpPacket xmp = Xmp(e => { e.SetLanguage("Xmp.dc.rights", "x-default", "(c) Example"); e.Set("Xmp.xmp.ModifyDate", "2026-11-12T09:05:07"); });

        Assert.True(MetadataSync.Reconcile(null, null, xmp).IsEmpty);
        Assert.True(MetadataSync.Reconcile(null, null, null).IsEmpty);

        SyncEdits created = MetadataSync.Reconcile(null, null, xmp, options: new ReconcileOptions { CreateIptc = true, CreateExif = true });
        Assert.Equal(new[] { "(c) Example" }, Texts(Apply((IptcPacket?)null, created.Iptc), 116));
        ExifMetadata exif = Apply(null, created.Exif);
        Assert.Equal("(c) Example", Text(exif, Ifd0, 0x8298));
        Assert.Equal("2026:11:12 09:05:07", Text(exif, Ifd0, 0x0132));
        Assert.True(created.Xmp.IsEmpty);
        Assert.Empty(created.Choices);

        // Without XMP, the packet is created from what the others hold.
        SyncEdits fromOthers = MetadataSync.Reconcile(exif, Iptc(e => e.SetHeadline("Headline")), null);
        XmpPacket made = Apply((XmpPacket?)null, fromOthers.Xmp);
        Assert.Equal(new[] { "(c) Example" }, Texts(made, Dc, "rights"));
        Assert.Equal(new[] { "2026-11-12T09:05:07" }, Texts(made, XmpNs, "ModifyDate"));
        Assert.Equal(new[] { "Headline" }, Texts(made, Photoshop, "Headline"));
    }

    // ---- Mirroring all of EXIF, and what the edits are saved with ----

    [Fact]
    public void MirrorAllExifCopiesTheTagsThatAreExifsAloneAndThenFindsNothingToDo()
    {
        ExifMetadata exif = Block((Ifd0, 0x010E, Ascii("Caption")), (Ifd0, 0x010F, Ascii("Maker")), (ExifDir, 0x8827, Shorts(100, 200)));
        XmpPacket xmp = Xmp(e => { e.Set("Xmp.tiff.Make", "Another maker"); e.Set("Xmp.tiff.Model", "A model EXIF does not name"); });

        // By default only the shared field is written.
        XmpPacket plain = Apply(xmp, MetadataSync.Reconcile(exif, null, xmp).Xmp);
        Assert.Equal(new[] { "Caption" }, Texts(plain, Dc, "description"));
        Assert.Equal(new[] { "Another maker" }, Texts(plain, Tiff, "Make"));
        Assert.Empty(Texts(plain, ExifNs, "ISOSpeedRatings"));

        var options = new ReconcileOptions { MirrorAllExif = true };
        SyncEdits edits = MetadataSync.Reconcile(exif, null, xmp, options: options);
        Assert.Equal(string.Empty, Notes(edits));
        Assert.True(edits.Exif.IsEmpty);
        XmpPacket mirrored = Apply(xmp, edits.Xmp);
        Assert.Equal(new[] { "Caption" }, Texts(mirrored, Dc, "description"));
        Assert.Equal(new[] { "Maker" }, Texts(mirrored, Tiff, "Make"));
        Assert.Equal(new[] { "100", "200" }, Texts(mirrored, ExifNs, "ISOSpeedRatings"));
        // A property whose tag EXIF lacks is left alone.
        Assert.Equal(new[] { "A model EXIF does not name" }, Texts(mirrored, Tiff, "Model"));

        // What was mirrored is left alone.
        SyncEdits again = MetadataSync.Reconcile(exif, null, mirrored, options: options);
        Assert.Equal(string.Empty, Notes(again));
        Assert.True(again.Xmp.IsEmpty);
        Assert.True(again.IsEmpty);
    }

    [Fact]
    public void ReconciledEditsAskForTheDigestAndConvertedEditsDoNot()
    {
        IptcPacket iptc = Iptc(e => e.SetHeadline("Headline"));
        XmpPacket xmp = Xmp(e => e.Set("Xmp.photoshop.Headline", "Headline"));
        Assert.True(MetadataSync.Reconcile(null, iptc, xmp).IptcWriteOptions.UpdateDigest);
        Assert.False(MetadataSync.IptcToXmp(iptc).IptcWriteOptions.UpdateDigest);
        Assert.False(MetadataSync.XmpToIptc(xmp).IptcWriteOptions.UpdateDigest);
        // A packet that no document read says nothing about a digest.
        Assert.Equal(IptcDigestState.Absent, iptc.Digest);
    }

    [Fact]
    public void ABlankTextSaysNothingAndAShortTextIsGivenBackAtItsLength()
    {
        // Spaces, NULs, and a comment that is its character code and nothing else: no property and no note.
        byte[] comment = [.. "ASCII\0\0\0"u8, 0x20, 0x20, 0, 0];
        ExifMetadata blank = Block((Ifd0, 0x010E, Ascii("   ")), (Ifd0, 0x013B, Ascii(string.Empty)),
            (ExifDir, 0x9286, Bytes(TiffFieldType.Undefined, comment)), (Ifd0, 0x010F, Ascii("Maker")));
        SyncEdits edits = MetadataSync.ExifToXmp(blank);
        Assert.Equal(string.Empty, Notes(edits));
        XmpPacket xmp = Apply((XmpPacket?)null, edits.Xmp);
        Assert.Equal(new[] { "Maker" }, Texts(xmp, Tiff, "Make"));
        Assert.Empty(Texts(xmp, Dc, "description"));
        Assert.Empty(Texts(xmp, Dc, "creator"));
        Assert.Empty(Texts(xmp, ExifNs, "UserComment"));

        // ImageUniqueID is defined with 33 bytes and cameras write fewer: the text comes back as it was.
        ExifMetadata exif = Block((ExifDir, 0xA420, Ascii("C12QLJK01SM")));
        SyncEdits there = MetadataSync.ExifToXmp(exif);
        SyncEdits back = MetadataSync.XmpToExif(Apply((XmpPacket?)null, there.Xmp));
        Assert.Equal(string.Empty, Notes(there) + Notes(back));
        Assert.Equal("C12QLJK01SM", Text(Apply((ExifMetadata?)null, back.Exif), ExifDir, 0xA420));

        // A count the tag is not defined with is not written; the note gives the reason and nothing else.
        SyncEdits wrong = MetadataSync.XmpToExif(Xmp(e => e.SetValue("Xmp.tiff.BitsPerSample", XmpValue.CreateArray(XmpValueKind.Sequence, new[] { "8", "8", "8", "8" }))));
        Assert.True(wrong.Exif.IsEmpty);
        Assert.Equal("Xmp.tiff.BitsPerSample -> Exif.Ifd0.0x0102: BitsPerSample (0x0102) takes 3 value(s), not 4.", Notes(wrong));

        // Elements written as one text with spaces between them, as Exiv2 writes an array, are the elements.
        SyncEdits spaced = MetadataSync.XmpToExif(Xmp(e => e.SetValue("Xmp.exif.ISOSpeedRatings", XmpValue.CreateArray(XmpValueKind.Sequence, new[] { "100 200" }))));
        Assert.Equal(string.Empty, Notes(spaced));
        Assert.Equal(Raw(Block((ExifDir, 0x8827, Shorts(100, 200))), new(ExifDir, 0x8827)), Raw(Apply(null, spaced.Exif), new(ExifDir, 0x8827)));
    }

    [Fact]
    public void TheCharsetMarkerExiv2WritesIsTakenOffAndItsCodeIsUsed()
    {
        static byte[] Value(string name, string text)
        {
            // UserComment is a language alternative in XMP, the two GPS texts are simple.
            ExifMetadata exif = Apply(null, MetadataSync.XmpToExif(Xmp(e =>
            {
                if (name == "UserComment") e.SetLanguage("Xmp.exif." + name, "x-default", text); else e.Set("Xmp.exif." + name, text);
            })).Exif);
            ExifTagKey key = name == "UserComment" ? new(ExifDir, 0x9286) : new(Gps, (ushort)(name == "GPSProcessingMethod" ? 0x1B : 0x1C));
            return Raw(exif, key);
        }

        Assert.Equal("ASCII\0\0\0HYBRID-FIX"u8.ToArray(), Value("GPSProcessingMethod", "charset=Ascii HYBRID-FIX"));
        Assert.Equal("ASCII\0\0\0Harbour"u8.ToArray(), Value("GPSAreaInformation", "charset=Ascii Harbour"));
        Assert.Equal("\0\0\0\0\0\0\0\0GPS"u8.ToArray(), Value("GPSProcessingMethod", "charset=Undefined GPS"));
        Assert.Equal(new byte[] { (byte)'U', (byte)'N', (byte)'I', (byte)'C', (byte)'O', (byte)'D', (byte)'E', 0, (byte)'h', 0, (byte)'i', 0 },
            Value("UserComment", "charset=Unicode hi"));
        // JIS is not encoded here: the marker goes, the text is written under the code it fits.
        Assert.Equal("ASCII\0\0\0text"u8.ToArray(), Value("UserComment", "charset=Jis text"));
        // Ascii named for text that is not ASCII: UNICODE.
        Assert.Equal(new byte[] { (byte)'U', (byte)'N', (byte)'I', (byte)'C', (byte)'O', (byte)'D', (byte)'E', 0, 0xE9, 0 }, Value("UserComment", "charset=Ascii \u00E9"));

        // Anything else is the text: another spelling, another name, a marker that is not at the start.
        Assert.Equal("ASCII\0\0\0charset=ascii x"u8.ToArray(), Value("UserComment", "charset=ascii x"));
        Assert.Equal("ASCII\0\0\0charset=Latin1 x"u8.ToArray(), Value("UserComment", "charset=Latin1 x"));
        Assert.Equal("ASCII\0\0\0charset=Ascii"u8.ToArray(), Value("UserComment", "charset=Ascii"));
        Assert.Equal("ASCII\0\0\0a charset=Ascii x"u8.ToArray(), Value("GPSAreaInformation", "a charset=Ascii x"));

        // What was written reads back as the text alone.
        ExifMetadata written = Apply(null, MetadataSync.XmpToExif(Xmp(e => e.Set("Xmp.exif.GPSProcessingMethod", "charset=Ascii HYBRID-FIX"))).Exif);
        Assert.Equal(new[] { "HYBRID-FIX" }, Texts(Apply((XmpPacket?)null, MetadataSync.ExifToXmp(written).Xmp), ExifNs, "GPSProcessingMethod"));
    }
}
