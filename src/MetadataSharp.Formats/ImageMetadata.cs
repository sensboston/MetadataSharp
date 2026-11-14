using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Cr3;
using MetadataSharp.Formats.Exv;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Jp2;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Jxl;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Psd;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.WebP;
// The two static readers share their names with properties of the snapshot.
using CommentReader = MetadataSharp.Formats.Comments;
using PreviewReader = MetadataSharp.Formats.Previews;

namespace MetadataSharp.Formats;

/// <summary>
/// Everything the library reads from one file of any container it knows, read once and not changed
/// afterwards: the format the signature tells, the EXIF with its MakerNote, the IPTC, the XMP, the
/// ICC profile, the comments and the list of previews. It is the one entry point for reading; the
/// readers of each container stay available, and they are what edits a file.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is parsed here: each part is what the reader of that container and that kind returns,
/// under the rules that reader documents. The kinds are read one by one. What the reader of a kind
/// refuses is listed in <see cref="Errors"/> and the part is absent; the other kinds are read all
/// the same, so a broken XMP packet does not hide the EXIF of the file.
/// </para>
/// <para>
/// A read takes the whole file into memory, within <see cref="MetadataLimits.MaxInputBytes"/>.
/// Without limits each reader applies its own documented defaults, exactly as when it is called
/// directly; given limits are handed to every reader that takes <see cref="MetadataLimits"/>. The
/// XMP and IPTC readers keep the defaults of their own limit types, and the PSD and JPEG 2000
/// readers their fixed ones.
/// </para>
/// </remarks>
public sealed class ImageMetadata
{
    /// <summary>
    /// How many bytes from the start of a file <see cref="Detect(ReadOnlySpan{byte})"/> looks at, at most. A shorter
    /// header is not an error: it gives <see cref="MetadataFormat.Unknown"/> where the signature
    /// does not fit in it.
    /// </summary>
    public const int HeaderLength = 16;

    private static readonly MetadataFamily[] ExifIptcXmp = [MetadataFamily.Exif, MetadataFamily.IptcIim, MetadataFamily.Xmp];
    private static readonly MetadataFamily[] ExifXmp = [MetadataFamily.Exif, MetadataFamily.Xmp];
    private static readonly MetadataFamily[] XmpOnly = [MetadataFamily.Xmp];

    private ImageMetadata(MetadataFormat format, Parts parts, ReadOnlyMemory<byte> iccProfile,
        IReadOnlyList<string> comments, IReadOnlyList<PreviewImage> previews)
    {
        Format = format;
        Exif = parts.Exif;
        Iptc = parts.Iptc;
        Xmp = parts.Xmp;
        IccProfile = iccProfile;
        Comments = Array.AsReadOnly(comments.ToArray());
        Previews = Array.AsReadOnly(previews.ToArray());
        Video = parts.Video;
        // A stable sort: the errors of one kind keep the order they were met in.
        Errors = Array.AsReadOnly(parts.Errors.OrderBy(error => error.Family).ToArray());
    }

    /// <summary>The format the signature of the file tells.</summary>
    public MetadataFormat Format { get; }

    /// <summary>
    /// The EXIF metadata, with the decoded MakerNote in <see cref="ExifMetadata.MakerNote"/>, or
    /// null when the file has none, the format carries none, or its reader refused it.
    /// </summary>
    public ExifMetadata? Exif { get; }

    /// <summary>The IPTC record, or null when the file has none, the format carries none, or its reader refused it.</summary>
    public IptcPacket? Iptc { get; }

    /// <summary>The XMP packet, or null when the file has none, the format carries none, or its reader refused it.</summary>
    public XmpPacket? Xmp { get; }

    /// <summary>
    /// The bytes of the embedded ICC profile, as <c>IccProfiles.Read</c> returns them; empty when
    /// the file has none, the format carries none, or its reader refused it.
    /// </summary>
    public ReadOnlyMemory<byte> IccProfile { get; }

    /// <summary>The comments of the file in its order, as <c>Comments.Read</c> lists them; empty when there are none.</summary>
    public IReadOnlyList<string> Comments { get; }

