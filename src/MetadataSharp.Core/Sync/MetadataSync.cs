using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Core.Sync;

/// <summary>
/// Conversion between EXIF, IPTC and XMP, and their reconciliation. Every method is a pure function:
/// it reads what it is given and returns edits; nothing is written until the edits are saved.
/// </summary>
/// <remarks>
/// <para>
/// The one-way conversions copy every field of <see cref="ExifMappings"/> or <see cref="IptcMappings"/>
/// that the source holds. A field the source lacks is no change: its counterpart in the target stays.
/// A field whose value cannot be converted is not written and is listed in <see cref="SyncEdits.Notes"/>.
/// </para>
/// <para>
/// What is not kept. An XMP date keeps its zone as written; EXIF holds it in the OffsetTime tags and
/// IIM requires one, so a date without a zone comes back from IIM as <c>+00:00</c>, and IIM drops
/// fractions of a second. IIM text is cut to the length of its dataset. A GPS coordinate comes back
/// as degrees, minutes and seconds to four decimals: the same place, not always the same rationals.
/// Of a language alternative only the default text is converted. EXIF text beyond ASCII is written
/// as UTF-8.
/// </para>
/// </remarks>
public static class MetadataSync
{
    /// <summary>The EXIF tags and the XMP properties they convert to and from.</summary>
    public static IReadOnlyList<ExifXmpMapping> ExifMappings { get; } = Array.AsReadOnly(SyncTable.Exif);

    /// <summary>The IIM datasets and the XMP properties they convert to and from.</summary>
    public static IReadOnlyList<IptcXmpMapping> IptcMappings { get; } = Array.AsReadOnly(SyncTable.Iptc);

    private static readonly (string Path, ExifXmpMapping? Exif, IptcXmpMapping? Iptc)[] Shared = BuildShared();

    // Derive the cross-family mapping once so all sync directions use the same field definitions.
    private static (string, ExifXmpMapping?, IptcXmpMapping?)[] BuildShared()
    {
        var fields = new List<(string, ExifXmpMapping?, IptcXmpMapping?)>();
        foreach (IptcXmpMapping iptc in SyncTable.Iptc)
            fields.Add((iptc.XmpPath, Array.Find(SyncTable.Exif, e => SyncTable.IsShared(e) && e.XmpPath == iptc.XmpPath), iptc));
        foreach (ExifXmpMapping exif in SyncTable.Exif)
            if (SyncTable.IsShared(exif) && !Array.Exists(SyncTable.Iptc, i => i.XmpPath == exif.XmpPath)) fields.Add((exif.XmpPath, exif, null));
        return fields.ToArray();
    }

    /// <summary>Copies EXIF to XMP: the returned <see cref="SyncEdits.Xmp"/> writes the properties.</summary>
    /// <param name="exif">The source.</param>
    /// <param name="xmp">The packet the edit is meant for, or null when the file has none.</param>
    /// <param name="options">Overwrite or fill, and whether the converted tags are removed from EXIF (<see cref="SyncEdits.Exif"/>).</param>
    /// <returns>The XMP edits and any conversions that could not be made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exif"/> is null.</exception>
    /// <exception cref="ArgumentException">A converted value cannot be represented within the XMP edit's value limits.</exception>
    public static SyncEdits ExifToXmp(ExifMetadata exif, XmpPacket? xmp = null, SyncOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(exif);
        options ??= new();
        var edits = new SyncEdits();
        var view = new SyncXmp(xmp);
        foreach (ExifXmpMapping mapping in SyncTable.Exif)
        {
            string[]? items = SyncExif.Read(exif, mapping);
            if (items is null)
            {
                if (exif.TryGetEntry(mapping.Tag, out _) && !SyncExif.IsBlank(exif, mapping))
                    edits.Note(SyncExif.Key(mapping.Tag), mapping.XmpPath, "The EXIF value is not one the tag is defined to hold, or it lacks a companion tag.");
                continue;
            }
            if (options.Mode == SyncMode.FillMissing && view.Has(mapping.NamespaceUri, mapping.Name)) continue;
            if (!ToXmp(edits, view, SyncExif.Key(mapping.Tag), mapping, items)) continue;
            if (options.EraseSource) SyncExif.Erase(edits.Exif, mapping);
        }
        return edits;
    }

