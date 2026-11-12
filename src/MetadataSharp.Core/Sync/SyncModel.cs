using System.Collections.ObjectModel;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Core.Sync;

/// <summary>What a conversion does with a field its target already holds.</summary>
public enum SyncMode
{
    /// <summary>The value of the source replaces the value of the target.</summary>
    Overwrite,
    /// <summary>A field the target holds is left alone; only fields it lacks are written.</summary>
    FillMissing
}

/// <summary>How a conversion of <see cref="MetadataSync"/> treats the target and the source.</summary>
public sealed class SyncOptions
{
    /// <summary>What happens to a field the target already holds. The default overwrites it.</summary>
    public SyncMode Mode { get; init; }

    /// <summary>
    /// When true, every field that was converted is also removed from its source, with the companion
    /// tags that were folded into the value (sub-seconds, time zone offsets, GPS references and the
    /// GPS date). A field that was not converted stays in the source.
    /// </summary>
    public bool EraseSource { get; init; }
}

/// <summary>How an EXIF value and an XMP value are converted into each other.</summary>
public enum SyncConversion
{
    /// <summary>ASCII text; in a tag that holds several strings, the first.</summary>
    Text,
    /// <summary>ASCII text that lists names separated by semicolons, one array item for each name.</summary>
    TextList,
    /// <summary>Integers of any integer type or UNDEFINED bytes, written in decimal.</summary>
    Integer,
    /// <summary>RATIONAL or SRATIONAL, written <c>numerator/denominator</c>.</summary>
    Rational,
    /// <summary>
    /// <c>YYYY:MM:DD HH:MM:SS</c> with the sub-second and time zone offset tags of the same moment,
    /// and an XMP date <c>YYYY-MM-DDThh:mm:ss[.s][±hh:mm]</c>.
    /// </summary>
    Date,
    /// <summary>GPSTimeStamp with GPSDateStamp, and an XMP date in UTC.</summary>
    GpsTime,
    /// <summary>Three rationals with the reference letter of the tag before, and <c>DDD,MM.mmmmmmmK</c>.</summary>
    GpsCoordinate,
    /// <summary>Four UNDEFINED bytes of ASCII digits, and the same four characters.</summary>
    Version,
    /// <summary>Four bytes, and their numbers joined by dots.</summary>
    GpsVersion,
    /// <summary>The Flash bit field, and the five fields of the <c>exif:Flash</c> structure.</summary>
    Flash,
    /// <summary>UNDEFINED bytes that start with an eight-byte character code, and text.</summary>
    Comment
}

/// <summary>One row of the EXIF and XMP mapping table.</summary>
public sealed class ExifXmpMapping
{
    internal ExifXmpMapping(ExifDirectoryKind directory, ushort tag, string prefix, string name,
        SyncConversion conversion, XmpValueKind form, TiffFieldType type)
    {
        Tag = new(directory, tag);
        Prefix = prefix;
        Name = name;
        Conversion = conversion;
        XmpForm = form;
        FieldType = type;
        NamespaceUri = SyncXmp.Uri(prefix);
    }

    internal string Prefix { get; }
    internal string Name { get; }
    internal string NamespaceUri { get; }

    /// <summary>The EXIF tag.</summary>
    public ExifTagKey Tag { get; }
    /// <summary>The XMP property, as a path an <see cref="XmpEdit"/> takes: for example <c>Xmp.tiff.Make</c>.</summary>
    public string XmpPath => "Xmp." + Prefix + "." + Name;
    /// <summary>The XMP property by namespace and local name.</summary>
    public XmpPropertyName Property => new(NamespaceUri, Name);
    /// <summary>How the two values are converted into each other.</summary>
    public SyncConversion Conversion { get; }
    /// <summary>
    /// The form the XMP property is written in. <see cref="SyncConversion.Flash"/> writes a structure
    /// and reports <see cref="XmpValueKind.Text"/> for its fields.
    /// </summary>
    public XmpValueKind XmpForm { get; }
    /// <summary>
    /// The field type the EXIF tag is written with. A tag that the file already holds as SHORT or LONG
    /// keeps that type when the other of the two is listed here.
    /// </summary>
    public TiffFieldType FieldType { get; }
}

/// <summary>One row of the IPTC and XMP mapping table.</summary>
public sealed class IptcXmpMapping
{
    internal IptcXmpMapping(byte dataset, string prefix, string name, XmpValueKind form, byte timeDataset = 0)
    {
        Dataset = new(2, dataset);
        TimeDataset = timeDataset == 0 ? null : new IptcDatasetKey(2, timeDataset);
        Prefix = prefix;
        Name = name;
        XmpForm = form;
        NamespaceUri = SyncXmp.Uri(prefix);
    }