    /// <summary>
    /// The previews of the file, as <c>Previews.List</c> lists them. Each names a range of the
    /// file: <c>Previews.Extract</c> returns its bytes from the same file.
    /// </summary>
    public IReadOnlyList<PreviewImage> Previews { get; }

    /// <summary>Container-derived video and audio properties, or null for a non-video format.</summary>
    public VideoMetadata? Video { get; }

    /// <summary>
    /// The kinds of metadata that were not read, in the order of <see cref="MetadataFamily"/>; empty
    /// when every reader accepted the file. A kind listed here is absent above because it was
    /// refused, not because the file lacks it. When the container itself is refused, the one
    /// exception is listed under each kind its reader gives: EXIF, IPTC and XMP then share the same
    /// <see cref="MetadataReadError.Exception"/> object.
    /// </summary>
    public IReadOnlyList<MetadataReadError> Errors { get; }

    /// <summary>
    /// Reconciles the EXIF, IPTC and XMP of the file as the Metadata Working Group guidelines read
    /// them: <see cref="MetadataSync.Reconcile"/> on <see cref="Exif"/>, <see cref="Iptc"/> and
    /// <see cref="Xmp"/>, with what the IPTC digest of the file says (<see cref="IptcPacket.Digest"/>).
    /// Nothing is written: the result holds the edits that bring the three into agreement, for the
    /// <c>SaveAll</c> of the document of the format, or for its separate saves.
    /// </summary>
    /// <param name="options">Whether blocks the file lacks are created, and whether all of EXIF is mirrored.</param>
    /// <exception cref="MetadataException">
    /// EXIF, IPTC or XMP is listed in <see cref="Errors"/>: a kind that was refused is not known to
    /// be absent, and edits made without it could replace what the file holds. The refusal is thrown as it was raised.
    /// </exception>
    /// <returns>The combined EXIF, IPTC, and XMP edits produced by the requested reconciliation.</returns>
    public SyncEdits Reconcile(ReconcileOptions? options = null)
    {
        foreach (MetadataReadError error in Errors)
        {
            if (error.Family is MetadataFamily.Exif or MetadataFamily.IptcIim or MetadataFamily.Xmp) throw error.Exception;
        }
        return MetadataSync.Reconcile(Exif, Iptc, Xmp, null, options);
    }

    /// <summary>
    /// Tells the format by the signature at the start of a file, never by its name. The first
    /// <see cref="HeaderLength"/> bytes are enough; more may be given, and the whole file may.
    /// </summary>
    /// <returns>The format, or <see cref="MetadataFormat.Unknown"/>. The method does not throw.</returns>
    /// <param name="header">Leading file bytes starting at offset zero, used to identify the container format.</param>
    public static MetadataFormat Detect(ReadOnlySpan<byte> header)
    {
        if (At(header, 0, [0xFF, 0xD8])) return MetadataFormat.Jpeg;
        if (At(header, 0, [0x89, 0x50, 0x4E, 0x47])) return MetadataFormat.Png;
        if (At(header, 0, "RIFF"u8) && At(header, 8, "WEBP"u8)) return MetadataFormat.WebP;
        if (VideoMetadata.TryDetect(header, out MetadataFormat videoFormat)) return videoFormat;
        if (At(header, 0, "II*\0"u8) || At(header, 0, "MM\0*"u8)) return MetadataFormat.Tiff;
        if (At(header, 0, "IIRC"u8) || At(header, 0, "MMCR"u8)) return MetadataFormat.Dcp;
        // ORF ("IIRO", "MMOR") and RW2 ("IIU\0") are TIFF structures under their own identifier.
        if (At(header, 0, "IIRO"u8) || At(header, 0, "MMOR"u8) || At(header, 0, "IIU\0"u8)) return MetadataFormat.Tiff;
        if (At(header, 0, "8BPS"u8)) return MetadataFormat.Psd;
        if (Crw.CrwReader.HasSignature(header)) return MetadataFormat.Crw;
        if (Mrw.MrwMetadata.HasSignature(header)) return MetadataFormat.Mrw;
        if (Raf.RafMetadata.HasSignature(header)) return MetadataFormat.Raf;
        if (At(header, 0, [0xFF, 0x01, 0x45, 0x78, 0x69, 0x76, 0x32])) return MetadataFormat.Exv;
        if (At(header, 4, "jP  "u8)) return MetadataFormat.Jp2;
        if (IsSidecar(header)) return MetadataFormat.XmpSidecar;
        // CR3 is a BMFF file of the major brand "crx " that keeps its metadata outside a meta box.
        if (At(header, 4, "ftyp"u8)) return At(header, 8, "crx "u8) ? MetadataFormat.Cr3 : MetadataFormat.Bmff;
        // JPEG XL: the signature box of a container (its file type box comes second), or the two
        // bytes a bare codestream starts with.
        if (At(header, 0, [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A]) || At(header, 0, [0xFF, 0x0A]))
            return MetadataFormat.Jxl;
        // GIF, BMP, PGF and EPS are told by their first bytes; a TARGA file has no signature at its
        // start, so it is told by the footer at its end when the whole file is given.
        if (SmallFormats.GifMetadata.HasSignature(header)) return MetadataFormat.Gif;
        if (SmallFormats.BmpMetadata.HasSignature(header)) return MetadataFormat.Bmp;
        if (Pgf.PgfMetadata.HasSignature(header)) return MetadataFormat.Pgf;
        if (Eps.EpsMetadata.HasSignature(header)) return MetadataFormat.Eps;
        if (SmallFormats.TgaMetadata.HasSignature(header)) return MetadataFormat.Tga;
        return MetadataFormat.Unknown;
    }