    /// <summary>Copies XMP to EXIF: the returned <see cref="SyncEdits.Exif"/> writes the tags.</summary>
    /// <param name="xmp">The source.</param>
    /// <param name="exif">The block the edit is meant for, or null when the file has none.</param>
    /// <param name="options">Overwrite or fill, and whether the converted properties are removed from XMP (<see cref="SyncEdits.Xmp"/>).</param>
    /// <returns>The EXIF edits and any conversions that could not be made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xmp"/> is null.</exception>
    /// <exception cref="ArgumentException">A converted value cannot be represented by the EXIF tag definition.</exception>
    public static SyncEdits XmpToExif(XmpPacket xmp, ExifMetadata? exif = null, SyncOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(xmp);
        options ??= new();
        var edits = new SyncEdits();
        var view = new SyncXmp(xmp);
        foreach (ExifXmpMapping mapping in SyncTable.Exif)
        {
            string[]? items = mapping.Conversion == SyncConversion.Flash ? view.ReadFlash() : view.Read(mapping.NamespaceUri, mapping.Name);
            if (items is null)
            {
                if (view.Has(mapping.NamespaceUri, mapping.Name)) edits.Note(mapping.XmpPath, SyncExif.Key(mapping.Tag), "The XMP property is empty or holds something other than texts.");
                continue;
            }
            if (options.Mode == SyncMode.FillMissing && exif is not null && exif.TryGetEntry(mapping.Tag, out _)) continue;
            if (!ToExif(edits, mapping.XmpPath, mapping, items, exif)) continue;
            if (options.EraseSource) edits.Xmp.RemovePath(mapping.XmpPath);
        }
        return edits;
    }

    /// <summary>Copies IPTC to XMP: the returned <see cref="SyncEdits.Xmp"/> writes the properties.</summary>
    /// <param name="iptc">The source.</param>
    /// <param name="xmp">The packet the edit is meant for, or null when the file has none.</param>
    /// <param name="options">Overwrite or fill, and whether the converted datasets are removed from IPTC (<see cref="SyncEdits.Iptc"/>).</param>
    /// <returns>The XMP edits and any conversions that could not be made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="iptc"/> is null.</exception>
    /// <exception cref="ArgumentException">A converted value cannot be represented within the XMP edit's value limits.</exception>
    public static SyncEdits IptcToXmp(IptcPacket iptc, XmpPacket? xmp = null, SyncOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(iptc);
        options ??= new();
        var edits = new SyncEdits();
        var view = new SyncXmp(xmp);
        foreach (IptcXmpMapping mapping in SyncTable.Iptc)
        {
            string[]? items = SyncIptc.Read(iptc, mapping, out bool undecodable);
            if (items is null)
            {
                if (undecodable) edits.Note(SyncIptc.Key(mapping.Dataset), mapping.XmpPath, "The dataset is not text in a declared character set, or not a date.");
                continue;
            }
            if (options.Mode == SyncMode.FillMissing && view.Has(mapping.NamespaceUri, mapping.Name)) continue;
            if (!ToXmp(edits, view, SyncIptc.Key(mapping.Dataset), mapping.XmpPath, mapping.NamespaceUri, mapping.Name, mapping.XmpForm, items)) continue;
            if (options.EraseSource) SyncIptc.Erase(edits.Iptc, mapping);
        }
        return edits;
    }

