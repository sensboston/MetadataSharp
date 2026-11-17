using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Crw;
using MetadataSharp.Formats.Jp2;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Operations;
using MetadataSharp.Formats.Pgf;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Psd;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.WebP;

namespace MetadataSharp.Fuzz;

/// <summary>A broken fuzz invariant. The libraries never throw it; only the harness does.</summary>
internal sealed class FuzzInvariantException : Exception
{
    /// <summary>Creates the failure.</summary>
    /// <param name="message">What was violated.</param>
    /// <param name="inner">The exception that showed it, if any.</param>
    public FuzzInvariantException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The fuzz targets, shared by the SharpFuzz harness (tools/MetadataSharp.Fuzz) and the regression
/// tests, which compile this file too. Each target takes arbitrary bytes and either succeeds or is
/// refused with a <see cref="MetadataException"/> whose code is defined - and where an API documents
/// that it does not refuse at all (<see cref="ImageMetadata.Read(ReadOnlySpan{byte}, MetadataLimits?)"/>,
/// <see cref="Previews.List"/>, <see cref="EmbeddedImageMetadata"/>), not even that. Anything else
/// escapes and is a bug: another exception, a result that exceeds the <see cref="MetadataLimits"/> it
/// was read under, a stream read that differs from the read of the same bytes in memory, a written
/// file that does not read back with its change or with a new error. Hangs, stack overflows and
/// out-of-memory failures are what the engine around the process observes.
/// </summary>
internal static class FuzzTargets
{
    /// <summary>Every target, in the order the workflow lists them.</summary>
    internal static readonly string[] Names = ["span", "stream", "previews", "embedded", "icc", "comments", "iptc", "xmp", "maker", "write"];

    /// <summary>The library defaults, given explicitly so that the results can be held to them.</summary>
    internal static readonly MetadataLimits Limits = new();

    /// <summary>Header of a <c>maker</c> input: make index, byte order, note start, note length, model length.</summary>
    internal const int MakerHeaderLength = 11;

    /// <summary>The Make prefixes that select every vendor branch of the MakerNote reader.</summary>
    internal static readonly string[] Makes =
    [
        "Canon", "FUJIFILM", "FUJI PHOTO FILM", "PENTAX", "RICOH", "SONY", "Panasonic", "OLYMPUS", "OM Digital",
        "NIKON", "Nikon", "MINOLTA", "KONICA MINOLTA", "CASIO", "SAMSUNG", "SIGMA", "FOVEON",
    ];

    private static readonly byte[][] MakeBytes = Makes.Select(make => Encoding.ASCII.GetBytes(make)).ToArray();
    private const string Marker = "MetadataSharp.Fuzz";
    private static readonly XmpPropertyName CreatorTool = new("http://ns.adobe.com/xap/1.0/", "CreatorTool");
    private static readonly ExifTagKey OrientationKey = new(ExifDirectoryKind.Ifd0, 0x0112);
    private static readonly IptcDatasetKey CaptionKey = new(2, 120);
    private static readonly int XmpCeiling = new XmpLimits().MaxPacketBytes;
    private static readonly int IptcCeiling = new IptcLimits().MaxPacketBytes;
    private static readonly byte[] MinimalJpeg = BuildMinimalJpeg();
    private static readonly Lazy<byte[]> Sidecar = new(BuildSidecar);
    private static readonly Lazy<byte[]> RichJpeg = new(BuildRichJpeg);
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>Runs one target over one input and enforces its contract.</summary>
    /// <param name="target">One of <see cref="Names"/>.</param>
    /// <param name="data">The input.</param>
    internal static void Run(string target, byte[] data)
    {
        switch (target)
        {
            case "span": SpanTarget(data); break;
            case "stream": StreamTarget(data); break;
            case "previews": PreviewsTarget(data); break;
            case "embedded": EmbeddedTarget(data); break;
            case "icc": IccTarget(data); break;
            case "comments": CommentsTarget(data); break;
            case "iptc": IptcTarget(data); break;
            case "xmp": XmpTarget(data); break;
            case "maker": MakerTarget(data); break;
            case "write": WriteTarget(data); break;
            default: throw new ArgumentException("Unknown fuzz target: " + target, nameof(target));
        }
    }

