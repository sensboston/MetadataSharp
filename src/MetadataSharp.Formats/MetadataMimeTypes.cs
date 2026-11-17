namespace MetadataSharp.Formats;

/// <summary>
/// The media type of a file, from the signature its first bytes state: the word <c>exiv2 -p s</c>
/// prints after <c>MIME type</c>.
/// </summary>
/// <remarks>
/// The type names the container as the signature tells it apart, not the camera's file extension: a
/// TIFF structure is <c>image/tiff</c> whatever raw format was written around it, because the
/// signature of CR2, NEF, ARW, PEF, ORF and RW2 is a TIFF header. A file whose signature the readers
/// know nothing about is <c>application/octet-stream</c>.
/// </remarks>
public static class MetadataMimeTypes
{
    /// <summary>The media type of a signature.</summary>
    /// <param name="format">The format a signature was recognised as.</param>
    /// <returns>The media type, <c>application/octet-stream</c> for <see cref="MetadataFormat.Unknown"/>.</returns>
    public static string For(MetadataFormat format) => format switch
    {
        MetadataFormat.Jpeg => "image/jpeg",
        MetadataFormat.Png => "image/png",
        MetadataFormat.WebP => "image/webp",
        MetadataFormat.Tiff => "image/tiff",
        MetadataFormat.Psd => "image/photoshop",
        MetadataFormat.Exv => "application/exv",
        MetadataFormat.Jp2 => "image/jp2",
        MetadataFormat.XmpSidecar => "application/rdf+xml",
        MetadataFormat.Cr3 => "image/x-canon-cr3",
        MetadataFormat.Bmff => "image/heif",
        MetadataFormat.Jxl => "image/jxl",
        MetadataFormat.Eps => "application/postscript",
        MetadataFormat.Pgf => "image/pgf",
        MetadataFormat.Crw => "image/x-canon-crw",
        MetadataFormat.Mrw => "image/x-minolta-mrw",
        MetadataFormat.Raf => "image/x-fuji-raf",
        MetadataFormat.Gif => "image/gif",
        MetadataFormat.Bmp => "image/bmp",
        MetadataFormat.Tga => "image/x-tga",
        MetadataFormat.Asf => "video/x-ms-asf",
        MetadataFormat.Matroska => "video/x-matroska",
        MetadataFormat.QuickTime => "video/quicktime",
        MetadataFormat.Riff => "video/x-msvideo",
        MetadataFormat.Dcp => "application/x-dcp",
        _ => "application/octet-stream",
    };
}