    /// <summary>Copies XMP to IPTC: the returned <see cref="SyncEdits.Iptc"/> writes the datasets.</summary>
    /// <param name="xmp">The source.</param>
    /// <param name="iptc">The stream the edit is meant for, or null when the file has none.</param>
    /// <param name="options">Overwrite or fill, and whether the converted properties are removed from XMP (<see cref="SyncEdits.Xmp"/>).</param>
    /// <returns>The IPTC edits and any conversions that could not be made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xmp"/> is null.</exception>
    /// <exception cref="ArgumentException">A converted value violates an IPTC dataset definition or edit budget.</exception>
    public static SyncEdits XmpToIptc(XmpPacket xmp, IptcPacket? iptc = null, SyncOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(xmp);
        options ??= new();
        var edits = new SyncEdits();
        var view = new SyncXmp(xmp);
        foreach (IptcXmpMapping mapping in SyncTable.Iptc)
        {
            string[]? items = view.Read(mapping.NamespaceUri, mapping.Name);
            if (items is null)
            {
                if (view.Has(mapping.NamespaceUri, mapping.Name)) edits.Note(mapping.XmpPath, SyncIptc.Key(mapping.Dataset), "The XMP property is empty or holds something other than texts.");
                continue;
            }
            if (options.Mode == SyncMode.FillMissing && iptc is not null && SyncIptc.Has(iptc, mapping.Dataset)) continue;
            if (!ToIptc(edits, mapping.XmpPath, mapping, items)) continue;
            if (options.EraseSource) edits.Xmp.RemovePath(mapping.XmpPath);
        }
        return edits;
    }