    /// <summary>The vendor index of a Make, or -1 when no vendor branch reads it.</summary>
    /// <param name="make">The Make tag.</param>
    /// <returns>The index into <see cref="Makes"/>.</returns>
    internal static int VendorIndex(string? make)
    {
        if (string.IsNullOrEmpty(make)) return -1;
        for (int i = 0; i < Makes.Length; i++)
            if (make.StartsWith(Makes[i], StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static bool Defined(MetadataException refusal) => Enum.IsDefined(refusal.Code);

    private static FuzzInvariantException Broken(string message, Exception? inner = null) => new(message, inner);

    // ---- read targets -------------------------------------------------------------------------------

    // Documented: nothing about the content of a file throws; what a reader refuses is in Errors.
    private static void SpanTarget(byte[] data) => Check(ImageMetadata.Read(data.AsSpan(), Limits), Limits);

    // Read(Stream) gives "the snapshot, as for the read of a file held in memory": through a seekable
    // stream (read where the readers ask) and through a forward-only stream of short reads (buffered).
    // The reader defaults apply here (no limits given), the other half of the configurations.
    private static void StreamTarget(byte[] data)
    {
        ImageMetadata memory = ImageMetadata.Read(data.AsSpan());
        Check(memory, null);
        string expected = Render(memory);
        using (var seekable = new MemoryStream(data, writable: false))
            Same("seekable stream", expected, Render(ImageMetadata.Read(seekable)));
        using (var forward = new ForwardOnlyStream(data))
            Same("non-seekable stream", expected, Render(ImageMetadata.Read(forward)));
    }

    private static void Same(string path, string expected, string actual)
    {
        if (expected == actual) return;
        string[] left = expected.Split('\n'), right = actual.Split('\n');
        int line = 0;
        while (line < left.Length && line < right.Length && left[line] == right[line]) line++;
        string Show(string[] lines) => line < lines.Length ? Shorten(lines[line]) : "<end>";
        throw Broken($"The read through a {path} differs from the read in memory at line {line + 1}: memory '{Show(left)}', stream '{Show(right)}'.");
    }

    private static string Shorten(string text) => text.Length <= 160 ? text : text[..160] + "...";

    // Documented: a listing is refused only for a budget, or for the malformed container of a CRW,
    // MRW or RAF file, whose previews are where the container says.
    private static bool ListingRefusal(MetadataException refusal) =>
        refusal.Code is MetadataErrorCode.LimitExceeded or MetadataErrorCode.InvalidCrw or MetadataErrorCode.InvalidMrw or MetadataErrorCode.InvalidRaf;

    private static IReadOnlyList<PreviewImage> ListPreviews(byte[] data)
    {
        try { return Previews.List(data, Limits); }
        catch (MetadataException refusal) when (ListingRefusal(refusal)) { return []; }
    }

    private static void PreviewsTarget(byte[] data)
    {
        foreach (PreviewImage preview in ListPreviews(data))
        {
            // Documented: a pointer that leaves the file is not listed. A preview the library builds
            // (from hex or base64 text, or from strips) has a length of its own, so only its start is held.
            bool built = preview.Source is "EPS.AI7.Hex" or "EPS.Photoshop.Hex" ||
                preview.Source.StartsWith("Xmp.Thumbnail", StringComparison.Ordinal) ||
                preview.Source.StartsWith("PNG.RawExif.", StringComparison.Ordinal) ||
                (preview.MimeType == "image/tiff" && preview.Source.EndsWith(".StripOffsets", StringComparison.Ordinal));
            if (preview.Offset < 0 || preview.Offset >= data.Length || preview.Length <= 0 ||
                (!built && preview.Offset + preview.Length > data.Length))
                throw Broken($"The preview {preview} does not lie inside the {data.Length}-byte file.");
            byte[] bytes;
            try { bytes = Previews.Extract(data, preview, Limits); }
            catch (MetadataException refusal) when (Defined(refusal)) { continue; }
            if (preview.MimeType != "image/jpeg") continue;
            // Documented: a JPEG preview begins with SOI and ends with EOI; zero padding after EOI is returned.
            int end = bytes.Length;
            while (end > 0 && bytes[end - 1] == 0) end--;
            if (end < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[end - 2] != 0xFF || bytes[end - 1] != 0xD9)
                throw Broken($"The JPEG preview {preview} was extracted without SOI and EOI.");
        }
    }

    // Documented: a preview without metadata, or one that cannot be read, gives null rather than a
    // refusal; the file is refused only as its listing is.
    private static void EmbeddedTarget(byte[] data)
    {
        try { Touch(EmbeddedImageMetadata.Read(data, Limits)); }
        catch (MetadataException refusal) when (ListingRefusal(refusal)) { }
        foreach (PreviewImage preview in ListPreviews(data))
            Touch(EmbeddedImageMetadata.Read(data, preview, Limits));
    }

    private static void IccTarget(byte[] data)
    {
        byte[]? profile;
        try { profile = IccProfiles.Read(data, Limits); }
        catch (MetadataException refusal) when (Defined(refusal)) { return; }
        if (profile is null) return;
        // Documented: the smaller of the two budgets bounds the profile; a header shorter than 128
        // bytes, a size field the bytes do not fill or a missing acsp signature is refused; bytes after
        // the declared size are padding and are not returned.
        int ceiling = Math.Min(Limits.MaxValueBytes, Limits.MaxRetainedMetadataBytes);
        if (profile.Length > ceiling)
            throw Broken($"An ICC profile of {profile.Length} bytes was returned under a budget of {ceiling}.");
        if (profile.Length < 128 || BinaryPrimitives.ReadUInt32BigEndian(profile) != (uint)profile.Length ||
            !profile.AsSpan(36, 4).SequenceEqual("acsp"u8))
            throw Broken($"An ICC profile of {profile.Length} bytes was returned that is not one: declared size and signature disagree.");
    }

    private static void CommentsTarget(byte[] data)
    {
        IReadOnlyList<string> comments;
        try { comments = Comments.Read(data, Limits); }
        catch (MetadataException refusal) when (Defined(refusal)) { return; }
        CheckComments(comments, Limits);
    }

    private static void IptcTarget(byte[] data)
    {
        // The strict reader, and the lenient one the containers fall back to (read-only result).
        IptcPacket? strict = Attempt(() => IptcPacket.Parse(data));
        IptcPacket? lenient = Attempt(() => IptcPacket.Inspect(data));
        if (lenient is not null) Touch(lenient);
        if (strict is null) return;
        Touch(strict);
        if (!strict.IsEditable) return;
        // Documented: a dataset the edit does not name keeps its bytes and its place; the result parses.
        var edit = new IptcEdit();
        edit.Set(CaptionKey, Marker);
        IptcPacket? applied = Attempt(() => strict.Apply(edit));
        if (applied is null) return;
        IptcPacket reparsed;
        try { reparsed = IptcPacket.Parse(Raw(applied)); }
        catch (MetadataException refusal) { throw Broken("An edited IPTC stream does not parse again: " + refusal.Code, refusal); }
        Touch(reparsed);
        if (reparsed.Caption != Marker) throw Broken("An edited IPTC stream lost the caption it was given.");
        string Kept(IptcPacket packet) => string.Join(";", packet.Datasets.Where(dataset => dataset.Key != CaptionKey)
            .Select(dataset => dataset.Key.Record + ":" + dataset.Key.Dataset + "=" + Convert.ToHexString(Raw(dataset.Value))));
        if (Kept(strict) != Kept(reparsed)) throw Broken("An IPTC edit of the caption changed datasets it did not name.");
    }

    private static void XmpTarget(byte[] data)
    {
        XmpPacket? packet = Attempt(() => XmpPacket.Parse(data));
        XmpPacket? loaded = Attempt(() =>
        {
            using var stream = new MemoryStream(data, writable: false);
            return XmpPacket.Load(stream);
        });
        if ((packet is null) != (loaded is null))
            throw Broken("XmpPacket.Parse and XmpPacket.Load disagree about whether the bytes are a packet.");
        if (loaded is not null) Touch(loaded);
        if (packet is null) return;
        Touch(packet);
        if (!packet.IsEditable) return;
        // The packet Apply writes is read back by Apply itself; Save must then give bytes that parse to it.
        XmpPacket? applied = Attempt(() => packet.Apply(MarkerEdit()));
        if (applied is null) return;
        using var output = new MemoryStream();
        applied.Save(output);
        XmpPacket reparsed;
        try { reparsed = XmpPacket.Parse(output.ToArray()); }
        catch (MetadataException refusal) { throw Broken("A saved XMP packet does not parse again: " + refusal.Code, refusal); }
        Touch(reparsed);
        if (!reparsed.IsEditable || !HasMarker(reparsed))
            throw Broken("A saved XMP packet is not editable again or lost its change.");
    }

    // The vendor readers are internal; the input chooses the Make, the byte order, the note range and the Model.
    private static void MakerTarget(byte[] data)
    {
        if (data.Length < MakerHeaderLength) return;
        byte[] make = MakeBytes[data[0] % MakeBytes.Length];
        Endianness order = (data[1] & 1) == 0 ? Endianness.LittleEndian : Endianness.BigEndian;
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2, 4));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6, 4));
        int modelLength = Math.Min(data[10], data.Length - MakerHeaderLength);
        byte[] model = data.AsSpan(MakerHeaderLength, modelLength).ToArray();
        byte[] tiff = data.AsSpan(MakerHeaderLength + modelLength).ToArray();
        int noteStart = (int)Math.Min(start, (uint)tiff.Length);
        int noteLength = (int)Math.Min(length, (uint)(tiff.Length - noteStart));
        MakerNote? note;
        try { note = MakerNoteReader.TryRead(tiff, noteStart, noteLength, order, make, model, Limits, Limits.MaxRetainedMetadataBytes); }
        catch (MetadataException refusal) when (Defined(refusal)) { return; }
        if (note is null) return;
        foreach (MakerNoteEntry entry in note.Entries)
            Within(entry.Value, Limits, "MakerNote tag " + entry.TagId);
        Touch(note, null, Encoding.ASCII.GetString(model));
    }

