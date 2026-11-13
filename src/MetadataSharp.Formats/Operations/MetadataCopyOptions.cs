using MetadataSharp.Core;
using MetadataSharp.Core.Sync;

namespace MetadataSharp.Formats.Operations;

/// <summary>Metadata families selected for copying from the source.</summary>
[Flags]
public enum MetadataCopyFamilies
{
    /// <summary>No metadata.</summary>
    None = 0,
    /// <summary>EXIF tags and the MakerNote when safely relocatable.</summary>
    Exif = 1,
    /// <summary>The IPTC/IIM datasets.</summary>
    Iptc = 2,
    /// <summary>The XMP packet.</summary>
    Xmp = 4,
    /// <summary>The ICC profile.</summary>
    Icc = 8,
    /// <summary>Free-text comments.</summary>
    Comment = 16,
    /// <summary>Every supported family.</summary>
    All = Exif | Iptc | Xmp | Icc | Comment
}

/// <summary>How the MakerNote is handled when EXIF is selected.</summary>
public enum MetadataMakerNoteMode
{
    /// <summary>Copy it only when its addressing and dependent cipher keys can be preserved safely.</summary>
    Copy,
    /// <summary>Leave the destination MakerNote alone (or absent) and do not attempt to copy it.</summary>
    Skip
}

/// <summary>Whether copied families are reconciled by MetadataSharp's existing MWG sync rules.</summary>
public enum MetadataCopySync
{
    /// <summary>Copy selected families without reconciling their overlapping values.</summary>
    None,
    /// <summary>Apply <see cref="MetadataSync.Reconcile"/> to source metadata before copying.</summary>
    ExistingRules
}

/// <summary>Options for <see cref="MetadataOperations.CopyMetadata(string, string, MetadataCopyOptions?)"/> and its byte overload.</summary>
public sealed record MetadataCopyOptions
{
    /// <summary>Families copied from the source. Destination-only entries are retained, matching Exiv2 insert behavior. Defaults to all families.</summary>
    public MetadataCopyFamilies Families { get; init; } = MetadataCopyFamilies.All;
    /// <summary>Whether a safely relocatable MakerNote is copied. Defaults to <see cref="MetadataMakerNoteMode.Copy"/>.</summary>
    public MetadataMakerNoteMode MakerNote { get; init; } = MetadataMakerNoteMode.Copy;
    /// <summary>Whether source metadata is reconciled by existing sync rules before it is copied.</summary>
    public MetadataCopySync Sync { get; init; }
    /// <summary>Resource budgets used while reading and preparing the copy.</summary>
    public MetadataLimits Limits { get; init; } = new();
}

/// <summary>Why a metadata family or MakerNote was skipped or the copy was refused.</summary>
public enum MetadataCopyReason
{
    /// <summary>The source does not contain that family.</summary>
    SourceFamilyAbsent,
    /// <summary>The destination format has no supported place for that family.</summary>
    DestinationDoesNotSupportFamily,
    /// <summary>The requested family could not be read safely from the source.</summary>
    MalformedSourceFamily,
    /// <summary>The destination could not be read or written safely.</summary>
    DestinationRefused,
    /// <summary>MakerNote copying was disabled by the options.</summary>
    MakerNoteCopyDisabled,
    /// <summary>No decoded MakerNote is available to prove that relocation is safe.</summary>
    MakerNoteNotDecoded,
    /// <summary>A MakerNote has offsets or layout that cannot be safely rebased.</summary>
    UnsafeMakerNoteRelocation,
    /// <summary>Cipher-dependent MakerNote data lacks keys that are copied with it.</summary>
    MakerNoteCipherKeysUnavailable,
    /// <summary>A source value could not be represented by the destination writer.</summary>
    ValueNotRepresentable,
    /// <summary>A sync rule could not represent a value in one of the destination families.</summary>
    SyncValueNotRepresentable,
    /// <summary>The destination writer refused the planned copy; no destination bytes were committed.</summary>
    CopyRefused
}

/// <summary>A structured reason why part of a metadata copy was skipped or the operation refused.</summary>
/// <param name="Reason">The stable reason code.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="Family">The affected family, or null when the whole operation was refused.</param>
public sealed record MetadataCopyNote(MetadataCopyReason Reason, string Message, MetadataCopyFamilies? Family = null);

/// <summary>The outcome of a transactional metadata copy.</summary>
public sealed class MetadataCopyResult
{
    // Freeze the copy outcome, copied-family mask and any refusal notes.
    internal MetadataCopyResult(byte[] destinationBytes, MetadataCopyFamilies copiedFamilies, IReadOnlyList<MetadataCopyNote> notes, bool refused)
    {
        DestinationBytes = destinationBytes;
        CopiedFamilies = copiedFamilies;
        Notes = notes;
        WasRefused = refused;
    }

    /// <summary>The new destination bytes, or the original destination bytes when the operation was refused.</summary>
    public byte[] DestinationBytes { get; }
    /// <summary>Families successfully copied to the destination.</summary>
    public MetadataCopyFamilies CopiedFamilies { get; }
    /// <summary>Structured reasons for skipped families, MakerNotes, sync values, or a refusal.</summary>
    public IReadOnlyList<MetadataCopyNote> Notes { get; }
    /// <summary>True when the plan was refused and <see cref="DestinationBytes"/> is unchanged.</summary>
    public bool WasRefused { get; }
}