    /// <summary>
    /// Brings the three families into agreement on the fields more than one of them holds, by the
    /// rules of the MWG Guidelines for Handling Image Metadata 2.0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fields. Every row of <see cref="IptcMappings"/>, and of EXIF the six tags that say the same
    /// thing as IPTC or XMP: ImageDescription, Artist, Copyright, DateTime, DateTimeOriginal and
    /// DateTimeDigitized. Every other EXIF tag is EXIF's alone and is not copied into XMP, unless
    /// <see cref="ReconcileOptions.MirrorAllExif"/> asks for it.
    /// </para>
    /// <para>
    /// Which value is taken. EXIF, when it holds the field. Otherwise IPTC when XMP lacks the field,
    /// or when the digest is <see cref="IptcDigestState.Stale"/> and XMP does not say what IPTC says;
    /// otherwise XMP. IPTC text that is the start of the XMP text, cut at the length of its dataset,
    /// says what XMP says. Dates that differ only in a part one of them lacks - the fraction of a
    /// second, the zone - say the same.
    /// </para>
    /// <para>
    /// Where it is written. To XMP always; to EXIF and to IPTC where the file has the block, unless
    /// the options ask for it to be created. A family that already says the same is not written, so
    /// the edits of a reconciled file are empty. A block that is not editable is not written and is
    /// named in <see cref="SyncEdits.Notes"/>.
    /// </para>
    /// <para>
    /// After the edits are saved IPTC and XMP agree: save IPTC with <see cref="SyncEdits.IptcWriteOptions"/>,
    /// which ask for the digest to be recomputed, so that the digest says so.
    /// </para>
    /// </remarks>
    /// <param name="exif">The EXIF block of the file, or null.</param>
    /// <param name="iptc">The IPTC stream of the file, or null.</param>
    /// <param name="xmp">The XMP packet of the file, or null.</param>
    /// <param name="digest">What the stored IPTC digest says about the stream; null for what <paramref name="iptc"/> was read with (<see cref="IptcPacket.Digest"/>).</param>
    /// <param name="options">Whether blocks the file lacks are created, and whether all of EXIF is mirrored.</param>
    /// <returns>The family edits needed to reconcile the shared fields, plus notes for refused conversions or writes.</returns>
    public static SyncEdits Reconcile(ExifMetadata? exif, IptcPacket? iptc, XmpPacket? xmp,
        IptcDigestState? digest = null, ReconcileOptions? options = null)
    {
        options ??= new();
        digest ??= iptc?.Digest ?? IptcDigestState.Absent;
        var edits = new SyncEdits();
        var view = new SyncXmp(xmp);
        bool writeExif = exif is null ? options.CreateExif : exif.IsEditable;
        bool writeIptc = iptc is null ? options.CreateIptc : iptc.IsEditable;
        bool writeXmp = xmp is null || xmp.IsEditable;
        if (exif is { IsEditable: false }) edits.Note(string.Empty, "Exif", "The EXIF block is not editable; it is left as it is.");
        if (iptc is { IsEditable: false }) edits.Note(string.Empty, "Iptc", "The IPTC stream is not editable; it is left as it is.");
        if (!writeXmp) edits.Note(string.Empty, "Xmp", "The XMP packet is not editable; it is left as it is.");

        foreach ((string path, ExifXmpMapping? exifMapping, IptcXmpMapping? iptcMapping) in Shared)
        {
            string uri = exifMapping?.NamespaceUri ?? iptcMapping!.NamespaceUri, name = exifMapping?.Name ?? iptcMapping!.Name;
            XmpValueKind form = iptcMapping?.XmpForm ?? exifMapping!.XmpForm;
            bool isDate = exifMapping?.Conversion == SyncConversion.Date || iptcMapping?.TimeDataset is not null;
            string[]? fromExif = exif is not null && exifMapping is not null ? SyncExif.Read(exif, exifMapping) : null;
            string[]? fromIptc = iptc is not null && iptcMapping is not null ? SyncIptc.Read(iptc, iptcMapping, out _) : null;
            string[]? fromXmp = view.Read(uri, name);

            string[] value;
            SyncSource source;
            if (fromExif is not null) (value, source) = (fromExif, SyncSource.Exif);
            else if (fromIptc is not null && (fromXmp is null || (digest == IptcDigestState.Stale && !IptcSays(iptcMapping!, isDate, fromIptc, fromXmp))))
                (value, source) = (fromIptc, SyncSource.Iptc);
            else if (fromXmp is not null) (value, source) = (fromXmp, SyncSource.Xmp);
            else continue;
            if ((fromExif is null ? 0 : 1) + (fromIptc is null ? 0 : 1) + (fromXmp is null ? 0 : 1) > 1) edits.Choose(path, source);
            string key = source == SyncSource.Exif ? SyncExif.Key(exifMapping!.Tag) : source == SyncSource.Iptc ? SyncIptc.Key(iptcMapping!.Dataset) : path;

            if (writeXmp && source != SyncSource.Xmp && !Same(isDate, fromXmp, value))
                ToXmp(edits, view, key, path, uri, name, form, value);
            if (writeExif && exifMapping is not null && source != SyncSource.Exif)
            {
                // EXIF text is one string; a date needs its time of day.
                string[]? held = exifMapping.Conversion == SyncConversion.Text ? new[] { value[0] }
                    : isDate && !(SyncDate.TryParse(value[0], out SyncDate date) && date.HasTime) ? null : value;
                if (held is null) edits.Note(key, SyncExif.Key(exifMapping.Tag), "The date names no time of day; EXIF holds a day and a time.");
                else if (!Same(isDate, fromExif, held)) ToExif(edits, key, exifMapping, held, exif);
            }
            if (writeIptc && iptcMapping is not null && source != SyncSource.Iptc && !IptcSays(iptcMapping, isDate, fromIptc, value))
                ToIptc(edits, key, iptcMapping, value);
        }
        if (options.MirrorAllExif && exif is not null && writeXmp)
        {
            // Where several tags name one property the last row of the table gives it, as in ExifToXmp;
            // it is compared once, so that a mirrored file gives no edit.
            var mirrored = new Dictionary<string, (ExifXmpMapping Mapping, string[] Items)>(StringComparer.Ordinal);
            foreach (ExifXmpMapping mapping in SyncTable.Exif)
            {
                if (!SyncTable.IsShared(mapping) && SyncExif.Read(exif, mapping) is string[] items) mirrored[mapping.XmpPath] = (mapping, items);
            }
            foreach ((ExifXmpMapping mapping, string[] items) in mirrored.Values)
            {
                string[]? held = mapping.Conversion == SyncConversion.Flash ? view.ReadFlash() : view.Read(mapping.NamespaceUri, mapping.Name);
                if (held is null || !Enumerable.SequenceEqual(held, items)) ToXmp(edits, view, SyncExif.Key(mapping.Tag), mapping, items);
            }
        }
        edits.IptcWriteOptions = new() { UpdateDigest = true };
        return edits;
    }