    internal string Prefix { get; }
    internal string Name { get; }
    internal string NamespaceUri { get; }

    /// <summary>The IIM dataset.</summary>
    public IptcDatasetKey Dataset { get; }
    /// <summary>
    /// For a date, the dataset that holds the time of day of the same moment; null for every other
    /// row. The two datasets together are one XMP date.
    /// </summary>
    public IptcDatasetKey? TimeDataset { get; }
    /// <summary>The XMP property, as a path an <see cref="XmpEdit"/> takes: for example <c>Xmp.dc.subject</c>.</summary>
    public string XmpPath => "Xmp." + Prefix + "." + Name;
    /// <summary>The XMP property by namespace and local name.</summary>
    public XmpPropertyName Property => new(NamespaceUri, Name);
    /// <summary>The form the XMP property is written in.</summary>
    public XmpValueKind XmpForm { get; }
}

/// <summary>A field a conversion found in its source and did not write, and why.</summary>
/// <param name="Source">The key of the field in the source: <c>Exif.Ifd0.0x010F</c>, <c>Iptc.2:25</c> or an XMP path.</param>
/// <param name="Target">The key of the field in the target, in the same notation.</param>
/// <param name="Reason">What kept the value from being written.</param>
public sealed record SyncNote(string Source, string Target, string Reason);

/// <summary>One of the three metadata families.</summary>
public enum SyncSource
{
    /// <summary>EXIF.</summary>
    Exif,
    /// <summary>IPTC IIM.</summary>
    Iptc,
    /// <summary>XMP.</summary>
    Xmp
}

/// <summary>The family whose value <see cref="MetadataSync.Reconcile"/> took for a field held by more than one.</summary>
/// <param name="XmpPath">The field, named by its XMP property: for example <c>Xmp.dc.description</c>.</param>
/// <param name="Source">The family whose value was taken.</param>
public sealed record SyncChoice(string XmpPath, SyncSource Source);

/// <summary>What the stored digest of the IPTC stream (Photoshop resource 0x0425) says about the stream.</summary>
public enum IptcDigestState
{
    /// <summary>The file has no digest, or no IPTC stream.</summary>
    Absent,
    /// <summary>The digest is the MD5 of the stream: nothing changed IPTC since XMP was brought into agreement.</summary>
    Current,
    /// <summary>The digest is not the MD5 of the stream: IPTC was changed by something that left XMP alone.</summary>
    Stale
}

/// <summary>How <see cref="MetadataSync.Reconcile"/> writes.</summary>
public sealed class ReconcileOptions
{
    /// <summary>
    /// When true, an IPTC stream is written into a file that has none. The default follows the MWG
    /// guidance: IPTC is kept in agreement where it exists and is not created.
    /// </summary>
    public bool CreateIptc { get; init; }

    /// <summary>When true, an EXIF block is written into a file that has none. The default does not create one.</summary>
    public bool CreateExif { get; init; }
}

/// <summary>
/// The changes a conversion asks for, one edit for each family. An edit that has nothing to do is
/// empty; nothing is written until the edits are given to a document or applied to a packet.
/// </summary>
public sealed class SyncEdits
{
    private readonly List<SyncNote> notes = [];
    private readonly List<SyncChoice> choices = [];

    internal SyncEdits()
    {
        Notes = new ReadOnlyCollection<SyncNote>(notes);
        Choices = new ReadOnlyCollection<SyncChoice>(choices);
    }

    /// <summary>The changes to EXIF.</summary>
    public ExifEdit Exif { get; } = new();
    /// <summary>The changes to IPTC.</summary>
    public IptcEdit Iptc { get; } = new();
    /// <summary>The changes to XMP.</summary>
    public XmpEdit Xmp { get; } = new();
    /// <summary>True when no edit holds a change.</summary>
    public bool IsEmpty => Exif.IsEmpty && Iptc.IsEmpty && Xmp.IsEmpty;
    /// <summary>The fields that were found and not written, in the order of the mapping table.</summary>
    public IReadOnlyList<SyncNote> Notes { get; }
    /// <summary>
    /// For <see cref="MetadataSync.Reconcile"/>, the family that was taken for every field more than
    /// one family holds; empty for the one-way conversions.
    /// </summary>
    public IReadOnlyList<SyncChoice> Choices { get; }

    internal void Note(string source, string target, string reason) => notes.Add(new(source, target, reason));
    internal void Choose(string path, SyncSource source) => choices.Add(new(path, source));
}
