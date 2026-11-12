using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Tiff.Serialization;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Bmff;

/// <summary>
/// An ISO BMFF image file (HEIC, HEIF, AVIF) opened for editing its EXIF, GPS and XMP, the EXIF
/// item and the XMP item that the top-level <c>meta</c> box declares, and its ICC profile, a
/// property of the primary item.
/// </summary>
/// <remarks>
/// <para>A save never moves the data of an item. An item whose new bytes are as many as the old
/// ones is written where it is. One that grows, and one the file does not have yet, is appended to
/// the file in a media data box of its own, and its entry in the item location box is pointed
/// there; the bytes it replaces stay in the file, no longer addressed: an edit is not an erasure.
/// Inside an EXIF item the same holds for its TIFF structure: values and directories are written in
/// place or appended to the item, and the MakerNote keeps its offset.</para>
/// <para>The <c>meta</c> box is rewritten where it is when its length does not change. For a new
/// item it has to grow: it does so in place when it is the last box of the file; otherwise a new
/// one is appended and the old one becomes a <c>free</c> box. No other box changes its offset or
/// its length, so image data stays where every offset in the file says it is.</para>
/// <para>The ICC profile is not an item: it is a <c>colr</c> property in the item properties box,
/// which is inside the <c>meta</c> box. That box is rewritten in the bytes it has when it does not
/// grow, and the <c>meta</c> box grows or is replaced, as for a new item, when it does.</para>
/// <para>Every save is planned, applied to a copy in memory, and read back before anything reaches
/// the destination; a save that cannot be shown to have changed only what was asked is refused with
/// a <see cref="MetadataException"/> and writes nothing. An edit that changes nothing writes a copy
/// of the source.</para>
/// <para>The document holds the whole file in memory and is independent of the stream or path it
/// was opened from, so the source cannot change under it. Each save starts from the file as it was
/// opened; to apply a second kind of edit, open the output of the first. It is not safe for
/// concurrent use.</para>
/// </remarks>
public sealed partial class BmffMetadataDocument : IDisposable, IAsyncDisposable
{
    private const int MaxAccountedRanges = 16_384;

    private readonly byte[] source;
    private readonly string? sourcePath;
    private readonly BmffReadOptions options;
    private readonly BmffFileMap? map;
    private readonly MetadataException? refusal;
    private bool disposed;

    private BmffMetadataDocument(byte[] source, string? sourcePath, BmffReadOptions options)
    {
        this.source = source;
        this.sourcePath = sourcePath;
        this.options = options;
        // A structure the writer cannot account for is still a file to read: the refusal is kept for Save.
        try { map = BmffFileMap.Read(source, options.Limits); }
        catch (MetadataException failure) { refusal = failure; }
    }

