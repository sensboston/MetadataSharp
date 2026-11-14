using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats;

/// <summary>
/// The metadata of an image a file carries inside itself: the JPEG a camera raw file embeds as its
/// preview, for example. It is a read-only snapshot of another file's metadata, not a second view of
/// the container's own, and nothing here can be written back: a writer that reached it would change
/// bytes the container indexes rather than bytes the container owns.
/// </summary>
/// <remarks>
/// The snapshot has no writer: it is a value read from the container, and a caller that wants to change
/// what it holds edits the embedded image as a file of its own. The metadata of an embedded image is
/// read the way <see cref="ImageMetadata.Read(ReadOnlySpan{byte}, MetadataLimits?)"/>
/// reads any file, from the preview's own byte range, so a file whose preview carries no metadata
/// has none. Elements keep the directory roles they have inside the embedded file: an
/// <see cref="ExifDirectoryKind.Ifd0"/> tag of this snapshot is the embedded image's primary
/// directory, which is a different directory from the container's primary one.
/// </remarks>
public sealed class EmbeddedImageMetadata
{
    private readonly ImageMetadata metadata;

    private EmbeddedImageMetadata(PreviewImage source, ImageMetadata metadata)
    {
        Source = source;
        this.metadata = metadata;
    }

    /// <summary>Which preview of the container the snapshot was read from.</summary>
    public PreviewImage Source { get; }

    /// <summary>The embedded image's EXIF metadata, or null when it carries none this library reads.</summary>
    public ExifMetadata? Exif => metadata.Exif;

    /// <summary>The embedded image's IPTC-IIM datasets, or null.</summary>
    public IptcPacket? Iptc => metadata.Iptc;

    /// <summary>The embedded image's XMP packet, or null.</summary>
    public XmpPacket? Xmp => metadata.Xmp;

    /// <summary>The embedded image's comments, in file order.</summary>
    public IReadOnlyList<string> Comments => metadata.Comments;

    /// <summary>
    /// Read the metadata of the first image a file embeds that carries any, previews from smallest to
    /// largest. A file with no preview, a preview whose bytes cannot be extracted, and a preview with
    /// no metadata yield null rather than a refusal: the container itself is what
    /// <see cref="ImageMetadata.Read(ReadOnlySpan{byte}, MetadataLimits?)"/> reads, and this is what it
    /// carries.
    /// </summary>
    /// <param name="file">Complete input file bytes starting at the container signature.</param>
    /// <param name="limits">Resource budgets applied while reading; null selects the default limits.</param>
    /// <returns>The snapshot, or null when no embedded image of the file carries metadata.</returns>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.LimitExceeded"/> when the file exceeds the input budget.</exception>
    public static EmbeddedImageMetadata? Read(ReadOnlySpan<byte> file, MetadataLimits? limits = null) =>
        ReadFirst(file, limits);

    /// <summary>
    /// Read the metadata of one named preview of a file. A preview the file does not list, or one whose
    /// bytes or metadata cannot be read, yields null.
    /// </summary>
    /// <param name="file">Complete input file bytes starting at the container signature.</param>
    /// <param name="preview">The preview to read, from <see cref="Previews.List"/>.</param>
    /// <param name="limits">Resource budgets applied while reading; null selects the default limits.</param>
    /// <returns>The snapshot of that preview's metadata, or null when it carries none.</returns>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.LimitExceeded"/> when the file exceeds the input budget.</exception>
    public static EmbeddedImageMetadata? Read(ReadOnlySpan<byte> file, PreviewImage preview, MetadataLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return TryRead(file, preview, limits);
    }

    private static EmbeddedImageMetadata? ReadFirst(ReadOnlySpan<byte> file, MetadataLimits? limits)
    {
        IReadOnlyList<PreviewImage> previews = Previews.List(file, limits);
        foreach (PreviewImage preview in previews)
        {
            if (TryRead(file, preview, limits) is EmbeddedImageMetadata found)
                return found;
        }
        return null;
    }

    private static EmbeddedImageMetadata? TryRead(ReadOnlySpan<byte> file, PreviewImage preview,
        MetadataLimits? limits)
    {
        byte[] bytes;
        try
        {
            bytes = Previews.Extract(file, preview, limits);
        }
        catch (MetadataException)
        {
            return null; // A preview this library cannot extract carries nothing it can report.
        }
        ImageMetadata metadata = ImageMetadata.Read(bytes, limits);
        bool empty = metadata.Exif is null && metadata.Iptc is null && metadata.Xmp is null &&
            metadata.Comments.Count == 0;
        return empty ? null : new EmbeddedImageMetadata(preview, metadata);
    }
}