    // ---- write target -------------------------------------------------------------------------------

    // Read, change, write, read again: every writer of the format, and the operations built on them.
    // The documents and the operations read with the reader defaults, and so does this target.
    private static void WriteTarget(byte[] data)
    {
        ImageMetadata before = ImageMetadata.Read(data.AsSpan());
        Check(before, null);
        MetadataFormat format = before.Format;
        bool Clean(MetadataFamily family) => !before.Errors.Any(error => error.Family == family);
        bool holdsIptc = format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.Tiff or MetadataFormat.Dcp;

        var orientation = new ExifEdit();
        orientation.Set(OrientationKey, (ushort)1);
        Written(before, "EXIF edit", () => SaveExif(format, data, orientation),
            after => !Clean(MetadataFamily.Exif) || HasOrientation(after));

        var edits = new SyncEdits();
        edits.Exif.Set(OrientationKey, (ushort)1);
        edits.Xmp.Set(CreatorTool, XmpValue.CreateText(Marker));
        if (holdsIptc) edits.Iptc.Set(CaptionKey, Marker);
        Written(before, "combined save", () => SaveAll(format, data, edits), after =>
            (!Clean(MetadataFamily.Exif) || HasOrientation(after)) &&
            (!Clean(MetadataFamily.Xmp) || HasMarker(after.Xmp)) &&
            (!holdsIptc || !Clean(MetadataFamily.IptcIim) || after.Iptc?.Caption == Marker));

        Written(before, "comment", () => SaveComment(format, data),
            after => !Clean(MetadataFamily.Comment) || after.Comments.Contains(Marker));

        Written(before, "XMP sidecar insertion", () => MetadataOperations.InsertXmpSidecar(data, Sidecar.Value),
            after => !Clean(MetadataFamily.Xmp) || HasMarker(after.Xmp));

        // Documented: the file without the kinds named; a kind its reader refuses is removed whole.
        bool removesExif = format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP;
        bool removesIptc = format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.Tiff or MetadataFormat.Pgf;
        bool removesComment = format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.Pgf;
        bool removesIcc = !before.IccProfile.IsEmpty &&
            format is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP or MetadataFormat.Tiff or MetadataFormat.Bmff;
        bool Removed(ImageMetadata after, bool exif) =>
            Gone(after, MetadataFamily.Xmp, after.Xmp is null) &&
            (!removesIptc || Gone(after, MetadataFamily.IptcIim, after.Iptc is null)) &&
            (!exif || !removesExif || Gone(after, MetadataFamily.Exif, after.Exif is null)) &&
            (!removesComment || after.Comments.Count == 0) &&
            (!removesIcc || after.IccProfile.IsEmpty);
        Written(before, "delete", () => MetadataOperations.Delete(data, MetadataFamilies.All), after => Removed(after, exif: true));
        const MetadataFamilies AllButExif = MetadataFamilies.Iptc | MetadataFamilies.Xmp | MetadataFamilies.Comment | MetadataFamilies.IccProfile;
        Written(before, "delete all but EXIF", () => MetadataOperations.Delete(data, AllButExif),
            after => Removed(after, exif: false) && (before.Exif is null || after.Exif is not null));
        if (before.Exif?.Entries.Any(entry => entry.Key.Directory == ExifDirectoryKind.Thumbnail) == true)
            Written(before, "thumbnail removal", () => MetadataOperations.Delete(data, MetadataFamilies.Thumbnail),
                after => !removesExif || after.Exif is null || after.Exif.Entries.All(entry => entry.Key.Directory != ExifDirectoryKind.Thumbnail));

        if (format is MetadataFormat.Psd or MetadataFormat.Jp2 && Clean(MetadataFamily.Xmp))
        {
            XmpPacket? xmp = Attempt(() => (before.Xmp ?? XmpPacket.CreateEmpty()).Apply(MarkerEdit()));
            byte[]? icc = before.IccProfile.IsEmpty ? null : before.IccProfile.ToArray();
            if (xmp is not null)
                Written(before, "metadata rewrite", () => format == MetadataFormat.Psd
                    ? PsdMetadata.Write(data, before.Exif, before.Iptc, xmp, icc)
                    : Jp2Metadata.Write(data, before.Exif, before.Iptc, xmp, icc), after => HasMarker(after.Xmp));
        }
        if (format == MetadataFormat.Pgf)
            Written(before, "PGF comment", () => PgfMetadata.Write(data, new SyncEdits(), Marker, replaceComment: true),
                after => !Clean(MetadataFamily.Comment) || after.Comments.Contains(Marker));

        // The operations that take the file as a source into a clean JPEG, and the reverse.
        ImageMetadata clean = ImageMetadata.Read(MinimalJpeg);
        Copied(before, "copy into the file", () => MetadataOperations.CopyMetadata(RichJpeg.Value, data));
        Copied(clean, "copy from the file", () => MetadataOperations.CopyMetadata(data, MinimalJpeg));
        if (AttemptBytes(() => MetadataOperations.ExtractExv(data)) is { } exv)
        {
            ImageMetadata extracted = ImageMetadata.Read(exv);
            if (extracted.Format != MetadataFormat.Exv)
                throw Broken("ExtractExv gave a file that reads as " + extracted.Format + ".");
            Check(extracted, null);
            NoNewErrors(before, extracted, "EXV extraction");
            Written(clean, "EXV insertion", () => MetadataOperations.InsertExv(MinimalJpeg, exv, MetadataFamilies.All, out _), null);
        }
        if (AttemptBytes(() => MetadataOperations.ExtractXmpSidecar(data)) is { } sidecar)
        {
            try { Touch(XmpPacket.Parse(sidecar)); }
            catch (MetadataException refusal) { throw Broken("ExtractXmpSidecar gave a packet that does not parse: " + refusal.Code, refusal); }
        }
        byte[]? thumbnail = AttemptBytes(() => MetadataOperations.ExtractThumbnail(data));
        if (thumbnail is not null && (thumbnail.Length < 4 || thumbnail[0] != 0xFF || thumbnail[1] != 0xD8))
            throw Broken("ExtractThumbnail gave bytes that are not a JPEG stream.");
    }

