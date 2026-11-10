using System.Collections.ObjectModel;

namespace MetadataSharp.Core;

/// <summary>Container/file categories for capability discovery. Unknown = 0.
/// Members cover the 31 documented v0.28.9 baseline category rows; the XMP
/// row is represented as XmpSidecar. Every documented category is present so
/// "not supported" is an explicit answer, not an absence of information.</summary>
public enum MetadataContainer
{
    /// <summary>No container information; never assumed supported.</summary>
    Unknown = 0,
    /// <summary>JPEG (JFIF/Exif in APP segments).</summary>
    Jpeg,
    /// <summary>PNG (static images only in this build).</summary>
    Png,
    /// <summary>WebP (static images only in this build).</summary>
    WebP,
    /// <summary>Standalone classic TIFF (baseline II/MM, non-BigTIFF).</summary>
    Tiff,
    /// <summary>Digital Negative (DNG); no implementation in this build.</summary>
    Dng,
    /// <summary>Sony ARW.</summary>
    Arw,
    /// <summary>AVIF (BMFF-based).</summary>
    Avif,
    /// <summary>BMP bitmap.</summary>
    Bmp,
    /// <summary>Canon CR2 (TIFF-derived RAW).</summary>
    Cr2,
    /// <summary>Canon CR3 (BMFF-based RAW).</summary>
    Cr3,
    /// <summary>Canon CRW/CIFF (legacy RAW).</summary>
    Crw,
    /// <summary>DNG Camera Profile (DCP).</summary>
    Dcp,
    /// <summary>Encapsulated PostScript.</summary>
    Eps,
    /// <summary>Adobe EXV.</summary>
    Exv,
    /// <summary>CompuServe GIF.</summary>
    Gif,
    /// <summary>High Efficiency Image Coding (HEIC).</summary>
    Heic,
    /// <summary>High Efficiency Image File (HEIF).</summary>
    Heif,
    /// <summary>JPEG 2000.</summary>
    Jp2,
    /// <summary>JPEG XL.</summary>
    Jxl,
    /// <summary>Minolta MRW.</summary>
    Mrw,
    /// <summary>Nikon NEF (TIFF-derived RAW).</summary>
    Nef,
    /// <summary>Olympus ORF (TIFF-derived RAW).</summary>
    Orf,
    /// <summary>Pentax PEF (TIFF-derived RAW).</summary>
    Pef,
    /// <summary>Photoshop PGF.</summary>
    Pgf,
    /// <summary>Photoshop PSD/PSB.</summary>
    Psd,
    /// <summary>Fujifilm RAF.</summary>
    Raf,
    /// <summary>Panasonic RW2 (TIFF-derived RAW).</summary>
    Rw2,
    /// <summary>Sony SR2 (TIFF-derived RAW).</summary>
    Sr2,
    /// <summary>Samsung SRW (TIFF-derived RAW).</summary>
    Srw,
    /// <summary>Targa TGA.</summary>
    Tga,
    /// <summary>XMP packet or .xmp sidecar (not a pixel container).</summary>
    XmpSidecar
}

/// <summary>Metadata family granularity used for capability rows.</summary>
public enum MetadataFamily
{
    /// <summary>EXIF (including GPS/Interop sub-IFDs).</summary>
    Exif = 0,
    /// <summary>IPTC Information Interchange Model (IIM).</summary>
    IptcIim,
    /// <summary>XMP packet (RDF) representation.</summary>
    Xmp,
    /// <summary>ICC colour profile (preservation exists in some paths; no public API in this build).</summary>
    Icc,
    /// <summary>Free-text comment (e.g. JPEG COM); no public API in this build.</summary>
    Comment,
    /// <summary>Embedded thumbnail; no general public API in this build.</summary>
    Thumbnail,
    /// <summary>Embedded preview; no general public API in this build.</summary>
    Preview
}

