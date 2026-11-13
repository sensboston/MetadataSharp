using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Jpeg;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// A TIFF-based file opened for editing its EXIF, GPS, IPTC, XMP and ICC profile: TIFF, DNG and the
/// raw formats that are TIFF structures (CR2, NEF, ARW, PEF, ORF, RW2).
/// </summary>
/// <remarks>
/// <para>A save never moves a byte of the source. A value that fits where the old one is, is written
/// there. Whatever grows - a longer value, a directory that gains a tag, a new GPS directory, a new
/// XMP packet, IPTC stream or ICC profile - is appended after the last byte of the file, and the one pointer that
/// leads to it is changed. The bytes it replaces stay in the file, no longer addressed: an edit is
/// not an erasure. Image data, SubIFDs and the MakerNote are never written to and keep their
/// offsets.</para>
/// <para>Every save is planned, applied to a copy in memory, and read back before anything reaches
/// the destination; a save that cannot be shown to have changed only what was asked is refused with
/// a <see cref="MetadataException"/> and writes nothing. An edit that changes nothing writes a copy
/// of the source.</para>
/// <para>The document holds the whole file in memory and is independent of the stream or path it
/// was opened from, so the source cannot change under it. Each save starts from the file as it was
/// opened; to apply a second kind of edit, open the output of the first. It is not safe for
/// concurrent use.</para>
/// </remarks>
public sealed class TiffBasedMetadataDocument : IDisposable, IAsyncDisposable
{
    private const ushort XmpTag = 0x02BC;
    private const ushort IptcTag = 0x83BB;
    private const ushort ResourcesTag = 0x8649;
    private const ushort IccTag = 0x8773;

    private readonly byte[] source;
    private readonly string? sourcePath;
    private readonly TiffReadOptions options;
    private readonly TiffBasedFileMap? map;
    private readonly MetadataException? refusal;
    private readonly TiffLayout? legacyLayout;
    private readonly TiffEditOptions? legacyOptions;
    private readonly Stream? legacyStream;
    private readonly bool ownsLegacyStream;
    private readonly byte[]? legacyDigest;
    private bool disposed;

    private TiffBasedMetadataDocument(byte[] source, string? sourcePath, TiffReadOptions options,
        TiffLayout? legacyLayout = null, TiffEditOptions? legacyOptions = null, Stream? legacyStream = null,
        bool ownsLegacyStream = false, byte[]? legacyDigest = null)
    {
        this.source = source;
        this.sourcePath = sourcePath;
        this.options = options;
        this.legacyLayout = legacyLayout;
        this.legacyOptions = legacyOptions;
        this.legacyStream = legacyStream;
        this.ownsLegacyStream = ownsLegacyStream;
        this.legacyDigest = legacyDigest;
        // A layout the writer cannot account for is still a file to read: the refusal is kept for Save.
        try { map = TiffBasedFileMap.Read(source, options.Limits, options.MaxAccountedRanges); }
        catch (MetadataException failure) { refusal = failure; }
    }

