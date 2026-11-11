using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Tiff;

/// <summary>
/// A TIFF-based file opened for editing its EXIF, GPS, IPTC and XMP: TIFF, DNG and the raw formats
/// that are TIFF structures (CR2, NEF, ARW, PEF, ORF, RW2).
/// </summary>
/// <remarks>
/// <para>A save never moves a byte of the source. A value that fits where the old one is, is written
/// there. Whatever grows - a longer value, a directory that gains a tag, a new GPS directory, a new
/// XMP packet or IPTC stream - is appended after the last byte of the file, and the one pointer that
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

    private readonly byte[] source;
    private readonly string? sourcePath;
    private readonly TiffReadOptions options;
    private readonly TiffBasedFileMap? map;
    private readonly MetadataException? refusal;
    private bool disposed;

    private TiffBasedMetadataDocument(byte[] source, string? sourcePath, TiffReadOptions options)
    {
        this.source = source;
        this.sourcePath = sourcePath;
        this.options = options;
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

    public static Task<TiffBasedMetadataDocument> OpenAsync(Stream source, TiffReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        OpenCore(source, null, (options ?? new()).Snapshot(), true, cancellationToken);

    public static Task<TiffBasedMetadataDocument> OpenAsync(string path, TiffReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        OpenPath(path, (options ?? new()).Snapshot(), true, cancellationToken);

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

    private static async Task<TiffBasedMetadataDocument> OpenCore(Stream source, string? path,
        TiffReadOptions options, bool asynchronous, CancellationToken token)
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
        if (!ExifTiffReader.HasFileHeader(bytes))
            throw new MetadataException(MetadataErrorCode.InvalidTiff, "The data does not begin with a TIFF header.", 0);
        token.ThrowIfCancellationRequested();
        return new TiffBasedMetadataDocument(bytes, path, options);
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
    public Task SaveExifAsync(Stream destination, ExifEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit), true, cancellationToken);

    /// <summary>
    /// Writes the file with the XMP edit applied: the packet is the value of tag 700 of the primary
    /// directory, created when the file has none. Fails as <see cref="SaveExif"/> does, and with
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a packet outside the editable profile.
    /// </summary>
    public void SaveXmp(Stream destination, XmpEdit edit) => SaveCore(destination, Edit(edit), false, default).GetAwaiter().GetResult();
    public Task SaveXmpAsync(Stream destination, XmpEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit), true, cancellationToken);

    /// <summary>
    /// Writes the file with the IPTC edit applied: the stream is the value of tag 0x83BB of the
    /// primary directory, created when the file has none, and keeps the field type it has. Fails as
    /// <see cref="SaveExif"/> does, and with <see cref="MetadataErrorCode.UnsupportedFeature"/> when
    /// the IPTC of the file is stored in its Photoshop image resources only.
    /// </summary>
    public void SaveIptc(Stream destination, IptcEdit edit) => SaveCore(destination, Edit(edit), false, default).GetAwaiter().GetResult();
    public Task SaveIptcAsync(Stream destination, IptcEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, Edit(edit), true, cancellationToken);

    /// <summary>Writes the edited file to a new path; the file appears there complete or not at all.</summary>
    public void SaveExifAs(string destinationPath, ExifEdit edit) => SavePath(destinationPath, Edit(edit), false, default).GetAwaiter().GetResult();
    public Task SaveExifAsAsync(string destinationPath, ExifEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit), true, cancellationToken);
    public void SaveXmpAs(string destinationPath, XmpEdit edit) => SavePath(destinationPath, Edit(edit), false, default).GetAwaiter().GetResult();
    public Task SaveXmpAsAsync(string destinationPath, XmpEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit), true, cancellationToken);
    public void SaveIptcAs(string destinationPath, IptcEdit edit) => SavePath(destinationPath, Edit(edit), false, default).GetAwaiter().GetResult();
    public Task SaveIptcAsAsync(string destinationPath, IptcEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, Edit(edit), true, cancellationToken);

    /// <summary>The changes of one save, captured when the save is called.</summary>
    private sealed record Pending(ExifEdit.EditSnapshot? Exif, (bool RemoveAll, Dictionary<XmpPropertyName, XmpValue?> Changes)? Xmp, IptcEdit? Iptc);

    private static Pending Edit(ExifEdit edit) { ArgumentNullException.ThrowIfNull(edit); return new(edit.Snapshot(), null, null); }
    private static Pending Edit(XmpEdit edit) { ArgumentNullException.ThrowIfNull(edit); return new(null, edit.Snapshot(), null); }
    private static Pending Edit(IptcEdit edit) { ArgumentNullException.ThrowIfNull(edit); return new(null, null, edit.Capture()); }

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
        IptcEdit? iptc = edit.Iptc;
        bool exifEmpty = exif is null || exif.IsEmpty;
        bool xmpEmpty = xmp is null || (!xmp.Value.RemoveAll && xmp.Value.Changes.Count == 0);
        bool iptcEmpty = iptc is null || iptc.IsEmpty;
        if (exifEmpty && xmpEmpty && iptcEmpty) return source;

        if (refusal is not null) throw new MetadataException(refusal.Code, refusal.Message, refusal.SourceOffset);
        TiffBasedFileMap layout = map!;
        MetadataLimits limits = options.Limits;
        // What the reader makes of the source: the output is held against it, so a source the reader
        // refuses cannot be edited.
        ExifMetadata before = ExifTiffReader.InspectFile(source, limits);

        var changes = new Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?>();
        if (!exifEmpty) AddExif(exif!, layout, changes);
        if (!xmpEmpty) AddXmp(xmp!.Value.RemoveAll, xmp.Value.Changes, layout, changes);
        if (!iptcEmpty) AddIptc(iptc!, layout, limits, changes);
        if (changes.Count == 0) return source;

        token.ThrowIfCancellationRequested();
        TiffBasedWritePlan plan = TiffBasedWritePlanner.Create(source, layout, changes, limits);
        if (plan.IsEmpty) return source;
        byte[] output = plan.Apply(source);
        TiffBasedWriteVerifier.Verify(source, layout, before, output, plan, limits, options.MaxAccountedRanges);
        token.ThrowIfCancellationRequested();
        return output;
    }

    private static void AddExif(ExifEdit.EditSnapshot edit, TiffBasedFileMap layout,
        Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes)
    {
        if (edit.RemoveAll)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The EXIF of a TIFF-based file is part of the directories that describe its image and cannot be removed as a whole.");
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
            ExifValue value = ExifEdit.ConvertOrder(change.Value, layout.Order);
            changes[(role, change.Key.TagId)] = new((ushort)value.FieldType, value.Count, value.RawBytes.ToArray());
        }
    }

    private void AddXmp(bool removeAll, Dictionary<XmpPropertyName, XmpValue?> edit, TiffBasedFileMap layout,
        Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> changes)
    {
        TiffBasedField? field = layout.Find(TiffBasedRole.Primary)?.Find(XmpTag);
        bool stored = field is not null && field.ByteLength != 0;
        if (removeAll)
        {
            if (field is not null) changes[(TiffBasedRole.Primary, XmpTag)] = null;
            return;
        }
        // Removing names from a packet that is not there changes nothing.
        if (!stored && edit.Values.All(value => value is null)) return;
        XmpPacket packet = stored ? XmpPacket.Parse(Value(field!)) : XmpPacket.CreateEmpty();
        var captured = new XmpEdit();
        foreach (KeyValuePair<XmpPropertyName, XmpValue?> change in edit)
        {
            if (change.Value is null) captured.Remove(change.Key);
            else captured.Set(change.Key, change.Value);
        }
        XmpPacket changed = packet.Apply(captured)!;
        if (stored && ReferenceEquals(changed, packet)) return;
        byte[] bytes = new byte[changed.ByteLength];
        changed.CopyRawTo(bytes);
        // BYTE is the type the XMP specification gives the tag; a file that uses UNDEFINED keeps it.
        ushort type = field is not null && field.Type == 7 ? (ushort)7 : (ushort)1;
        changes[(TiffBasedRole.Primary, XmpTag)] = new(type, (uint)bytes.Length, bytes);
    }

    private void AddIptc(IptcEdit edit, TiffBasedFileMap layout, MetadataLimits limits,
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
        else if (TiffIptc.Read(source, limits) is not null)
        {
            // The reader found a stream in the Photoshop resources (0x8649). A new tag would hide it
            // from this library and leave it in the file for every other reader.
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                "The IPTC of this file is stored in its Photoshop image resources, which are not edited.");
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

    private ReadOnlySpan<byte> Value(TiffBasedField field)
    {
        if (field.ValueOffset > source.Length - field.ByteLength)
            throw new MetadataException(MetadataErrorCode.InvalidTiff, "A value to edit lies outside the file.", field.EntryOffset);
        return source.AsSpan((int)field.ValueOffset, (int)field.ByteLength);
    }

    public void Dispose() => disposed = true;

    public ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