    /// <summary>Whether IPTC says what the value says, as far as IIM can: text up to the length of the dataset.</summary>
    private static bool IptcSays(IptcXmpMapping mapping, bool isDate, string[]? held, string[] value) =>
        Same(isDate, held, isDate ? value : SyncIptc.Project(mapping, value));

    /// <summary>Whether a family that holds <paramref name="held"/> says what <paramref name="value"/> says.</summary>
    private static bool Same(bool isDate, string[]? held, string[]? value)
    {
        if (held is null || value is null) return held is null && value is null;
        if (isDate && held.Length == 1 && value.Length == 1 && SyncDate.TryParse(held[0], out SyncDate a) && SyncDate.TryParse(value[0], out SyncDate b))
        {
            return a.Year == b.Year && a.Month == b.Month && a.Day == b.Day && a.HasTime == b.HasTime
                && a.Hour == b.Hour && a.Minute == b.Minute && a.Second == b.Second
                && (a.Fraction.TrimEnd('0') == b.Fraction.TrimEnd('0') || a.Fraction.Length == 0 || b.Fraction.Length == 0)
                && (a.Offset == b.Offset || a.Offset.Length == 0 || b.Offset.Length == 0);
        }
        return Enumerable.SequenceEqual(held, value);
    }

    // Write ordinary properties through their mapping; Flash is expanded into its five structure fields.
    private static bool ToXmp(SyncEdits edits, SyncXmp view, string source, ExifXmpMapping mapping, string[] items)
    {
        if (mapping.Conversion != SyncConversion.Flash)
            return ToXmp(edits, view, source, mapping.XmpPath, mapping.NamespaceUri, mapping.Name, mapping.XmpForm, items);
        SyncXmp.WriteFlash(edits.Xmp, items);
        return true;
    }

    // Turn target-form refusals into sync notes so one incompatible property does not discard other edits.
    private static bool ToXmp(SyncEdits edits, SyncXmp view, string source, string path, string uri, string name, XmpValueKind form, string[] items)
    {
        try { view.Write(edits.Xmp, path, uri, name, form, items); return true; }
        catch (ArgumentException e) { edits.Note(source, path, e.Message); return false; }
    }

    // Preserve the specific conversion reason while removing edit-layer advice from user-facing sync notes.
    private static bool ToExif(SyncEdits edits, string source, ExifXmpMapping mapping, string[] items, ExifMetadata? existing)
    {
        try { SyncExif.Write(edits.Exif, mapping, items, existing); return true; }
        catch (ArgumentException e)
        {
            // The reason alone: the advice of the edit and the parameter name are for its caller.
            string reason = e.Message;
            int advice = reason.IndexOf(" SetUnchecked", StringComparison.Ordinal);
            if (advice < 0) advice = reason.IndexOf(" (Parameter '", StringComparison.Ordinal);
            edits.Note(source, SyncExif.Key(mapping.Tag), advice > 0 ? reason[..advice] : reason);
            return false;
        }
    }

    // Record dataset validation failures per property rather than aborting the remaining conversions.
    private static bool ToIptc(SyncEdits edits, string source, IptcXmpMapping mapping, string[] items)
    {
        try { SyncIptc.Write(edits.Iptc, mapping, items); return true; }
        catch (ArgumentException e) { edits.Note(source, SyncIptc.Key(mapping.Dataset), e.Message); return false; }
    }
}