    /// <summary>Reads the whole stream, from its start when it can seek, and leaves it open.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidTiff"/> when the data does not begin with a TIFF, ORF or
    /// RW2 header; <see cref="MetadataErrorCode.LimitExceeded"/> when it exceeds the input budget.
    /// </exception>
    public static TiffBasedMetadataDocument Open(Stream source, TiffReadOptions? options = null) =>
        OpenCore(source, null, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();

    /// <summary>Reads the whole file and closes it.</summary>
    public static TiffBasedMetadataDocument Open(string path, TiffReadOptions? options = null) =>
        OpenPath(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="Open(Stream, TiffReadOptions?)"/>
    public static Task<TiffBasedMetadataDocument> OpenAsync(Stream source, TiffReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        OpenCore(source, null, (options ?? new()).Snapshot(), true, cancellationToken);

    /// <inheritdoc cref="Open(string, TiffReadOptions?)"/>
    public static Task<TiffBasedMetadataDocument> OpenAsync(string path, TiffReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        OpenPath(path, (options ?? new()).Snapshot(), true, cancellationToken);

    // Compatibility entry points used only by the obsolete TiffMetadataDocument facade. They keep
    // the old append-preserve editor's guarantees while storing all document state in this model.
    internal static TiffBasedMetadataDocument OpenLegacy(Stream source, TiffEditOptions options) =>
        OpenLegacyCore(source, null, options.Snapshot(), false, default).GetAwaiter().GetResult();

    internal static TiffBasedMetadataDocument OpenLegacy(string path, TiffEditOptions options) =>
        OpenLegacyPath(path, options.Snapshot(), false, default).GetAwaiter().GetResult();

    internal static Task<TiffBasedMetadataDocument> OpenLegacyAsync(Stream source, TiffEditOptions options, CancellationToken token) =>
        OpenLegacyCore(source, null, options.Snapshot(), true, token);

    internal static Task<TiffBasedMetadataDocument> OpenLegacyAsync(string path, TiffEditOptions options, CancellationToken token) =>
        OpenLegacyPath(path, options.Snapshot(), true, token);

    // Open the source with the selected options, keeping stream ownership and cancellation explicit.
    private static async Task<TiffBasedMetadataDocument> OpenLegacyPath(string path, TiffEditOptions options,
        bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        token.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(path);
        var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, options.ReadOptions.Limits.CopyBufferBytes,
            asynchronous ? FileOptions.Asynchronous : FileOptions.None);
        try { return await OpenLegacyCore(file, full, options, asynchronous, token, ownsStream: true).ConfigureAwait(false); }
        catch { await file.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static Task<TiffBasedMetadataDocument> OpenLegacyCore(Stream source, string? path,
        TiffEditOptions options, bool asynchronous, CancellationToken token, bool ownsStream = false) =>
        OpenCore(source, path, options.ReadOptions, asynchronous, token, options, ownsStream);

    // Open the source with the selected options, keeping stream ownership and cancellation explicit.
    private static async Task<TiffBasedMetadataDocument> OpenPath(string path, TiffReadOptions options,
        bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        token.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(path);
        var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, options.Limits.CopyBufferBytes,
            FileOptions.SequentialScan | (asynchronous ? FileOptions.Asynchronous : FileOptions.None));
        try { return await OpenCore(file, full, options, asynchronous, token).ConfigureAwait(false); }
        finally { await file.DisposeAsync().ConfigureAwait(false); }
    }

    // Open the source with the selected options, keeping stream ownership and cancellation explicit.
    private static async Task<TiffBasedMetadataDocument> OpenCore(Stream source, string? path,
        TiffReadOptions options, bool asynchronous, CancellationToken token,
        TiffEditOptions? legacyOptions = null, bool ownsLegacyStream = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        if (legacyOptions is not null && (!source.CanSeek || source.Position != 0))
            throw new ArgumentException("The TIFF editing source must be seekable and at zero.", nameof(source));
        long ceiling = Math.Min(options.Limits.MaxInputBytes, Array.MaxLength);
        byte[] bytes;
        if (source.CanSeek)
        {
            long length = source.Length;
            if (length > ceiling) throw TooLarge();
            source.Position = 0;
            bytes = new byte[(int)length];
            await ContainerIo.Read(source, bytes, asynchronous, token).ConfigureAwait(false);
        }
        else
        {
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[options.Limits.CopyBufferBytes];
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
            bytes = buffer.ToArray();
        }
        if (!ExifTiffReader.HasFileHeader(bytes))
            throw new MetadataException(MetadataErrorCode.InvalidTiff, "The data does not begin with a TIFF header.", 0);
        token.ThrowIfCancellationRequested();
        TiffLayout? legacyLayout = null;
        byte[]? legacyDigest = null;
        if (legacyOptions is not null)
        {
            using var layoutSource = new MemoryStream(bytes, writable: false);
            legacyLayout = await TiffLayoutReader.Read(layoutSource, legacyOptions.ReadOptions, asynchronous, token).ConfigureAwait(false);
            legacyDigest = SHA256.HashData(bytes);
        }
        return new TiffBasedMetadataDocument(bytes, path, options, legacyLayout, legacyOptions,
            legacyOptions is null ? null : source, ownsLegacyStream, legacyDigest);
    }

    private static MetadataException TooLarge() =>
        new(MetadataErrorCode.LimitExceeded, "The TIFF file exceeds the input budget.", 0);

    /// <summary>The EXIF metadata of the file, as <see cref="TiffExif.Read"/> returns it.</summary>
    public ExifMetadata ReadExif()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return TiffExif.Read(source, options.Limits);
    }

    /// <summary>The IPTC of the file, as <see cref="TiffIptc.Read"/> returns it, or null.</summary>
    public IptcPacket? ReadIptc(IptcReadOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return TiffIptc.Read(source, this.options.Limits, options);
    }

    /// <summary>The XMP packet stored as the value of tag 700 of the primary directory, or null.</summary>
    public XmpPacket? ReadXmp(XmpReadOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ExifMetadata exif = TiffExif.Read(source, this.options.Limits);
        if (!exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, XmpTag), out ExifEntry? entry) || entry.Value.ByteLength == 0)
            return null;
        return XmpPacket.Parse(entry.Value.RawBytes, options);
    }

    // Retain LegacyLayout state required to parse or write TIFF/DNG data safely.
    internal TiffLayout LegacyLayout => legacyLayout ?? throw new InvalidOperationException("This document was not opened through the legacy TIFF editor.");

    internal ExifValue? ReadLegacyValue(TiffFieldKey key) => ReadLegacyValueCore(key, false, default).GetAwaiter().GetResult();

    internal Task<ExifValue?> ReadLegacyValueAsync(TiffFieldKey key, CancellationToken token) => ReadLegacyValueCore(key, true, token);

    // Read legacy value core from bounded TIFF/DNG data, validating its declared extent before consuming fields.
    private async Task<ExifValue?> ReadLegacyValueCore(TiffFieldKey key, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        TiffEditPolicy.Key(key);
        await VerifyLegacySource(asynchronous, token).ConfigureAwait(false);
        TiffLayout layout = LegacyLayout;
        TiffDirectoryInfo directory = layout.Directories.FirstOrDefault(d => d.Offset == key.DirectoryOffset)
            ?? throw new ArgumentException("Key does not identify a reachable source IFD.", nameof(key));
        TiffFieldInfo? field = directory.Fields.FirstOrDefault(f => f.TagId == key.TagId);
        if (field is null) return null;
        if (!TiffEditPolicy.Shape(field, 1, 0, 0))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unsupported descriptive target shape.");
        token.ThrowIfCancellationRequested();
        ExifValue value = ExifValue.FromRaw((TiffFieldType)field.FieldType, field.Count, layout.ByteOrder,
            source.AsSpan(checked((int)field.ValueRange.Offset), checked((int)field.ValueRange.Length)));
        if (!TiffEditPolicy.Value(key.TagId, value))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unsupported descriptive target content.");
        return value;
    }

    internal Task SaveLegacyAsync(Stream destination, TiffMetadataEdit edit, bool asynchronous, CancellationToken token) =>
        SaveLegacyCore(destination, edit, asynchronous, token);

    // Write the prepared TIFF/DNG edit only after validation, preserving unrelated payloads.
    private async Task SaveLegacyCore(Stream destination, TiffMetadataEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (legacyStream is null || legacyOptions is null) throw new InvalidOperationException("Legacy TIFF editing is not configured.");
        ContainerIo.Output(legacyStream, destination);
        TiffAppendPlan? plan = await PrepareLegacy(edit, asynchronous, token).ConfigureAwait(false);
        await EmitLegacy(destination, plan, asynchronous, token).ConfigureAwait(false);
    }

    internal Task SaveLegacyAsAsync(string path, TiffMetadataEdit edit, bool asynchronous, CancellationToken token) =>
        SaveLegacyPath(path, edit, asynchronous, token);

    // Write the prepared TIFF/DNG edit only after validation, preserving unrelated payloads.
    private async Task SaveLegacyPath(string path, TiffMetadataEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (legacyOptions is null) throw new InvalidOperationException("Legacy TIFF editing is not configured.");
        string target = ContainerIo.Target(path, sourcePath);
        TiffAppendPlan? plan = await PrepareLegacy(edit, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Publish(target, legacyOptions.ReadOptions.Limits.CopyBufferBytes, asynchronous, token,
            destination => EmitLegacy(destination, plan, asynchronous, token)).ConfigureAwait(false);
    }

    // Keep PrepareLegacy focused on TIFF/DNG and its format-specific validation contract.
    private async Task<TiffAppendPlan?> PrepareLegacy(TiffMetadataEdit edit, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (legacyOptions is null) throw new InvalidOperationException("Legacy TIFF editing is not configured.");
        Dictionary<TiffFieldKey, ExifValue?> changes = edit.Snapshot(legacyOptions.ReadOptions.Limits.MaxRetainedMetadataBytes);
        await VerifyLegacySource(asynchronous, token).ConfigureAwait(false);
        return await new TiffAppendPlanner(LegacyLayout, legacyOptions,
            (offset, count) => Task.FromResult(source.AsSpan(checked((int)offset), count).ToArray()), token)
            .Create(changes).ConfigureAwait(false);
    }

    // Compare the staged result with the source to confirm unrelated TIFF/DNG data stayed unchanged.
    private async Task VerifyLegacySource(bool asynchronous, CancellationToken token)
    {
        if (legacyStream is null || legacyDigest is null || legacyOptions is null)
            throw new InvalidOperationException("Legacy TIFF source verification is not configured.");
        byte[] digest = await ContainerIo.Hash(legacyStream, LegacyLayout.SourceLength,
            legacyOptions.ReadOptions.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest, legacyDigest)) throw ContainerIo.Changed();
    }

    // Write the prepared TIFF/DNG edit only after validation, preserving unrelated payloads.
    private async Task EmitLegacy(Stream destination, TiffAppendPlan? plan, bool asynchronous, CancellationToken token)
    {
        if (legacyStream is null || legacyOptions is null) throw new InvalidOperationException("Legacy TIFF source is not retained.");
        int buffer = legacyOptions.ReadOptions.Limits.CopyBufferBytes;
        if (plan is null)
        {
            await ContainerIo.Copy(legacyStream, destination, 0, LegacyLayout.SourceLength, buffer, asynchronous, token).ConfigureAwait(false);
            return;
        }
        await ContainerIo.Copy(legacyStream, destination, 0, 4, buffer, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Write(destination, plan.Root, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Copy(legacyStream, destination, 8, LegacyLayout.SourceLength - 8, buffer, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Write(destination, plan.Tail, asynchronous, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the file with the EXIF edit applied to an empty, seekable stream. The edit names tags
    /// of the primary, Exif and GPS directories; a GPS directory the file does not have is created.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the edit needs to append and the file
    /// does not allow it (image data that runs to the end of the file or is addressed past it), when
    /// a directory to copy is reached from more than one pointer, or when the planned output fails
    /// its verification; <see cref="MetadataErrorCode.UnsupportedFeature"/> for BigTIFF, a field type
    /// of unknown width, or removal of all EXIF; <see cref="MetadataErrorCode.InvalidTiff"/> for a
    /// directory whose tags do not ascend; <see cref="MetadataErrorCode.LimitExceeded"/> when the
    /// output would exceed the input budget of the limits, another of their budgets, or the four
    /// gigabytes a TIFF offset addresses; otherwise the code the reader reports for the file.
    /// </exception>
    public void SaveExif(Stream destination, ExifEdit edit) => SaveCore(destination, Edit(edit), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveExif(Stream, ExifEdit)"/>
    public Task SaveExifAsync(Stream destination, ExifEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit), true, cancellationToken);

    /// <summary>
    /// Writes the file with the XMP edit applied: the packet is the value of tag 700 of the primary
    /// directory, created when the file has none. Fails as <see cref="SaveExif"/> does, and with
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a packet outside the editable profile.
    /// </summary>
    public void SaveXmp(Stream destination, XmpEdit edit) => SaveCore(destination, Edit(edit), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveXmp(Stream, XmpEdit)"/>
    public Task SaveXmpAsync(Stream destination, XmpEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit), true, cancellationToken);

    /// <summary>
    /// Writes the file with the IPTC edit applied: the stream is the value of tag 0x83BB of the
    /// primary directory, created when the file has none, and keeps the field type it has. A file
    /// without that tag whose IPTC is resource 0x0404 of its Photoshop image resources (tag 0x8649)
    /// is edited there: every other resource keeps its bytes and its place, and the IPTC digest
    /// (resource 0x0425) is left as it is unless <see cref="IptcWriteOptions.UpdateDigest"/> asks for
    /// it to be recomputed; it is removed with the stream and never added. The resources of a file
    /// whose stream is the value of tag 0x83BB are not touched. Fails as <see cref="SaveExif"/> does.
    /// </summary>
    public void SaveIptc(Stream destination, IptcEdit edit) => SaveCore(destination, Edit(edit, null), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptc(Stream, IptcEdit)"/>
    public Task SaveIptcAsync(Stream destination, IptcEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit, null), true, cancellationToken);
    /// <summary>As <see cref="SaveIptc(Stream, IptcEdit)"/>, with options; null options are the defaults.</summary>
    public void SaveIptc(Stream destination, IptcEdit edit, IptcWriteOptions? options) => SaveCore(destination, Edit(edit, options), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptc(Stream, IptcEdit, IptcWriteOptions)"/>
    public Task SaveIptcAsync(Stream destination, IptcEdit edit, IptcWriteOptions? options, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit, options), true, cancellationToken);

    /// <summary>
    /// Writes a plain TIFF with <paramref name="profile"/> as its ICC profile, or without one when
    /// it is null. The profile is the value of tag 0x8773 of the primary directory, which is where
    /// <see cref="IccProfiles.Read"/> looks for it: the tag is created when the file has none,
    /// written as UNDEFINED unless the file has it as BYTE, and removed for null. As in every save
    /// of this document no byte of the source moves: a profile no longer than the stored one is
    /// written where that one is, a longer one and a new one are appended to the file, and the
    /// bytes of a profile that was replaced or removed stay in the file, no longer addressed. A
    /// file that already holds exactly this profile, and a file without a profile that is to have
    /// none, is copied as it is. The profile is not interpreted; the array is copied before the
    /// method returns.
    /// </summary>
    /// <remarks>
    /// The tag describes the image of its directory. In a DNG and in a raw file the primary
    /// directory is commonly that of a reduced image, and a profile there would say how to show
    /// that one. Such a file is refused, for a profile and for null alike, and nothing is written:
    /// a file with DNGVersion (0xC612) in the primary directory; a file with the header of an ORF
    /// or an RW2 or the signature of a CR2; a file whose primary directory is that of a reduced
    /// image (bit 0 of NewSubfileType, as in a NEF or an ARW); and a file with raw sensor data in
    /// any image directory (a colour filter array, linear raw data or the compression of a raw
    /// format, as in a PEF). The file is judged by what it holds, not by its name, and the name
    /// of the maker alone decides nothing. <see cref="IccProfiles.Read"/> still returns what the
    /// primary directory of such a file holds.
    /// </remarks>
    /// <param name="destination">A writable, seekable, empty stream.</param>
    /// <param name="profile">The whole profile, or null for none.</param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidIccProfile"/> when the profile is shorter than the 128
    /// bytes of a header, its size field is not the number of bytes given, or the <c>acsp</c>
    /// signature is absent; <see cref="MetadataErrorCode.LimitExceeded"/> when it is longer than
    /// <see cref="MetadataLimits.MaxValueBytes"/> or
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/> of the limits;
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the file is a DNG or a raw file, or
    /// its primary directory is that of a reduced image; otherwise as <see cref="SaveExif"/> fails.
    /// </exception>
    public void SaveIccProfile(Stream destination, byte[]? profile) => SaveCore(destination, Icc(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfile(Stream, byte[])"/>
    public Task SaveIccProfileAsync(Stream destination, byte[]? profile, CancellationToken cancellationToken = default) => SaveCore(destination, Icc(profile), true, cancellationToken);

    /// <summary>As <see cref="SaveIccProfile(Stream, byte[])"/>, to a new path; the file appears there complete or not at all.</summary>
    public void SaveIccProfileAs(string destinationPath, byte[]? profile) => SavePath(destinationPath, Icc(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfileAs(string, byte[])"/>
    public Task SaveIccProfileAsAsync(string destinationPath, byte[]? profile, CancellationToken cancellationToken = default) => SavePath(destinationPath, Icc(profile), true, cancellationToken);

    /// <summary>Writes the edited file to a new path; the file appears there complete or not at all.</summary>
    public void SaveExifAs(string destinationPath, ExifEdit edit) => SavePath(destinationPath, Edit(edit), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveExifAs(string, ExifEdit)"/>
    public Task SaveExifAsAsync(string destinationPath, ExifEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit), true, cancellationToken);
    /// <summary>As <see cref="SaveXmp(Stream, XmpEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    public void SaveXmpAs(string destinationPath, XmpEdit edit) => SavePath(destinationPath, Edit(edit), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveXmpAs(string, XmpEdit)"/>
    public Task SaveXmpAsAsync(string destinationPath, XmpEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit), true, cancellationToken);
    /// <summary>As <see cref="SaveIptc(Stream, IptcEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    public void SaveIptcAs(string destinationPath, IptcEdit edit) => SavePath(destinationPath, Edit(edit, null), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptcAs(string, IptcEdit)"/>
    public Task SaveIptcAsAsync(string destinationPath, IptcEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit, null), true, cancellationToken);
    /// <summary>As <see cref="SaveIptcAs(string, IptcEdit)"/>, with options; null options are the defaults.</summary>
    public void SaveIptcAs(string destinationPath, IptcEdit edit, IptcWriteOptions? options) => SavePath(destinationPath, Edit(edit, options), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptcAs(string, IptcEdit, IptcWriteOptions)"/>
    public Task SaveIptcAsAsync(string destinationPath, IptcEdit edit, IptcWriteOptions? options, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit, options), true, cancellationToken);

    /// <summary>
    /// Writes a copy of the file with the EXIF, XMP and IPTC edits of a conversion or a reconciliation
    /// applied in one pass, IPTC with <see cref="SyncEdits.IptcWriteOptions"/>, so that after
    /// <see cref="ImageMetadata.Reconcile"/> the IPTC digest, where the file keeps one, says that IPTC
    /// and XMP agree. Fails as <see cref="SaveExif(Stream, ExifEdit)"/>,
    /// <see cref="SaveXmp(Stream, XmpEdit)"/> and <see cref="SaveIptc(Stream, IptcEdit, IptcWriteOptions?)"/>
    /// do, before anything is written. Edits without a change copy the file.
    /// </summary>
    public void SaveAll(Stream destination, SyncEdits edits) => SaveCore(destination, Edit(edits), false, default).GetAwaiter().GetResult();
    /// <summary>As <see cref="SaveAll(Stream, SyncEdits)"/>, to a new file.</summary>
    public void SaveAllAs(string destinationPath, SyncEdits edits) => SavePath(destinationPath, Edit(edits), false, default).GetAwaiter().GetResult();
    private static Pending Edit(SyncEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        return new(edits.Exif.Snapshot(), edits.Xmp.Snapshot(), edits.Iptc.Capture(), null, edits.IptcWriteOptions.UpdateDigest);
    }

    /// <summary>The changes of one save, captured when the save is called.</summary>
    private sealed record Pending(ExifEdit.EditSnapshot? Exif, XmpEdit? Xmp, IptcEdit? Iptc,
        IccChange? Icc = null, bool UpdateDigest = false);

    /// <summary>The profile the file is to have; null for none.</summary>
    private sealed record IccChange(byte[]? Profile);

    private static Pending Edit(ExifEdit edit) { ArgumentNullException.ThrowIfNull(edit); return new(edit.Snapshot(), null, null); }
    private static Pending Edit(XmpEdit edit) { ArgumentNullException.ThrowIfNull(edit); return new(null, edit.Snapshot(), null); }
    private static Pending Edit(IptcEdit edit, IptcWriteOptions? write) { ArgumentNullException.ThrowIfNull(edit); return new(null, null, edit.Capture(), null, write?.UpdateDigest ?? false); }
    private static Pending Icc(byte[]? profile) => new(null, null, null, new IccChange(profile is null ? null : (byte[])profile.Clone()));

    // Write the prepared TIFF/DNG edit only after validation, preserving unrelated payloads.
    private async Task SaveCore(Stream destination, Pending edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ContainerIo.Output(Stream.Null, destination);
        byte[] output = Prepare(edit, token);
        await ContainerIo.Write(destination, output, asynchronous, token).ConfigureAwait(false);
    }

    // Write the prepared TIFF/DNG edit only after validation, preserving unrelated payloads.
    private async Task SavePath(string destinationPath, Pending edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string target = ContainerIo.Target(destinationPath, sourcePath);
        byte[] output = Prepare(edit, token);
        await ContainerIo.Publish(target, options.Limits.CopyBufferBytes, asynchronous, token,
            file => ContainerIo.Write(file, output, asynchronous, token).AsTask()).ConfigureAwait(false);
    }

    /// <summary>The bytes of the edited file, verified; the source itself when the edit changes nothing.</summary>
    private byte[] Prepare(Pending edit, CancellationToken token)
    {
        bool digestOnly = edit.UpdateDigest && edit.Iptc is { IsEmpty: true } && edit.Icc is null
            && (edit.Exif is null || edit.Exif.IsEmpty) && (edit.Xmp is null || edit.Xmp.IsEmpty);
        if (!digestOnly) return PrepareCore(edit, token);
        // Nothing but the digest is asked for: it is recomputed where the file can be edited, and the
        // file is copied where it cannot, as it is without the option.
        try { return PrepareCore(edit, token); }
        catch (MetadataException) { return source; }
    }

    private byte[] PrepareCore(Pending edit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ExifEdit.EditSnapshot? exif = edit.Exif;
        XmpEdit? xmp = edit.Xmp;
        IptcEdit? iptc = edit.Iptc;
        bool exifEmpty = exif is null || exif.IsEmpty;
        bool xmpEmpty = xmp is null || xmp.IsEmpty;
        bool iptcEmpty = iptc is null || iptc.IsEmpty;
        IccChange? icc = edit.Icc;
        bool refresh = edit.UpdateDigest && iptc is not null && iptcEmpty;
        if (exifEmpty && xmpEmpty && iptcEmpty && icc is null && !refresh) return source;
        if (icc?.Profile is byte[] profile)
        {
            IccProfiles.Exact(profile);
            if (profile.Length > EmbeddedFile.Budget(options.Limits))
                throw EmbeddedFile.Limit("The ICC profile exceeds its budget.");
        }

        // Before the refusal of the map: a raw file is refused for what it is, damaged or not.
        if (icc is not null && TiffBasedIccTarget.Refusal(source, map) is string unsuitable)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, unsuitable);
        if (refusal is not null) throw new MetadataException(refusal.Code, refusal.Message, refusal.SourceOffset);
        TiffBasedFileMap layout = map!;
        MetadataLimits limits = options.Limits;
        // What the reader makes of the source: the output is held against it, so a source the reader
        // refuses cannot be edited.
        ExifMetadata before = ExifTiffReader.InspectFile(source, limits);

        // Tags of the MakerNote first, where they are: the file keeps its length and its layout, and
        // what follows is planned and verified against the file with those tags changed.
        byte[] start = source;
        if (!exifEmpty && exif!.MakerNote.Count != 0)
        {
            MakerNoteWriter.Result noted = MakerNoteWriter.Apply(source, exif.MakerNote, limits, wholeFile: true);
            if (!noted.Output.AsSpan().SequenceEqual(source))
            {
                TiffBasedWriteVerifier.MakerNote(source, layout, noted.Output, noted.Ranges, limits, options.MaxAccountedRanges, wholeFile: true);
                start = noted.Output;
                layout = TiffBasedFileMap.Read(start, limits, options.MaxAccountedRanges);
                before = ExifTiffReader.InspectFile(start, limits);
            }
        }

        var changes = new Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?>();
        if (!exifEmpty) AddExif(exif!, layout, changes);
        if (!xmpEmpty) AddXmp(xmp!, layout, changes);
        if (!iptcEmpty) AddIptc(iptc!, edit.UpdateDigest, layout, limits, changes);
        else if (refresh)
        {
            // The stream stays as it is; a digest that cannot be recomputed is left, as it is without the option.
            try { AddIptc(iptc!, true, layout, limits, changes); }
            catch (MetadataException) { }
        }
        if (icc is not null) AddIcc(icc.Profile, layout, changes);
        if (changes.Count == 0) return start;

        token.ThrowIfCancellationRequested();
        bool makerNoteCopy = exif?.MakerNoteCopy is not null;
        TiffBasedWritePlan plan = TiffBasedWritePlanner.Create(start, layout, changes, limits, allowMakerNoteCopy: makerNoteCopy);
        if (plan.IsEmpty) return start;
        byte[] output = plan.Apply(start);
        TiffBasedWriteVerifier.Verify(start, layout, before, output, plan, limits, options.MaxAccountedRanges,
            allowMakerNoteCopy: makerNoteCopy);
        if (makerNoteCopy) output = ExifBlockEditor.RelocateMakerNote(output, exif!.MakerNoteCopy!, limits);
        if (icc is not null) HoldIcc(output, icc.Profile, limits);
        token.ThrowIfCancellationRequested();
        return output;
    }

    private static void AddIcc(byte[]? profile, TiffBasedFileMap layout,
        Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes)
    {
        TiffBasedField? field = layout.Find(TiffBasedRole.Primary)?.Find(IccTag);
        if (profile is null)
        {
            if (field is not null) changes[(TiffBasedRole.Primary, IccTag)] = null;
            return;
        }
        // UNDEFINED is the type the ICC specification gives the tag; a file that uses BYTE keeps it.
        // A tag of any other type is not a profile to the reader, and is written as one. The plan
        // leaves a value alone that the file already has.
        ushort type = field is not null && field.Type == 1 ? (ushort)1 : (ushort)7;
        changes[(TiffBasedRole.Primary, IccTag)] = new(type, (uint)profile.Length, profile);
    }

    /// <summary>
    /// The last check of a profile save: the reader of profiles, which walks the file on its own,
    /// returns from the output the profile that was asked for, or none.
    /// </summary>
    private static void HoldIcc(byte[] output, byte[]? profile, MetadataLimits limits)
    {
        byte[]? read;
        try { read = IccProfiles.Read(output, limits); }
        catch (MetadataException failure)
        {
            if (failure.Code == MetadataErrorCode.LimitExceeded)
                throw new MetadataException(failure.Code,
                    "The edited file exceeds a budget of the limits it would be read with: " + failure.Message);
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "The planned output failed verification and was not written: the reader of ICC profiles refuses it.");
        }
        bool held = profile is null ? read is null : read is not null && read.AsSpan().SequenceEqual(profile);
        if (!held)
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation,
                "The planned output failed verification and was not written: the reader does not return the ICC profile as it was asked for.");
    }

    private static void AddExif(ExifEdit.EditSnapshot edit, TiffBasedFileMap layout,
        Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes)
    {
        if (edit.RemoveAll)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The EXIF of a TIFF-based file is part of the directories that describe its image and cannot be removed as a whole.");
        // The tags of the MakerNote were changed before the plan.
        ExifBlockEditor.AddChanges(edit, layout.Order, changes, makerNoteApart: true);
    }

    /// <summary>The padding of a packet that is appended, so that the next edit is written over it.</summary>
    internal const int XmpPaddingBytes = 2048;

    private void AddXmp(XmpEdit edit, TiffBasedFileMap layout,
        Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes)
    {
        TiffBasedField? field = layout.Find(TiffBasedRole.Primary)?.Find(XmpTag);
        bool stored = field is not null && field.ByteLength != 0;
        if (edit.RemovesAll)
        {
            if (field is not null) changes[(TiffBasedRole.Primary, XmpTag)] = null;
            return;
        }
        XmpPacket packet = stored ? XmpPacket.Parse(Value(field!)) : XmpPacket.CreateEmpty();
        // A packet that still fits is padded to the length of the stored one and written over it;
        // one that grows is appended with padding of its own.
        XmpPacket changed = packet.Apply(edit, stored ? (int)Math.Min(field!.ByteLength, int.MaxValue) : 0, XmpPaddingBytes)!;
        // An edit that leaves the packet as it is, or leaves a file without a packet without one, changes nothing.
        if (ReferenceEquals(changed, packet)) return;
        byte[] bytes = new byte[changed.ByteLength];
        changed.CopyRawTo(bytes);
        // BYTE is the type the XMP specification gives the tag; a file that uses UNDEFINED keeps it.
        ushort type = field is not null && field.Type == 7 ? (ushort)7 : (ushort)1;
        changes[(TiffBasedRole.Primary, XmpTag)] = new(type, (uint)bytes.Length, bytes);
    }

    private void AddIptc(IptcEdit edit, bool updateDigest, TiffBasedFileMap layout, MetadataLimits limits,
        Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes)
    {
        TiffBasedField? field = layout.Find(TiffBasedRole.Primary)?.Find(IptcTag);
        bool stored = field is not null && field.ByteLength != 0;
        IptcPacket? packet = null;
        if (stored)
        {
            ReadOnlySpan<byte> stream = Value(field!);
            // A stream stored as LONGs is rounded up to four bytes with zeros.
            if (field!.Type == 4) stream = stream[..TiffIptc.UnpaddedLength(stream)];
            packet = IptcPacket.Inspect(stream);
        }
        else if (layout.Find(TiffBasedRole.Primary)?.Find(ResourcesTag) is TiffBasedField block && block.ByteLength != 0
            && Resources(block) is PhotoshopResources resources && resources.IptcForReading() is not null)
        {
            // The stream lives in the Photoshop resources (0x8649) and is edited there. A new 0x83BB
            // tag would hide it from this library and leave the old one in the file for every other reader.
            if (block.Type is not (1 or 7))
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "The Photoshop image resources of this file are not stored as bytes and are not edited.");
            resources.ValidateTargets();
            IptcPacket held = IptcPacket.Parse(resources.Iptc!.Data.Span);
            IptcPacket? edited = held.Apply(edit);
            if (ReferenceEquals(edited, held) && !(updateDigest && resources.DigestIsStale())) return;
            byte[]? stream = null;
            if (edited is not null) { stream = new byte[edited.ByteLength]; edited.CopyRawTo(stream); }
            byte[]? rewritten = resources.Rewrite(stream, updateDigest, int.MaxValue);
            // The tag holds the resources without the signature an APP13 segment begins with.
            changes[(TiffBasedRole.Primary, ResourcesTag)] = rewritten is null || rewritten.Length == 14 ? null
                : new(block.Type, (uint)(rewritten.Length - 14), rewritten[14..]);
            return;
        }
        IptcPacket? changed = (packet ?? IptcPacket.CreateEmpty()).Apply(edit);
        if (changed is null)
        {
            if (field is not null) changes[(TiffBasedRole.Primary, IptcTag)] = null;
            return;
        }
        // Nothing was added to an absent stream, or nothing in the stored one was changed.
        if (packet is null ? Unchanged(changed) : ReferenceEquals(changed, packet))
            return;
        byte[] bytes = new byte[changed.ByteLength];
        changed.CopyRawTo(bytes);
        if (field is not null && field.Type == 4)
        {
            // The file stores the stream as LONGs: it stays that way, padded as such writers pad it.
            byte[] padded = new byte[(bytes.Length + 3) / 4 * 4];
            bytes.CopyTo(padded, 0);
            changes[(TiffBasedRole.Primary, IptcTag)] = new(4, (uint)(padded.Length / 4), padded);
            return;
        }
        ushort type = field is not null && field.Type == 1 ? (ushort)1 : (ushort)7;
        changes[(TiffBasedRole.Primary, IptcTag)] = new(type, (uint)bytes.Length, bytes);
    }

    /// <summary>The Photoshop image resources a tag holds.</summary>
    private PhotoshopResources Resources(TiffBasedField field)
    {
        ReadOnlySpan<byte> signature = "Photoshop 3.0\0"u8;
        ReadOnlySpan<byte> value = Value(field);
        byte[] payload = new byte[signature.Length + value.Length];
        signature.CopyTo(payload);
        value.CopyTo(payload.AsSpan(signature.Length));
        return PhotoshopResources.Parse(payload, new IptcLimits());
    }

    /// <summary>Whether a packet built from the empty one is still the empty one: nothing was added.</summary>
    private static bool Unchanged(IptcPacket packet)
    {
        IptcPacket empty = IptcPacket.CreateEmpty();
        if (packet.ByteLength != empty.ByteLength) return false;
        byte[] left = new byte[packet.ByteLength];
        byte[] right = new byte[empty.ByteLength];
        packet.CopyRawTo(left);
        empty.CopyRawTo(right);
        return left.AsSpan().SequenceEqual(right);
    }

    // Extract TIFF/DNG from the already-validated representation.
    private ReadOnlySpan<byte> Value(TiffBasedField field)
    {
        if (field.ValueOffset > source.Length - field.ByteLength)
            throw new MetadataException(MetadataErrorCode.InvalidTiff, "A value to edit lies outside the file.", field.EntryOffset);
        return source.AsSpan((int)field.ValueOffset, (int)field.ByteLength);
    }

    /// <summary>Marks the document as disposed and closes a retained compatibility source when it owns one.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ownsLegacyStream) legacyStream?.Dispose();
    }

    /// <inheritdoc cref="Dispose"/>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        if (ownsLegacyStream && legacyStream is not null) await legacyStream.DisposeAsync().ConfigureAwait(false);
    }
}