    private static bool Gone(ImageMetadata after, MetadataFamily family, bool absent) =>
        absent && !after.Errors.Any(error => error.Family == family);

    private static void Copied(ImageMetadata destination, string operation, Func<MetadataCopyResult> copy)
    {
        MetadataCopyResult result;
        try { result = copy(); }
        catch (MetadataException refusal) when (Defined(refusal)) { return; }
        if (!result.WasRefused) Written(destination, operation, () => result.DestinationBytes, null);
    }

    private static void Written(ImageMetadata before, string operation, Func<byte[]?> write, Func<ImageMetadata, bool>? landed)
    {
        byte[]? output;
        try { output = write(); }
        catch (MetadataException refusal) when (Defined(refusal)) { return; }
        if (output is null) return;
        ImageMetadata after = ImageMetadata.Read(output);
        // Documented: an effective edit of a DCP profile gives it the classic TIFF magic.
        bool kept = after.Format == before.Format || (before.Format == MetadataFormat.Dcp && after.Format == MetadataFormat.Tiff);
        if (!kept) throw Broken($"{operation}: a {before.Format} file was written as {after.Format}.");
        Check(after, null);
        NoNewErrors(before, after, operation);
        if (landed is not null && !landed(after))
            throw Broken($"{operation}: the written {before.Format} file does not read back with the change: before {Describe(before)}; after {Describe(after)}.");
    }