    /// <summary>
    /// Tells the format of a file at a path. Signatures take precedence; when none identifies the
    /// file, a <c>.tga</c> or <c>.TGA</c> suffix tells TARGA as it does for the reference tool.
    /// </summary>
    /// <param name="path">Path of the supported image format file to identify or open.</param>
    /// <returns>The detected format, identified by file signature or path suffix.</returns>
    /// <exception cref="ArgumentException">The path is null or empty.</exception>
    public static MetadataFormat Detect(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var file = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] head = new byte[HeaderLength];
        int read = 0;
        while (read < head.Length)
        {
            int count = file.Read(head.AsSpan(read));
            if (count == 0) break;
            read += count;
        }
        MetadataFormat format = Detect(head.AsSpan(0, read));
        if (format == MetadataFormat.Unknown && file.Length >= 26)
        {
            byte[] tail = new byte[26];
            file.Position = file.Length - tail.Length;
            int tailRead = 0;
            while (tailRead < tail.Length)
            {
                int count = file.Read(tail.AsSpan(tailRead));
                if (count == 0) break;
                tailRead += count;
            }
            format = Detect(tail.AsSpan(0, tailRead));
        }
        return format == MetadataFormat.Unknown && HasTgaName(path) ? MetadataFormat.Tga : format;
    }

    private static bool HasTgaName(string path) =>
        string.Equals(Path.GetExtension(path), ".tga", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads every kind of metadata of one complete file held in memory.</summary>
    /// <param name="data">The whole file.</param>
    /// <param name="limits">The budgets handed to the readers, or null for the defaults of each reader.</param>
    /// <returns>
    /// The snapshot. A file of an unknown format, or one its readers refuse, gives a snapshot too:
    /// see <see cref="Format"/> and <see cref="Errors"/>.
    /// </returns>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the file is longer than
    /// <see cref="MetadataLimits.MaxInputBytes"/>. Nothing else about the content of a file throws.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
    public static ImageMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null)
    {
        limits?.Validate();
        if (data.Length > Ceiling(limits)) throw TooLarge();
        return Compose(data, null, limits);
    }

    /// <summary>
    /// Reads every kind of metadata of a file from a stream: from its position to its end, into
    /// memory. The stream need not seek, and it is left open.
    /// </summary>
    /// <param name="source">A readable stream positioned at the start of the file.</param>
    /// <param name="limits">The budgets handed to the readers, or null for the defaults of each reader.</param>
    /// <returns>The snapshot, as for the read of a file held in memory.</returns>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the stream holds more than
    /// <see cref="MetadataLimits.MaxInputBytes"/>; nothing past that budget is read.
    /// </exception>
    /// <exception cref="ArgumentException">The stream cannot be read.</exception>
    public static ImageMetadata Read(Stream source, MetadataLimits? limits = null)
    {
        Check(source, limits);
        return Compose(Drain(source, limits, false, default).GetAwaiter().GetResult(), limits);
    }

    /// <summary>
    /// Reads every kind of metadata of a file from a stream without blocking on it: from its
    /// position to its end, into memory. The stream need not seek, and it is left open.
    /// </summary>
    /// <param name="source">A readable stream positioned at the start of the file.</param>
    /// <param name="limits">The budgets handed to the readers, or null for the defaults of each reader.</param>
    /// <param name="cancellationToken">Ends the read of the stream.</param>
    /// <returns>The snapshot, as for the read of a file held in memory.</returns>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the stream holds more than
    /// <see cref="MetadataLimits.MaxInputBytes"/>; nothing past that budget is read.
    /// </exception>
    /// <exception cref="ArgumentException">The stream cannot be read.</exception>
    public static Task<ImageMetadata> ReadAsync(Stream source, MetadataLimits? limits = null, CancellationToken cancellationToken = default)
    {
        // The arguments are checked before the task starts, so a wrong call fails at the call.
        Check(source, limits);
        return DrainAndCompose(source, limits, cancellationToken);
    }

    private static async Task<ImageMetadata> DrainAndCompose(Stream source, MetadataLimits? limits, CancellationToken token) =>
        Compose(await Drain(source, limits, true, token).ConfigureAwait(false), limits);

    /// <summary>Reads every kind of metadata of the file at a path. The file is read into memory and closed.</summary>
    /// <param name="path">The path of the file. A <c>.tga</c> or <c>.TGA</c> suffix identifies TARGA when no signature identifies a format.</param>
    /// <param name="limits">The budgets handed to the readers, or null for the defaults of each reader.</param>
    /// <returns>The snapshot, as for the read of a file held in memory.</returns>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the file is longer than
    /// <see cref="MetadataLimits.MaxInputBytes"/>.
    /// </exception>
    public static ImageMetadata Read(string path, MetadataLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        limits?.Validate();
        using var file = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read,
            (limits ?? new MetadataLimits()).CopyBufferBytes, FileOptions.SequentialScan);
        return Compose(Drain(file, limits, false, default).GetAwaiter().GetResult(), limits, path);
    }

    /// <summary>
    /// Reads every kind of metadata of the file at a path without blocking on it. The file is read
    /// into memory and closed.
    /// </summary>
    /// <param name="path">The path of the file. A <c>.tga</c> or <c>.TGA</c> suffix identifies TARGA when no signature identifies a format.</param>
    /// <param name="limits">The budgets handed to the readers, or null for the defaults of each reader.</param>
    /// <param name="cancellationToken">Ends the read of the file.</param>
    /// <returns>The snapshot, as for the read of a file held in memory.</returns>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the file is longer than
    /// <see cref="MetadataLimits.MaxInputBytes"/>.
    /// </exception>
    /// <exception cref="ArgumentException">The path is null or empty.</exception>
    public static Task<ImageMetadata> ReadAsync(string path, MetadataLimits? limits = null, CancellationToken cancellationToken = default)
    {
        // As for a stream, a wrong call fails at the call. The file is opened and read in the task,
        // so a file that is not there fails the task.
        ArgumentException.ThrowIfNullOrEmpty(path);
        limits?.Validate();
        return OpenAndCompose(path, limits, cancellationToken);
    }

    // Open and drain the path asynchronously before composing its metadata snapshot.
    private static async Task<ImageMetadata> OpenAndCompose(string path, MetadataLimits? limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var file = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read,
            (limits ?? new MetadataLimits()).CopyBufferBytes, FileOptions.SequentialScan | FileOptions.Asynchronous);
        try { return Compose(await Drain(file, limits, true, token).ConfigureAwait(false), limits, path); }
        finally { await file.DisposeAsync().ConfigureAwait(false); }
    }

    // ---- the signature ----

    // Match a container signature at the requested header offset.
    private static bool At(ReadOnlySpan<byte> header, int offset, ReadOnlySpan<byte> bytes) =>
        header.Length - offset >= bytes.Length && header.Slice(offset, bytes.Length).SequenceEqual(bytes);

    private static bool IsSidecar(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> text = At(header, 0, [0xEF, 0xBB, 0xBF]) ? header[3..] : header;
        return At(text, 0, "<?xpacket"u8) || At(text, 0, "<?xml"u8) || At(text, 0, "<x:xmpmeta"u8) || At(text, 0, "<rdf:RDF"u8);
    }

    // ---- the bytes ----

    // Validate stream readability and input-size limits before starting a metadata read.
    private static void Check(Stream source, MetadataLimits? limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        limits?.Validate();
    }

    /// <summary>The longest file a read takes: the input budget, and no more than one array holds.</summary>
    private static long Ceiling(MetadataLimits? limits) =>
        Math.Min((limits ?? new MetadataLimits()).MaxInputBytes, Array.MaxLength);

    private static MetadataException TooLarge() =>
        new(MetadataErrorCode.LimitExceeded, "The file exceeds the input budget.", 0);

    /// <summary>The rest of a stream, read in pieces and never past the input budget.</summary>
    private static async Task<byte[]> Drain(Stream source, MetadataLimits? limits, bool asynchronous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        long ceiling = Ceiling(limits);
        if (source.CanSeek)
        {
            long length = source.Length - source.Position;
            if (length > ceiling) throw TooLarge();
            if (length > 0)
            {
                // The length is known: one array, filled exactly. A stream that ends short of the
                // length it gave is cut there, as one that cannot seek would be.
                byte[] bytes = new byte[(int)length];
                int filled = 0;
                while (filled < bytes.Length)
                {
                    token.ThrowIfCancellationRequested();
                    int count = asynchronous
                        ? await source.ReadAsync(bytes.AsMemory(filled), token).ConfigureAwait(false)
                        : source.Read(bytes, filled, bytes.Length - filled);
                    if (count == 0) break;
                    filled += count;
                }
                return filled == bytes.Length ? bytes : bytes.AsSpan(0, filled).ToArray();
            }
        }
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[(limits ?? new MetadataLimits()).CopyBufferBytes];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = asynchronous
                ? await source.ReadAsync(chunk, token).ConfigureAwait(false)
                : source.Read(chunk, 0, chunk.Length);
            if (count == 0) break;
            if (count > ceiling - buffer.Length) throw TooLarge();
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    // ---- the composition ----

    /// <summary>What the readers of the container gave, and what they refused.</summary>
    private sealed class Parts
    {
        internal ExifMetadata? Exif;
        internal IptcPacket? Iptc;
        internal XmpPacket? Xmp;
        internal IReadOnlyList<string>? Comments;
        internal VideoMetadata? Video;
        internal readonly List<MetadataReadError> Errors = [];

        /// <summary>Lists one refusal under each of the kinds it kept from being read.</summary>
        internal void Refuse(MetadataException refusal, params MetadataFamily[] kinds)
        {
            foreach (MetadataFamily kind in kinds) Errors.Add(new MetadataReadError(kind, refusal));
        }

        /// <summary>Lists what a reader of several kinds refused kind by kind.</summary>
        internal void Refuse(KindRefusals refusals)
        {
            if (refusals.Exif is MetadataException exif) Refuse(exif, MetadataFamily.Exif);
            if (refusals.Iptc is MetadataException iptc) Refuse(iptc, MetadataFamily.IptcIim);
            if (refusals.Xmp is MetadataException xmp) Refuse(xmp, MetadataFamily.Xmp);
        }
    }

    private static ImageMetadata Compose(byte[] file, MetadataLimits? limits, string? path = null) => Compose(file, file, limits, path);

    /// <summary>
    /// Reads the kinds one by one. <paramref name="owned"/> is the same file as an array when the
    /// caller has one, for the readers that take a stream; they get a copy otherwise.
    /// </summary>
    private static ImageMetadata Compose(ReadOnlySpan<byte> data, byte[]? owned, MetadataLimits? limits, string? path = null)
    {
        MetadataFormat format = Detect(data);
        if (format == MetadataFormat.Unknown && path is not null && HasTgaName(path)) format = MetadataFormat.Tga;
        var parts = new Parts();
        switch (format)
        {
            case MetadataFormat.Jpeg: ReadJpeg(owned ?? data.ToArray(), limits, parts); break;
            case MetadataFormat.Png: ReadPng(owned ?? data.ToArray(), limits, parts); break;
            case MetadataFormat.WebP: ReadWebP(owned ?? data.ToArray(), limits, parts); break;
            case MetadataFormat.Tiff: ReadTiff(data, limits, parts); break;
            case MetadataFormat.Dcp: ReadTiff(data, limits, parts); break;
            case MetadataFormat.Psd: ReadPsd(data, limits, parts); break;
            case MetadataFormat.Exv: ReadExv(data, limits, parts); break;
            case MetadataFormat.Jp2: ReadJp2(data, parts); break;
            case MetadataFormat.XmpSidecar: ReadSidecar(data, parts); break;
            case MetadataFormat.Cr3: ReadCr3(data, limits, parts); break;
            case MetadataFormat.Bmff: ReadBmff(data, limits, parts); break;
            case MetadataFormat.Jxl: ReadJxl(data, limits, parts); break;
            case MetadataFormat.Crw: ReadCrw(data, limits, parts); break;
            case MetadataFormat.Mrw: ReadMrw(data, limits, parts); break;
            case MetadataFormat.Raf: ReadRaf(data, limits, parts); break;
            // A GIF, BMP or TARGA file has no metadata block, so no reader runs for it and the
            // snapshot is empty rather than refused.
            case MetadataFormat.Gif:
            case MetadataFormat.Bmp:
            case MetadataFormat.Tga: break;
            case MetadataFormat.Pgf: ReadPgf(data, limits, parts); break;
            case MetadataFormat.Eps: ReadEps(data, limits, parts); break;
            case MetadataFormat.Asf:
            case MetadataFormat.Matroska:
            case MetadataFormat.QuickTime:
            case MetadataFormat.Riff: ReadVideo(data, format, limits, parts); break;
        }

        // These three readers tell the container themselves and take any bytes: a format the
        // signature does not name gives nothing, and a file its own reader refused may still give
        // its profile, its comments and its previews.
        ReadOnlyMemory<byte> profile = default;
        try { profile = IccProfiles.Read(data, limits); }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Icc); }
        IReadOnlyList<string> comments = [];
        try { comments = CommentReader.Read(data, limits); }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Comment); }
        // A CRW file keeps its comment in a record, and a RAF file has those of its JPEG file.
        if (parts.Comments is not null) comments = parts.Comments;
        IReadOnlyList<PreviewImage> previews = [];
        try { previews = PreviewReader.List(data, limits); }
        catch (MetadataException refusal)
        {
            // The CRW metadata reader already reports a malformed CIFF heap. Its preview loader
            // walks that same heap, so do not report the identical refusal a second time as Preview.
            if (format != MetadataFormat.Crw || !parts.Errors.Any(error => error.Code == refusal.Code))
                parts.Refuse(refusal, MetadataFamily.Preview);
        }
        return new ImageMetadata(format, parts, profile, comments, previews);
    }

    // Read JPEG from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadJpeg(byte[] file, MetadataLimits? limits, Parts parts)
    {
        var refusals = new KindRefusals();
        JpegMetadataDocument document;
        try { document = JpegMetadataDocument.Open(new MemoryStream(file, writable: false), limits, refusals); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); return; }
        using (document)
        {
            parts.Exif = document.Exif;
            parts.Refuse(refusals);
            try { parts.Iptc = document.ReadIptc(); }
            catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.IptcIim); }
            try { parts.Xmp = document.ReadXmp(); }
            catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Xmp); }
        }
    }

    // Read PNG from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadPng(byte[] file, MetadataLimits? limits, Parts parts)
    {
        PngMetadataDocument document;
        try { document = PngMetadataDocument.Open(new MemoryStream(file, writable: false), limits is null ? null : new PngReadOptions { Limits = limits }); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); return; }
        using (document)
        {
            try { parts.Exif = document.ReadExif(); }
            catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Exif); }
            try { parts.Iptc = document.ReadIptc(); }
            catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.IptcIim); }
            try { parts.Xmp = document.ReadXmp(); }
            catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Xmp); }
        }
    }

    // Read web p from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadWebP(byte[] file, MetadataLimits? limits, Parts parts)
    {
        WebPMetadataDocument document;
        try { document = WebPMetadataDocument.Open(new MemoryStream(file, writable: false), limits is null ? null : new WebPReadOptions { Limits = limits }); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifXmp); return; }
        using (document)
        {
            try { parts.Exif = document.ReadExif(); }
            catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Exif); }
            try { parts.Xmp = document.ReadXmp(); }
            catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Xmp); }
        }
    }

    private static void ReadTiff(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        // The packet is the value of a tag of the primary directory: without the directories there
        // is no packet to look for, so their refusal stands for the XMP of the file as well.
        try { parts.Exif = TiffExif.Read(data, limits); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifXmp); }
        // The IPTC reader walks the file on its own.
        try { parts.Iptc = TiffIptc.Read(data, limits); }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.IptcIim); }
        if (parts.Exif is not ExifMetadata exif) return;
        try
        {
            // Tag 700 (XMLPacket), as TiffBasedMetadataDocument.ReadXmp reads it.
            if (exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x02BC), out ExifEntry? entry) && entry.Value.ByteLength != 0)
                parts.Xmp = XmpPacket.Parse(entry.Value.RawBytes);
        }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Xmp); }
    }

    // Read PSD from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadPsd(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        PsdMetadata resources;
        try { resources = PsdMetadata.Read(data); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); return; }
        try { parts.Exif = resources.ReadExif(limits); }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Exif); }
        try { parts.Iptc = resources.ReadIptc(); }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.IptcIim); }
        try { parts.Xmp = resources.ReadXmp(); }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Xmp); }
    }

    // Read exv from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadExv(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        var refusals = new KindRefusals();
        ExvMetadata metadata;
        try { metadata = ExvMetadata.Read(data, limits, refusals); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); return; }
        parts.Exif = metadata.Exif;
        parts.Iptc = metadata.Iptc;
        parts.Xmp = metadata.Xmp;
        parts.Refuse(refusals);
    }

    private static void ReadJp2(ReadOnlySpan<byte> data, Parts parts)
    {
        try
        {
            Jp2Metadata boxes = Jp2Metadata.Read(data);
            parts.Exif = boxes.Exif;
            parts.Iptc = boxes.Iptc;
            parts.Xmp = boxes.Xmp;
            // An XML box holds any XML: the first packet or XML declaration is a fallback when no XMP UUID exists.
            if (parts.Xmp is null)
                for (int i = 0; i < boxes.XmlBoxCount; i++)
                {
                    byte[] box = boxes.GetXmlBox(i);
                    if (!At(box, 0, "<?xpacket"u8) && !At(box, 0, "<?xml"u8)) continue;
                    try { parts.Xmp = XmpPacket.Parse(box); }
                    catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Xmp); }
                    break;
                }
        }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); }
    }

    private static void ReadSidecar(ReadOnlySpan<byte> data, Parts parts)
    {
        try { parts.Xmp = XmpPacket.Parse(data); }
        catch (MetadataException refusal) { parts.Refuse(refusal, XmpOnly); }
    }

    // Read CR3 from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadCr3(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        var refusals = new KindRefusals();
        Cr3Metadata metadata;
        try { metadata = Cr3Reader.Read(data, limits, null, refusals); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); return; }
        parts.Exif = metadata.Exif;
        parts.Iptc = metadata.Iptc;
        parts.Xmp = metadata.Xmp;
        parts.Refuse(refusals);
    }

    // Read BMFF from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadBmff(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        var refusals = new KindRefusals();
        BmffMetadata metadata;
        try { metadata = BmffReader.Read(data, limits, null, refusals); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); return; }
        parts.Exif = metadata.Exif;
        parts.Iptc = metadata.Iptc;
        parts.Xmp = metadata.Xmp;
        parts.Refuse(refusals);
    }

    // Read CRW from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadCrw(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        try
        {
            Crw.CrwMetadata metadata = Crw.CrwMetadata.Read(data, limits);
            parts.Exif = metadata.Exif;
            if (metadata.Comment is string comment) parts.Comments = [comment];
        }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Exif); }
    }

    // Read mrw from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadMrw(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        var refusals = new KindRefusals();
        try
        {
            Mrw.MrwMetadata metadata = Mrw.MrwMetadata.Read(data, limits, refusals);
            parts.Exif = metadata.Exif;
            parts.Iptc = metadata.Iptc;
            parts.Xmp = metadata.Xmp;
            parts.Refuse(refusals);
        }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); }
    }

    // Read RAF from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadRaf(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        var refusals = new KindRefusals();
        try
        {
            Raf.RafMetadata metadata = Raf.RafMetadata.Read(data, limits, refusals);
            parts.Exif = metadata.Exif;
            parts.Iptc = metadata.Iptc;
            parts.Xmp = metadata.Xmp;
            if (metadata.Comments.Count != 0) parts.Comments = metadata.Comments;
            parts.Refuse(refusals);
        }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); }
    }

    // Read JXL from bounded metadata data, validating its declared extent before consuming fields.
    private static void ReadJxl(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        var refusals = new KindRefusals();
        JxlMetadata metadata;
        try { metadata = JxlReader.Read(data, limits, null, refusals); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); return; }
        parts.Exif = metadata.Exif;
        parts.Iptc = metadata.Iptc;
        parts.Xmp = metadata.Xmp;
        parts.Refuse(refusals);
    }

    private static void ReadVideo(ReadOnlySpan<byte> data, MetadataFormat format, MetadataLimits? limits, Parts parts)
    {
        try { parts.Video = VideoMetadata.ReadKnown(data, format, limits ?? new MetadataLimits()); }
        catch (MetadataException refusal) { parts.Refuse(refusal, MetadataFamily.Video); }
    }

    // ---- the small formats ----
    //
    // BMP, GIF and TARGA carry no EXIF, IPTC or XMP block, and the reference reads none of them: there
    // is nothing here for a reader to refuse, so the snapshot of such a file has no parts and no
    // errors. What the format does have is the size its header states, which is not metadata; the
    // readers that give it - BmpMetadata, GifMetadata and TgaMetadata - are called by a consumer that
    // wants it, as the dump tool of this repository is.

    /// <summary>
    /// A PGF file: the EXIF, IPTC and XMP of the image file its header region holds. The reference
    /// hands the region to one reader and takes whatever that reader gives or throws, so a refusal
    /// anywhere in the region - of the carrier's container or of one kind of its metadata - ends the
    /// whole file as refused rather than leaving the other kinds behind.
    /// </summary>
    private static void ReadPgf(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        Pgf.PgfMetadata metadata;
        try { metadata = Pgf.PgfMetadata.Read(data, limits); }
        catch (MetadataException refusal) { parts.Refuse(refusal, ExifIptcXmp); return; }
        parts.Exif = metadata.Exif;
        parts.Iptc = metadata.Iptc;
        parts.Xmp = metadata.Xmp;
    }

    /// <summary>An EPS file: the XMP packet of its DSC comments and nothing else.</summary>
    private static void ReadEps(ReadOnlySpan<byte> data, MetadataLimits? limits, Parts parts)
    {
        var refusals = new KindRefusals();
        Eps.EpsMetadata metadata;
        try { metadata = Eps.EpsMetadata.Read(data, limits, refusals); }
        catch (MetadataException refusal) { parts.Refuse(refusal, XmpOnly); return; }
        parts.Xmp = metadata.Xmp;
        parts.Refuse(refusals);
    }
}