    /// <summary>Reads the whole stream, from its start when it can seek, and leaves it open.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidBmff"/> when the data does not begin with a file type
    /// box; <see cref="MetadataErrorCode.LimitExceeded"/> when it exceeds the input budget.
    /// </exception>
    public static BmffMetadataDocument Open(Stream source, BmffReadOptions? options = null) =>
        OpenCore(source, null, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();

    /// <summary>Reads the whole file and closes it.</summary>
    public static BmffMetadataDocument Open(string path, BmffReadOptions? options = null) =>
        OpenPath(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="Open(Stream, BmffReadOptions?)"/>
    public static Task<BmffMetadataDocument> OpenAsync(Stream source, BmffReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        OpenCore(source, null, (options ?? new()).Snapshot(), true, cancellationToken);

    /// <inheritdoc cref="Open(string, BmffReadOptions?)"/>
    public static Task<BmffMetadataDocument> OpenAsync(string path, BmffReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        OpenPath(path, (options ?? new()).Snapshot(), true, cancellationToken);

    private static async Task<BmffMetadataDocument> OpenPath(string path, BmffReadOptions options,
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

    private static async Task<BmffMetadataDocument> OpenCore(Stream source, string? path,
        BmffReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
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
        if (bytes.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) != BmffFileMap.FileType)
            throw new MetadataException(MetadataErrorCode.InvalidBmff, "The data does not begin with a file type box.", 0);
        token.ThrowIfCancellationRequested();
        return new BmffMetadataDocument(bytes, path, options);
    }

    private static MetadataException TooLarge() =>
        new(MetadataErrorCode.LimitExceeded, "The BMFF file exceeds the input budget.", 0);

    /// <summary>The EXIF item of the file, as <see cref="BmffMetadata.Read"/> returns it, or null.</summary>
    public ExifMetadata? ReadExif()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return BmffReader.Read(source, options.Limits).Exif;
    }

    /// <summary>The XMP item of the file (<c>mime</c>, <c>application/rdf+xml</c>), or null.</summary>
    public XmpPacket? ReadXmp(XmpReadOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return BmffReader.Read(source, this.options.Limits, options).Xmp;
    }

    /// <summary>
    /// Writes the file with the EXIF edit applied to an empty, seekable stream. The edit names tags
    /// of the primary, Exif and GPS directories; a GPS directory the item does not have is created,
    /// and so is the EXIF item of a file that has none.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the edit needs to append and the file
    /// does not allow it (a box or an item that runs to the end of the file, an item addressed past
    /// it), when the <c>meta</c> box has to change its length and cannot, or when the planned
    /// output fails its verification; <see cref="MetadataErrorCode.UnsupportedFeature"/> for a file
    /// without a <c>meta</c> box or without a primary item to attach a new item to, a top-level box
    /// of a kind that may not be followed by new data, an item built from other items or stored in
    /// another file, and removal of all EXIF; <see cref="MetadataErrorCode.LimitExceeded"/> when a
    /// value, the EXIF item or the output would exceed a budget of the limits (the item has no
    /// other bound: it may be longer than a JPEG APP1 segment holds); otherwise the code the
    /// reader reports for the file.
    /// </exception>
    public void SaveExif(Stream destination, ExifEdit edit) => SaveCore(destination, Edit(edit), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveExif(Stream, ExifEdit)"/>
    public Task SaveExifAsync(Stream destination, ExifEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit), true, cancellationToken);

    /// <summary>
    /// Writes the file with the XMP edit applied: the packet is the XMP item, created when the file
    /// has none. Fails as <see cref="SaveExif"/> does, and with
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a packet outside the editable profile
    /// and for removal of all XMP.
    /// </summary>
    public void SaveXmp(Stream destination, XmpEdit edit) => SaveCore(destination, Edit(edit), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveXmp(Stream, XmpEdit)"/>
    public Task SaveXmpAsync(Stream destination, XmpEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit), true, cancellationToken);

    /// <summary>Writes the edited file to a new path; the file appears there complete or not at all.</summary>
    public void SaveExifAs(string destinationPath, ExifEdit edit) => SavePath(destinationPath, Edit(edit), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveExifAs(string, ExifEdit)"/>
    public Task SaveExifAsAsync(string destinationPath, ExifEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit), true, cancellationToken);
    /// <summary>As <see cref="SaveXmp(Stream, XmpEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    public void SaveXmpAs(string destinationPath, XmpEdit edit) => SavePath(destinationPath, Edit(edit), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveXmpAs(string, XmpEdit)"/>
    public Task SaveXmpAsAsync(string destinationPath, XmpEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit), true, cancellationToken);

    /// <summary>The changes of one save, captured when the save is called.</summary>
    private sealed record Pending(ExifEdit.EditSnapshot? Exif, (bool RemoveAll, Dictionary<XmpPropertyName, XmpValue?> Changes)? Xmp);

    private static Pending Edit(ExifEdit edit) { ArgumentNullException.ThrowIfNull(edit); return new(edit.Snapshot(), null); }
    private static Pending Edit(XmpEdit edit) { ArgumentNullException.ThrowIfNull(edit); return new(null, edit.Snapshot()); }

    private async Task SaveCore(Stream destination, Pending edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ContainerIo.Output(Stream.Null, destination);
        byte[] output = Prepare(edit, token);
        await ContainerIo.Write(destination, output, asynchronous, token).ConfigureAwait(false);
    }

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
        token.ThrowIfCancellationRequested();
        ExifEdit.EditSnapshot? exif = edit.Exif;
        (bool RemoveAll, Dictionary<XmpPropertyName, XmpValue?> Changes)? xmp = edit.Xmp;
        bool exifEmpty = exif is null || exif.IsEmpty;
        bool xmpEmpty = xmp is null || (!xmp.Value.RemoveAll && xmp.Value.Changes.Count == 0);
        if (exifEmpty && xmpEmpty) return source;

        if (refusal is not null) throw new MetadataException(refusal.Code, refusal.Message, refusal.SourceOffset);
        BmffFileMap layout = map!;
        MetadataLimits limits = options.Limits;
        // What the reader makes of the source: the output is held against it, so a source the reader
        // refuses cannot be edited.
        BmffMetadata before = BmffReader.Read(source, limits);
        if (layout.MetaBox is null)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The file has no meta box: there is no item structure to hold the metadata.");

        BmffItemKind kind;
        BmffItem? item;
        byte[]? payload;
        TiffBasedWritePlan? tiffPlan = null;
        ExifMetadata? desired = null;
        if (!exifEmpty)
        {
            kind = BmffItemKind.Exif;
            item = layout.Find(BmffFileMap.ExifItem, null);
            payload = ExifPayload(exif!, layout, item, limits, out tiffPlan, out desired);
        }
        else
        {
            kind = BmffItemKind.Xmp;
            item = layout.Find(BmffFileMap.MimeItem, BmffFileMap.XmpContentType);
            payload = XmpPayload(xmp!.Value.RemoveAll, xmp.Value.Changes, layout, item);
        }
        if (payload is null) return source;

        token.ThrowIfCancellationRequested();
        BmffWritePlan plan = BmffWritePlanner.Create(source, layout, kind, item, payload, limits);
        if (plan.IsEmpty) return source;
        byte[] output = plan.Apply(source);
        BmffWriteVerifier.Verify(source, layout, before, output, plan, limits, tiffPlan, desired);
        token.ThrowIfCancellationRequested();
        return output;
    }

    /// <summary>The bytes the EXIF item is to have, or null when the edit changes nothing.</summary>
    private byte[]? ExifPayload(ExifEdit.EditSnapshot edit, BmffFileMap layout, BmffItem? item, MetadataLimits limits,
        out TiffBasedWritePlan? tiffPlan, out ExifMetadata? desired)
    {
        tiffPlan = null;
        desired = null;
        if (edit.RemoveAll)
        {
            if (item is null) return null;
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "An item of a BMFF file is not removed: the EXIF item can be edited tag by tag.");
        }
        if (item is null)
        {
            // Removing tags from an item that is not there changes nothing.
            if (edit.Changes.Values.All(value => value is null)) return null;
            desired = edit.Apply(null, Endianness.LittleEndian, limits);
            if (desired.Entries.Count == 0) return null;
            ExifWritePlan block = ExifLayoutPlanner.Create(null, desired, limits, ExifTiffReader.NoCeiling);
            if (block.Length > Array.MaxLength - 4)
                throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EXIF item exceeds the length of an array.");
            // The item begins with the offset of the TIFF header from the byte after it: zero.
            byte[] created = new byte[4 + block.Length];
            ExifTiffSerializer.Write(block, created.AsSpan(4));
            return created;
        }

        byte[] bytes = ItemBytes(layout, item);
        long start = bytes.Length < 4 ? long.MaxValue : 4L + BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (start >= bytes.Length)
            throw new MetadataException(MetadataErrorCode.InvalidBmff, "The EXIF TIFF header lies past its item.");
        // The TIFF structure of the item is edited as that of a TIFF file is: nothing in it moves,
        // and what grows is appended to it.
        byte[] tiff = bytes.AsSpan((int)start).ToArray();
        TiffBasedFileMap directories = TiffBasedFileMap.Read(tiff, limits, MaxAccountedRanges);
        ExifMetadata before = ExifTiffReader.InspectFile(tiff, limits);
        var changes = new Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?>();
        foreach (KeyValuePair<ExifTagKey, ExifValue?> change in edit.Changes)
        {
            TiffBasedRole role = change.Key.Directory switch
            {
                ExifDirectoryKind.Ifd0 => TiffBasedRole.Primary,
                ExifDirectoryKind.Exif => TiffBasedRole.Exif,
                ExifDirectoryKind.Gps => TiffBasedRole.Gps,
                _ => throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "Only tags of the primary, Exif and GPS directories are edited.")
            };
            if (change.Value is null)
            {
                changes[(role, change.Key.TagId)] = null;
                continue;
            }
            ExifValue value = ExifEdit.ConvertOrder(change.Value, directories.Order);
            changes[(role, change.Key.TagId)] = new((ushort)value.FieldType, value.Count, value.RawBytes.ToArray());
        }
        TiffBasedWritePlan plan = TiffBasedWritePlanner.Create(tiff, directories, changes, limits);
        if (plan.IsEmpty) return null;
        byte[] edited = plan.Apply(tiff);
        TiffBasedWriteVerifier.Verify(tiff, directories, before, edited, plan, limits, MaxAccountedRanges);
        if (edited.Length > Array.MaxLength - start)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EXIF item exceeds the length of an array.");
        tiffPlan = plan;
        byte[] payload = new byte[(int)start + edited.Length];
        bytes.AsSpan(0, (int)start).CopyTo(payload);
        edited.CopyTo(payload, (int)start);
        return payload;
    }

    /// <summary>The bytes the XMP item is to have, or null when the edit changes nothing.</summary>
    private byte[]? XmpPayload(bool removeAll, Dictionary<XmpPropertyName, XmpValue?> edit, BmffFileMap layout, BmffItem? item)
    {
        if (removeAll)
        {
            if (item is null) return null;
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "An item of a BMFF file is not removed: the XMP item can be edited property by property.");
        }
        // Removing names from a packet that is not there changes nothing.
        if (item is null && edit.Values.All(value => value is null)) return null;
        XmpPacket packet = item is null ? XmpPacket.CreateEmpty() : XmpPacket.Parse(ItemBytes(layout, item));
        var captured = new XmpEdit();
        foreach (KeyValuePair<XmpPropertyName, XmpValue?> change in edit)
        {
            if (change.Value is null) captured.Remove(change.Key);
            else captured.Set(change.Key, change.Value);
        }
        XmpPacket changed = packet.Apply(captured)!;
        if (item is not null && ReferenceEquals(changed, packet)) return null;
        byte[] bytes = new byte[changed.ByteLength];
        changed.CopyRawTo(bytes);
        return bytes;
    }

    /// <summary>The bytes of an item, its extents in their declared order, as the reader reads them.</summary>
    private byte[] ItemBytes(BmffFileMap layout, BmffItem item)
    {
        BmffLocation location = layout.Location(item.Id) ??
            throw new MetadataException(MetadataErrorCode.InvalidBmff, "A metadata item has no item location.");
        var segments = new List<BmffSegment>();
        if (!layout.TryResolve(location, wide: false, segments))
            throw new MetadataException(MetadataErrorCode.InvalidBmff, "A metadata item is addressed outside the file.");
        long total = 0;
        foreach (BmffSegment segment in segments) total += segment.Length;
        if (total > options.Limits.MaxRetainedMetadataBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The item data exceeds the retained metadata budget.");
        byte[] bytes = new byte[(int)total];
        int at = 0;
        foreach (BmffSegment segment in segments)
        {
            source.AsSpan(segment.Start, segment.Length).CopyTo(bytes.AsSpan(at));
            at += segment.Length;
        }
        return bytes;
    }

    /// <summary>Marks the document as disposed. It holds a copy of the file in memory and no file or stream.</summary>
    public void Dispose() => disposed = true;

    /// <inheritdoc cref="Dispose"/>
    public ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