    // What a failed write is told by: the change of each kind, and what each kind holds.
    private static string Describe(ImageMetadata metadata) =>
        $"orientation {HasOrientation(metadata)}, XMP mark {HasMarker(metadata.Xmp)}, caption {metadata.Iptc?.Caption ?? "-"}, " +
        $"EXIF {metadata.Exif?.Entries.Count.ToString() ?? "-"}, IPTC {metadata.Iptc?.Datasets.Count.ToString() ?? "-"}, " +
        $"XMP {metadata.Xmp?.ByteLength.ToString() ?? "-"}, ICC {metadata.IccProfile.Length}, comments {metadata.Comments.Count}, " +
        $"errors [{string.Join(", ", metadata.Errors.Select(error => error.Family + " " + error.Code))}]";

    private static void NoNewErrors(ImageMetadata before, ImageMetadata after, string operation)
    {
        foreach (MetadataReadError error in after.Errors)
            if (!before.Errors.Any(old => old.Family == error.Family))
                throw Broken($"{operation}: the written file has a {error.Family} error the source did not have: {error.Code}: {error.Exception.Message}", error.Exception);
    }

    private static byte[]? SaveExif(MetadataFormat format, byte[] data, ExifEdit edit)
    {
        using var input = new MemoryStream(data, writable: false);
        switch (format)
        {
            case MetadataFormat.Jpeg:
                using (JpegMetadataDocument document = JpegMetadataDocument.Open(input)) return Saved(output => document.Save(output, edit));
            case MetadataFormat.Png:
                using (PngMetadataDocument document = PngMetadataDocument.Open(input)) return Saved(output => document.SaveExif(output, edit));
            case MetadataFormat.WebP:
                using (WebPMetadataDocument document = WebPMetadataDocument.Open(input)) return Saved(output => document.SaveExif(output, edit));
            case MetadataFormat.Tiff:
            case MetadataFormat.Dcp:
                using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(input)) return Saved(output => document.SaveExif(output, edit));
            case MetadataFormat.Bmff:
                using (BmffMetadataDocument document = BmffMetadataDocument.Open(input)) return Saved(output => document.SaveExif(output, edit));
            case MetadataFormat.Crw:
                using (CrwMetadataDocument document = CrwMetadataDocument.Open(input)) return Saved(output => document.SaveExif(output, edit));
            default:
                return null;
        }
    }

    private static byte[]? SaveAll(MetadataFormat format, byte[] data, SyncEdits edits)
    {
        using var input = new MemoryStream(data, writable: false);
        switch (format)
        {
            case MetadataFormat.Jpeg:
                using (JpegMetadataDocument document = JpegMetadataDocument.Open(input)) return Saved(output => document.SaveAll(output, edits));
            case MetadataFormat.Png:
                using (PngMetadataDocument document = PngMetadataDocument.Open(input)) return Saved(output => document.SaveAll(output, edits));
            case MetadataFormat.WebP:
                using (WebPMetadataDocument document = WebPMetadataDocument.Open(input)) return Saved(output => document.SaveAll(output, edits));
            case MetadataFormat.Tiff:
            case MetadataFormat.Dcp:
                using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(input)) return Saved(output => document.SaveAll(output, edits));
            case MetadataFormat.Bmff:
                using (BmffMetadataDocument document = BmffMetadataDocument.Open(input)) return Saved(output => document.SaveAll(output, edits));
            default:
                return null;
        }
    }

    private static byte[]? SaveComment(MetadataFormat format, byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        switch (format)
        {
            case MetadataFormat.Jpeg:
                using (JpegMetadataDocument document = JpegMetadataDocument.Open(input)) return Saved(output => document.SaveComment(output, Marker));
            case MetadataFormat.Png:
                using (PngMetadataDocument document = PngMetadataDocument.Open(input)) return Saved(output => document.SaveComment(output, Marker));
            case MetadataFormat.Crw:
                using (CrwMetadataDocument document = CrwMetadataDocument.Open(input)) return Saved(output => document.SaveComment(output, Marker));
            default:
                return null;
        }
    }

    private static byte[] Saved(Action<Stream> save)
    {
        using var output = new MemoryStream();
        save(output);
        return output.ToArray();
    }

    private static bool HasOrientation(ImageMetadata metadata) =>
        metadata.Exif is { } exif && exif.TryGetEntry(OrientationKey, out ExifEntry? entry) &&
        entry.Value.TryGetUInt32(out uint value) && value == 1;

    private static bool HasMarker(XmpPacket? packet) =>
        packet is not null && packet.Entries.Any(entry =>
            entry.NamespaceUri == CreatorTool.NamespaceUri && entry.Path == "CreatorTool" && entry.Values.Contains(Marker));

    private static XmpEdit MarkerEdit()
    {
        var edit = new XmpEdit();
        edit.Set(CreatorTool, XmpValue.CreateText(Marker));
        return edit;
    }

    private static byte[] BuildSidecar() =>
        Raw(XmpPacket.CreateEmpty().Apply(MarkerEdit()) ?? throw new InvalidOperationException("The sidecar packet could not be built."));

    // An authored 8x8 grayscale baseline JPEG: one quantization table of ones, a DC table whose one
    // code is category zero and an AC table whose one code is end-of-block, and one entropy byte.
    private static byte[] BuildMinimalJpeg()
    {
        byte[] quant = new byte[65];
        Array.Fill(quant, (byte)1, 1, 64);
        byte[] huffman = new byte[36];
        huffman[1] = 1;
        huffman[18] = 0x10;
        huffman[19] = 1;
        var file = new List<byte> { 0xFF, 0xD8 };
        void Segment(byte marker, byte[] payload)
        {
            file.AddRange([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)]);
            file.AddRange(payload);
        }
        Segment(0xDB, quant);
        Segment(0xC4, huffman);
        Segment(0xC0, [8, 0, 8, 0, 8, 1, 1, 0x11, 0]);
        Segment(0xDA, [1, 1, 0, 0, 63, 0]);
        file.AddRange([0x3F, 0xFF, 0xD9]);
        return file.ToArray();
    }

    // A JPEG with every kind the copy operation carries, written by the library from the minimal one.
    private static byte[] BuildRichJpeg()
    {
        var edits = new SyncEdits();
        edits.Exif.Set(OrientationKey, (ushort)6);
        edits.Exif.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010F), "Canon");
        edits.Exif.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0110), "Canon EOS 5D");
        edits.Exif.Set(new ExifTagKey(ExifDirectoryKind.Exif, 0x9003), "2026:11:14 03:31:00");
        edits.Iptc.Set(CaptionKey, Marker);
        edits.Iptc.SetKeywords(["fuzz", "metadata"]);
        edits.Xmp.Set(CreatorTool, XmpValue.CreateText(Marker));
        byte[] file;
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(MinimalJpeg, writable: false)))
            file = Saved(output => document.SaveAll(output, edits));
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file, writable: false)))
            file = Saved(output => document.SaveComment(output, Marker));
        return file;
    }

    // ---- invariants of a snapshot ---------------------------------------------------------------------

    private static T? Attempt<T>(Func<T?> action) where T : class
    {
        try { return action(); }
        catch (MetadataException refusal) when (Defined(refusal)) { return null; }
    }

    private static byte[]? AttemptBytes(Func<byte[]?> action) => Attempt(action);

    private static void Try(Action action)
    {
        try { action(); }
        catch (MetadataException refusal) when (Defined(refusal)) { }
    }

    // Touch everything the snapshot offers; with the limits it was read under, hold it to them. The
    // PSD and JPEG 2000 readers keep fixed budgets of their own, so only the ICC profile and the
    // comments, read by their own readers, are held to the limits there.
    private static void Check(ImageMetadata metadata, MetadataLimits? limits)
    {
        Touch(metadata);
        bool own = metadata.Format is MetadataFormat.Psd or MetadataFormat.Jp2;
        if (!own && metadata.Xmp is { } xmp && xmp.ByteLength > XmpCeiling)
            throw Broken($"An XMP packet of {xmp.ByteLength} bytes exceeds the packet budget of {XmpCeiling}.");
        if (!own && metadata.Iptc is { } iptc && iptc.ByteLength > IptcCeiling)
            throw Broken($"An IPTC stream of {iptc.ByteLength} bytes exceeds the packet budget of {IptcCeiling}.");
        if (limits is null) return;
        int iccCeiling = Math.Min(limits.MaxValueBytes, limits.MaxRetainedMetadataBytes);
        if (metadata.IccProfile.Length > iccCeiling)
            throw Broken($"An ICC profile of {metadata.IccProfile.Length} bytes exceeds the budget of {iccCeiling}.");
        CheckComments(metadata.Comments, limits);
        if (own || metadata.Exif is not { } exif) return;
        foreach (ExifEntry entry in exif.Entries) Within(entry.Value, limits, "EXIF " + entry.Key);
        foreach (ExifSubImage image in exif.SubImages)
            foreach (ExifEntry entry in image.Entries) Within(entry.Value, limits, "sub-image " + image.Index + " " + entry.Key);
        if (exif.MakerNote is { } note)
            foreach (MakerNoteEntry entry in note.Entries) Within(entry.Value, limits, "MakerNote tag " + entry.TagId);
        // A RAF file joins the EXIF of its JPEG and of its TIFF, a CRW file builds EXIF from its
        // records and a CR3 file reads a block per directory: each block has the budget to itself.
        bool joined = metadata.Format is MetadataFormat.Raf or MetadataFormat.Crw;
        if (!joined)
        {
            foreach (IGrouping<ExifDirectoryKind, ExifEntry> directory in exif.Entries.GroupBy(entry => entry.Key.Directory))
                if (directory.Count() > limits.MaxEntriesPerDirectory)
                    throw Broken($"The {directory.Key} directory holds {directory.Count()} entries; MaxEntriesPerDirectory is {limits.MaxEntriesPerDirectory}.");
            foreach (ExifSubImage image in exif.SubImages)
                if (image.Entries.Count > limits.MaxEntriesPerDirectory)
                    throw Broken($"Sub-image {image.Index} holds {image.Entries.Count} entries; MaxEntriesPerDirectory is {limits.MaxEntriesPerDirectory}.");
        }
        int total = exif.Entries.Count + exif.SubImages.Sum(image => image.Entries.Count);
        if (!joined && metadata.Format != MetadataFormat.Cr3 && total > limits.MaxTotalEntries)
            throw Broken($"The EXIF block holds {total} entries; MaxTotalEntries is {limits.MaxTotalEntries}.");
    }

    private static void Within(ExifValue value, MetadataLimits limits, string where)
    {
        if (value.ByteLength > limits.MaxValueBytes)
            throw Broken($"{where} holds a value of {value.ByteLength} bytes; MaxValueBytes is {limits.MaxValueBytes}.");
    }

    // Documented: zero bytes that end a comment are dropped and an empty comment is not listed; one
    // comment is bounded by MaxValueBytes and all of them by MaxRetainedMetadataBytes. Decoding gives
    // at most one character per byte, so the bounds hold for the characters too.
    private static void CheckComments(IReadOnlyList<string> comments, MetadataLimits limits)
    {
        long total = 0;
        foreach (string comment in comments)
        {
            if (comment.Length == 0 || comment[^1] == '\0')
                throw Broken("A comment is listed empty or with the zero bytes that end it.");
            if (comment.Length > limits.MaxValueBytes)
                throw Broken($"A comment of {comment.Length} characters exceeds MaxValueBytes {limits.MaxValueBytes}.");
            total += comment.Length;
        }
        if (total > limits.MaxRetainedMetadataBytes)
            throw Broken($"Comments of {total} characters exceed MaxRetainedMetadataBytes {limits.MaxRetainedMetadataBytes}.");
    }

    private static void Touch(ImageMetadata metadata)
    {
        _ = metadata.MimeType;
        if (metadata.Exif is { } exif) Touch(exif);
        if (metadata.Iptc is { } iptc) Touch(iptc);
        if (metadata.Xmp is { } xmp) Touch(xmp);
        foreach (PreviewImage preview in metadata.Previews) _ = preview.ToString();
        if (metadata.Video is { } video)
            foreach (VideoProperty property in video.Properties)
                _ = property.Key.Length + property.Value.Length + (property.Items?.Count ?? 0);
        foreach (MetadataReadError error in metadata.Errors)
        {
            if (!Defined(error.Exception))
                throw Broken("A read error carries an undefined code: " + (int)error.Code);
            _ = error.ToString();
        }
        Try(() => metadata.Reconcile());
    }

    private static void Touch(EmbeddedImageMetadata? embedded)
    {
        if (embedded is null) return;
        _ = embedded.Source.ToString();
        if (embedded.Exif is { } exif) Touch(exif);
        if (embedded.Iptc is { } iptc) Touch(iptc);
        if (embedded.Xmp is { } xmp) Touch(xmp);
        foreach (string comment in embedded.Comments) _ = comment.Length;
    }

    private static void Touch(ExifMetadata exif)
    {
        foreach (ExifEntry entry in exif.Entries)
        {
            Touch(entry.Value);
            Try(() => ExifTags.Interpret(exif, entry.Key));
            Try(() => ExifTags.Interpret(entry.Key, entry.Value));
            _ = ExifTags.Find(entry.Key)?.ToString();
        }
        foreach (ExifSubImage image in exif.SubImages)
            foreach (ExifEntry entry in image.Entries)
            {
                Touch(entry.Value);
                Try(() => ExifTags.Interpret(entry.Key, entry.Value));
            }
        TouchProperties(exif);
        if (exif.MakerNote is { } note) Touch(note, exif, exif.Model);
    }

    private static void Touch(MakerNote note, ExifMetadata? image, string? model)
    {
        _ = note.TryGetEntry(0x0001, out _);
        foreach (MakerNoteEntry entry in note.Entries)
        {
            Touch(entry.Value);
            _ = note.GetKey(entry);
            _ = note.Interpret(entry);
            _ = note.InterpretForModel(entry, model);
            if (image is not null) _ = note.InterpretForImage(entry, image);
            foreach (MakerNoteField field in entry.Fields)
            {
                Touch(field.Value);
                _ = note.GetKey(entry, field);
                _ = note.Interpret(entry, field);
                _ = note.InterpretFieldForModel(entry, field, model);
                if (image is not null) _ = note.InterpretFieldForImage(entry, field, image);
                foreach (MakerNoteField element in field.Fields)
                {
                    TouchDeep(element, 0);
                    _ = note.GetKey(entry, field, element);
                }
            }
        }
    }

    private static void TouchDeep(MakerNoteField field, int depth)
    {
        Touch(field.Value);
        if (depth < 8)
            foreach (MakerNoteField inner in field.Fields) TouchDeep(inner, depth + 1);
    }

    private static void Touch(ExifValue value)
    {
        _ = Raw(value);
        _ = value.TryGetString(out _);
        _ = value.TryGetStringArray(out _);
        _ = value.TryGetByteArray(out _);
        _ = value.TryGetSByteArray(out _);
        _ = value.TryGetUInt16Array(out _);
        _ = value.TryGetUInt32Array(out _);
        _ = value.TryGetInt16Array(out _);
        _ = value.TryGetInt32Array(out _);
        _ = value.TryGetInt64Array(out _);
        _ = value.TryGetRationalArray(out _);
        _ = value.TryGetSignedRationalArray(out _);
        _ = value.TryGetSingleArray(out _);
        _ = value.TryGetDoubleArray(out _);
        _ = value.TryGetUInt32(out _);
        _ = value.TryGetInt32(out _);
        _ = value.TryGetInt64(out _);
        _ = value.TryGetRational(out _);
        _ = value.TryGetSignedRational(out _);
        _ = value.TryGetDouble(out _);
    }

    private static void Touch(IptcPacket packet)
    {
        for (int i = 0; i < packet.Datasets.Count; i++)
        {
            _ = Raw(packet.Datasets[i].Value);
            int index = i;
            Try(() => packet.GetText(index));
        }
        TouchProperties(packet);
    }

    private static void Touch(XmpPacket packet)
    {
        foreach (XmpEntry entry in packet.Entries)
        {
            _ = entry.Key;
            foreach (string value in entry.Values) _ = value.Length;
            foreach ((string language, string text) in entry.Languages) _ = language.Length + text.Length;
        }
        TouchProperties(packet);
    }

    // The derived properties of a model object (dates, lens, keywords, titles...), found by reflection
    // so that a property added later is fuzzed too. None of them documents an exception.
    private static void TouchProperties(object target)
    {
        PropertyInfo[] properties = PropertyCache.GetOrAdd(target.GetType(), type => type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0 && !property.PropertyType.IsByRefLike && property.GetMethod is not null)
            .ToArray());
        foreach (PropertyInfo property in properties)
        {
            object? value;
            try { value = property.GetValue(target); }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is MetadataException refusal && Defined(refusal)) { continue; }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } inner)
            {
                ExceptionDispatchInfo.Throw(inner);
                throw;
            }
            if (value is IEnumerable items and not string)
            {
                foreach (object? item in items) _ = item?.ToString();
            }
            else
            {
                _ = value?.ToString();
            }
        }
    }

    private static byte[] Raw(ExifValue value)
    {
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return bytes;
    }

    private static byte[] Raw(IptcValue value)
    {
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return bytes;
    }

    private static byte[] Raw(IptcPacket packet)
    {
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        return bytes;
    }

    private static byte[] Raw(XmpPacket packet)
    {
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        return bytes;
    }

    // ---- canonical rendering for the stream comparison -------------------------------------------------

    private static string Render(ImageMetadata file)
    {
        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append('\n');
        Line("FORMAT " + file.Format);
        if (file.Exif is { } exif)
        {
            Line("EXIF " + exif.ByteOrder + " " + exif.IsEditable + " " + exif.Entries.Count + " " + exif.SubImages.Count);
            foreach (ExifEntry entry in exif.Entries)
                Line("ENTRY " + entry.Key + " " + Render(entry.Value));
            foreach (ExifSubImage image in exif.SubImages)
                foreach (ExifEntry entry in image.Entries)
                    Line("SUBENTRY " + image.Kind + " " + image.Index + " " + entry.Key + " " + Render(entry.Value));
            if (exif.MakerNote is { } note)
            {
                Line("MAKERNOTE " + note.Format + " " + note.Offset + " " + note.ByteOrder + " " + note.Entries.Count);
                foreach (MakerNoteEntry entry in note.Entries)
                {
                    Line("MNENTRY " + entry.TagId + " " + Render(entry.Value));
                    foreach (MakerNoteField field in entry.Fields)
                        Line("MNFIELD " + entry.TagId + " " + field.Index + " " + Render(field.Value));
                }
            }
        }
        if (file.Iptc is { } iptc)
        {
            Line("IPTC " + iptc.IsEditable + " " + iptc.Digest + " " + Convert.ToBase64String(Raw(iptc)));
        }
        if (file.Xmp is { } xmp)
            Line("XMP " + xmp.IsEditable + " " + Convert.ToBase64String(Raw(xmp)));
        Line("ICC " + Convert.ToBase64String(file.IccProfile.Span));
        foreach (string comment in file.Comments) Line("COMMENT " + comment);
        foreach (PreviewImage preview in file.Previews) Line("PREVIEW " + preview);
        if (file.Video is { } video)
            foreach (VideoProperty property in video.Properties)
                Line("VIDEO " + property.Key + "=" + property.Value + " " + string.Join("|", property.Items ?? Array.Empty<string>()));
        foreach (MetadataReadError error in file.Errors) Line("ERROR " + error.Family + " " + error.Code);
        return text.ToString();
    }

    private static string Render(ExifValue value) =>
        value.FieldTypeCode + " " + value.Count + " " + value.ByteOrder + " " + Convert.ToHexString(Raw(value));

    /// <summary>A read-only stream that cannot seek and returns at most 4093 bytes a call.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly byte[] data;
        private int position;

        public ForwardOnlyStream(byte[] data) => this.data = data;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = Math.Min(Math.Min(count, 4093), data.Length - position);
            Array.Copy(data, position, buffer, offset, read);
            position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