/// <summary>User-visible operation classes; mirrors the E01 ledger axes.</summary>
public enum MetadataOperation
{
    /// <summary>Read/inspect only; no modification.</summary>
    Read = 0,
    /// <summary>Add metadata that was absent.</summary>
    Insert,
    /// <summary>Replace existing metadata values.</summary>
    Replace,
    /// <summary>Remove metadata (per item where applicable, or whole family via the approved whole-removal APIs).</summary>
    Remove,
    /// <summary>Extract embedded resources (previews/thumbnails/ICC); not exposed as an API in this build.</summary>
    Extract
}

/// <summary>Fail-closed support status for one capability row.</summary>
public enum CapabilityStatus
{
    /// <summary>Not available; this is the default and the answer for absent rows.</summary>
    NotSupported = 0,
    /// <summary>Implemented for a restricted subset; see the row note and the shipped contracts.</summary>
    Partial,
    /// <summary>Implemented for the documented family within finite limits; unsupported structures are refused.</summary>
    Supported
}

/// <summary>Immutable description of one (container, family, operation) row.
/// Note is a bounded, compile-time constant explaining partiality or scope;
/// null when nothing needs qualifying.</summary>
public readonly record struct CapabilityDescriptor(
    MetadataContainer Container,
    MetadataFamily Family,
    MetadataOperation Operation,
    CapabilityStatus Status,
    string? Note);

/// <summary>Model-driven capability table. Static, immutable, deterministic;
/// no reflection probing, no machine-dependent behavior, no I/O. A row is
/// Supported/Partial only with test evidence in this repository; anything
/// else is NotSupported. Missing rows are NotSupported by contract.
/// Rows are declared in stable sorted (Container, Family, Operation) order.</summary>
public static class MetadataCapabilities
{
    private const int MaxNoteLength = 240;

    // Data, not code: mirror of shipped, test-evidenced capability. Change only
    // together with the tests that pin it, in the same reviewed unit.
    private static readonly CapabilityDescriptor[] manifest =
    [
        // JPEG: every frame type and scan count; classic II/MM TIFF/EXIF.
        new(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "Classic TIFF/EXIF within any JPEG frame type or scan count; unsupported layouts are refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset only (descriptive IFD0/EXIF/GPS tags); unsupported tags are refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset only (descriptive IFD0/EXIF/GPS tags); unsupported tags are refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        new(MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM within the approved IPTC usage contract; declared or UTF-8 encoding; finite packet limits enforced."),
        new(MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved IPTC dataset usage subset; unsupported records are refused, not silently rewritten."),
        new(MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved IPTC dataset usage subset; unsupported records are refused, not silently rewritten."),
        new(MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset."),
        new(MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Bounded restricted XMP profile (UTF-8, finite packet limits); raw bytes retained for unmodeled constructs."),
        new(MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        new(MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal is also supported."),
        // PNG: static images only; CRC/chunk policy enforced per the approved contract.
        new(MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "eXIf chunk in static PNG within the approved chunk policy; unsupported layouts are refused."),
        new(MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "eXIf in static PNG; approved writable tag subset only; unsupported tags are refused."),
        new(MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "eXIf in static PNG; approved writable tag subset only; unsupported tags are refused."),
        new(MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "eXIf in static PNG; per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        new(MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static PNG within the approved chunk/text policy; raw bytes retained for unmodeled constructs."),
        new(MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static PNG; bounded restricted profile; Extended XMP is refused."),
        new(MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static PNG; bounded restricted profile; Extended XMP is refused."),
        new(MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static PNG; per-property removal within the restricted profile; whole-XMP removal is also supported."),
        // WebP: static images only.
        new(MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "EXIF in static (non-animated) WebP within the approved RIFF policy; unsupported layouts are refused."),
        new(MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "EXIF in static WebP; approved writable tag subset only; unsupported tags are refused."),
        new(MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "EXIF in static WebP; approved writable tag subset only; unsupported tags are refused."),
        new(MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "EXIF in static WebP; per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        new(MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static WebP within the approved RIFF policy; raw bytes retained for unmodeled constructs."),
        new(MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static WebP; bounded restricted profile; Extended XMP is refused."),
        new(MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static WebP; bounded restricted profile; Extended XMP is refused."),
        new(MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static WebP; per-property removal within the restricted profile; whole-XMP removal is also supported."),
        // Standalone classic TIFF: range accounting + restricted append-preserve edit.
        new(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "Standalone classic (non-BigTIFF) TIFF only; read-only range accounting and raw values, not general TIFF support."),
        new(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Ten descriptive IFD0 tags via append-preserve rebuild; obsolete bytes retained; unsupported schemas refused."),
        new(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Ten descriptive IFD0 tags via append-preserve rebuild; obsolete bytes retained; unsupported schemas refused."),
        // XMP packet/sidecar: restricted profile, no container framing.
        new(MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Load/save of XMP packets and .xmp sidecars within the bounded restricted profile."),
        new(MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        new(MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        new(MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal is also supported."),
    ];

    /// <summary>All declared (non-NotSupported) capability rows in stable sorted
    /// (Container, Family, Operation) order. Absent rows are NotSupported.</summary>
    public static IReadOnlyList<CapabilityDescriptor> All { get; } = Array.AsReadOnly(manifest);

    private static readonly Dictionary<(MetadataContainer Container, MetadataFamily Family, MetadataOperation Operation), CapabilityDescriptor> index = CreateIndex();

    /// <summary>Fail-closed lookup; false when the row is absent. Undefined enum
    /// values are reported as not supported rather than rejected.</summary>
    public static bool TryGet(MetadataContainer container, MetadataFamily family,
        MetadataOperation operation, out CapabilityDescriptor descriptor)
    {
        if (index.TryGetValue((container, family, operation), out CapabilityDescriptor found))
        {
            descriptor = found;
            return true;
        }
        descriptor = default;
        return false;
    }

    /// <summary>Convenience; returns NotSupported when the row is absent. Undefined
    /// enum values are reported as NotSupported rather than rejected.</summary>
    public static CapabilityStatus GetStatus(MetadataContainer container,
        MetadataFamily family, MetadataOperation operation) =>
        index.TryGetValue((container, family, operation), out CapabilityDescriptor descriptor)
            ? descriptor.Status
            : CapabilityStatus.NotSupported;

    private static Dictionary<(MetadataContainer Container, MetadataFamily Family, MetadataOperation Operation), CapabilityDescriptor> CreateIndex()
    {
        var map = new Dictionary<(MetadataContainer Container, MetadataFamily Family, MetadataOperation Operation), CapabilityDescriptor>(manifest.Length);
        for (int i = 0; i < manifest.Length; i++)
        {
            CapabilityDescriptor row = manifest[i];
            if (row.Status == CapabilityStatus.NotSupported)
                throw new InvalidOperationException("NotSupported rows must be omitted from the manifest; absence already means NotSupported.");
            if (!Enum.IsDefined(row.Container) || !Enum.IsDefined(row.Family) || !Enum.IsDefined(row.Operation))
                throw new InvalidOperationException("The capability manifest contains an undefined enum value.");
            if (row.Note is { Length: > MaxNoteLength })
                throw new InvalidOperationException("Capability notes must be at most 240 characters.");
            if (i > 0 && Compare(manifest[i - 1], row) >= 0)
                throw new InvalidOperationException("The capability manifest must be strictly ordered by (Container, Family, Operation).");
            map.Add((row.Container, row.Family, row.Operation), row);
        }
        return map;
    }

    private static int Compare(in CapabilityDescriptor left, in CapabilityDescriptor right)
    {
        int result = left.Container.CompareTo(right.Container);
        if (result != 0) return result;
        result = left.Family.CompareTo(right.Family);
        if (result != 0) return result;
        return left.Operation.CompareTo(right.Operation);
    }
}
