using System.Globalization;
using System.Text.RegularExpressions;
using MetadataSharp.Core;
using MetadataSharp.Formats;

namespace MetaTool.Cli;

/// <summary>The print action: the report, the listing, the previews, the comment and the raw payloads.</summary>
internal static class PrintAction
{
    /// <summary>Prints every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(Session session)
    {
        List<Regex>? patterns = Compile(session);
        if (patterns is null) return ExitCodes.Usage;
        return session.ForEachFile(path => One(session, path, patterns));
    }

    private static List<Regex>? Compile(Session session)
    {
        var patterns = new List<Regex>();
        foreach (string pattern in session.Options.Patterns)
        {
            try
            {
                patterns.Add(new Regex(pattern, RegexOptions.CultureInvariant));
            }
            catch (ArgumentException exception)
            {
                session.Warn($"invalid pattern {pattern}: {exception.Message}");
                return null;
            }
        }
        return patterns;
    }

    private static int One(Session session, string path, List<Regex> patterns)
    {
        byte[] bytes = session.ReadFile(path);
        ImageMetadata image = ImageMetadata.Read(bytes);
        foreach (MetadataReadError error in image.Errors) session.Warn($"{path}: {error}");
        return session.Options.Mode switch
        {
            PrintMode.Summary => Reports.Summary(session, path, bytes, image),
            PrintMode.Comment => Reports.Comment(session, path, image),
            PrintMode.Preview => Reports.Previews(session, path, image),
            PrintMode.Icc => Profile(session, path, image),
            PrintMode.XmpPacket => Packet(session, path, image),
            _ => Reports.List(session, path, image, patterns),
        };
    }

    private static int Profile(Session session, string path, ImageMetadata image)
    {
        if (image.IccProfile.IsEmpty)
        {
            session.Warn($"{path}: the file has no ICC profile");
            return ExitCodes.Absent;
        }
        session.WriteStandardOutput(image.IccProfile.Span);
        return ExitCodes.Ok;
    }

    private static int Packet(Session session, string path, ImageMetadata image)
    {
        if (image.Xmp is not { } packet)
        {
            session.Warn($"{path}: the file has no XMP packet");
            return ExitCodes.Absent;
        }
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        session.WriteStandardOutput(bytes);
        return ExitCodes.Ok;
    }
}

/// <summary>The four reports the print action writes, in the layout of the reference.</summary>
internal static class Reports
{
    private const int LabelWidth = 16;

    /// <summary>Writes one row per tag, dataset and property, in the columns <c>-P</c> chose.</summary>
    /// <param name="session">The run.</param>
    /// <param name="path">The file.</param>
    /// <param name="image">Its metadata.</param>
    /// <param name="patterns">The <c>-g</c> expressions, already compiled.</param>
    /// <returns>The exit code: <see cref="ExitCodes.Failed"/> when a filter matched nothing.</returns>
    internal static int List(Session session, string path, ImageMetadata image, List<Regex> patterns)
    {
        Options options = session.Options;
        PrintColumns columns = options.EffectiveColumns;
        PrintFamilies families = options.EffectiveFamilies;
        string? prefix = session.MultiFile ? path : null;
        bool printed = false;

        if (families.HasFlag(PrintFamilies.Exif) && image.Exif is { } exif)
            foreach (MetadataRow row in Rows.Exif(exif))
                printed |= Emit(session, row, columns, prefix, patterns);
        if (families.HasFlag(PrintFamilies.Iptc) && image.Iptc is { } iptc)
            foreach (MetadataRow row in Rows.Iptc(iptc))
                printed |= Emit(session, row, columns, prefix, patterns);
        if (families.HasFlag(PrintFamilies.Xmp) && image.Xmp is { } xmp)
            foreach (MetadataRow row in Rows.Xmp(xmp))
                printed |= Emit(session, row, columns, prefix, patterns);

        if (options.Verbose)
        {
            if (families.HasFlag(PrintFamilies.Exif) && image.Exif is null) session.Warn($"{path}: no Exif data found in the file");
            if (families.HasFlag(PrintFamilies.Iptc) && image.Iptc is null) session.Warn($"{path}: no IPTC data found in the file");
            if (families.HasFlag(PrintFamilies.Xmp) && image.Xmp is null) session.Warn($"{path}: no XMP data found in the file");
        }

        bool filtered = options.ExactKeys.Count > 0 || patterns.Count > 0;
        return filtered && !printed ? ExitCodes.Failed : ExitCodes.Ok;
    }

    /// <summary>Writes the report of <c>-p s</c>.</summary>
    /// <param name="session">The run.</param>
    /// <param name="path">The file.</param>
    /// <param name="bytes">Its bytes.</param>
    /// <param name="image">Its metadata.</param>
    /// <returns>The exit code: <see cref="ExitCodes.Absent"/> when the file has no EXIF block.</returns>
    internal static int Summary(Session session, string path, byte[] bytes, ImageMetadata image)
    {
        TextWriter output = session.Output;
        string prefix = session.MultiFile ? RowWriter.Pad(path, 20) + " " : string.Empty;

        void Field(string label, string value) => output.Write(prefix + RowWriter.Pad(label, LabelWidth) + ": " + value + "\n");

        Field("File name", path);
        Field("File size", bytes.Length.ToString(CultureInfo.InvariantCulture) + " Bytes");
        Field("MIME type", image.MimeType);
        Field("Image size", Size(bytes, image));

        if (image.Exif is not { } exif)
        {
            session.Warn($"{path}: no Exif data found in the file");
            output.Write('\n');
            return ExitCodes.Absent;
        }

        PreviewImage? thumbnail = null;
        foreach (PreviewImage preview in image.Previews)
        {
            if (!preview.Source.StartsWith("Exif.Thumbnail", StringComparison.Ordinal)) continue;
            thumbnail = preview;
            break;
        }
        Field("Thumbnail", thumbnail is null ? "None" : thumbnail.MimeType + ", " + thumbnail.Length.ToString(CultureInfo.InvariantCulture) + " Bytes");

        Dictionary<string, string> values = Values(exif);
        string Get(string key, string? fallback = null)
        {
            if (values.TryGetValue(key, out string? text)) return text;
            return fallback is not null && values.TryGetValue(fallback, out string? other) ? other : string.Empty;
        }

        Field("Camera make", Get("Exif.Image.Make"));
        Field("Camera model", Get("Exif.Image.Model"));
        Field("Image timestamp", Get("Exif.Photo.DateTimeOriginal"));
        Field("File number", Get("Exif.Canon.FileNumber"));
        Field("Exposure time", Get("Exif.Photo.ExposureTime", "Exif.Photo.ShutterSpeedValue"));
        Field("Aperture", Get("Exif.Photo.FNumber", "Exif.Photo.ApertureValue"));
        Field("Exposure bias", Get("Exif.Photo.ExposureBiasValue"));
        Field("Flash", Get("Exif.Photo.Flash"));
        Field("Flash bias", Get("Exif.CanonSi.FlashBias", "Exif.CanonCs.FlashActivity"));
        Field("Focal length", Get("Exif.Photo.FocalLength"));
        Field("Subject distance", Get("Exif.Photo.SubjectDistance"));
        Field("ISO speed", Get("Exif.Photo.ISOSpeedRatings", "Exif.Photo.PhotographicSensitivity"));
        Field("Exposure mode", Get("Exif.Photo.ExposureMode"));
        Field("Metering mode", Get("Exif.Photo.MeteringMode"));
        Field("Macro mode", Get("Exif.CanonCs.Macro"));
        Field("Image quality", Get("Exif.CanonCs.Quality"));
        Field("White balance", Get("Exif.Photo.WhiteBalance"));
        Field("Copyright", Get("Exif.Image.Copyright"));
        Field("Exif comment", Get("Exif.Photo.UserComment"));
        output.Write('\n');
        return ExitCodes.Ok;
    }

    /// <summary>Writes the comment of <c>-p c</c>.</summary>
    /// <param name="session">The run.</param>
    /// <param name="path">The file.</param>
    /// <param name="image">Its metadata.</param>
    /// <returns>The exit code.</returns>
    internal static int Comment(Session session, string path, ImageMetadata image)
    {
        string prefix = session.MultiFile ? RowWriter.Pad(path, 20) + "  " : string.Empty;
        string label = session.Options.Verbose ? "JPEG comment: " : string.Empty;
        if (image.Comments.Count == 0) session.Output.Write(prefix + "\n");
        foreach (string comment in image.Comments) session.Output.Write(prefix + label + comment + "\n");
        return ExitCodes.Ok;
    }

    /// <summary>Writes the preview listing of <c>-p p</c>.</summary>
    /// <param name="session">The run.</param>
    /// <param name="path">The file.</param>
    /// <param name="image">Its metadata.</param>
    /// <returns>The exit code.</returns>
    internal static int Previews(Session session, string path, ImageMetadata image)
    {
        string prefix = session.MultiFile ? RowWriter.Pad(path, 20) + "  " : string.Empty;
        int number = 0;
        foreach (PreviewImage preview in image.Previews)
        {
            number++;
            string line = "Preview " + number.ToString(CultureInfo.InvariantCulture) + ": " + preview.MimeType + ", ";
            if (preview.Width is int width && preview.Height is int height && width != 0 && height != 0)
                line += width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture) + " pixels, ";
            line += preview.Length.ToString(CultureInfo.InvariantCulture) + " bytes";
            session.Output.Write(prefix + line + "\n");
        }
        return ExitCodes.Ok;
    }

    /// <summary>The text of every tag, keyed the way the reference looks it up in the summary.</summary>
    /// <param name="exif">The EXIF block, MakerNote included.</param>
    /// <returns>A lookup of translated values.</returns>
    internal static Dictionary<string, string> Values(ExifMetadata exif)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (MetadataRow row in Rows.Exif(exif)) values[row.Key] = row.Translated ?? row.Value;
        return values;
    }

    private static string Size(byte[] bytes, ImageMetadata image)
    {
        ImageSize? size = ImageGeometry.Read(bytes);
        if (size is null && image.Exif?.PixelWidth is int width && image.Exif?.PixelHeight is int height)
            size = new ImageSize(width, height);
        return size?.ToString() ?? "0 x 0";
    }

    private static bool Emit(Session session, MetadataRow row, PrintColumns columns, string? prefix, List<Regex> patterns)
    {
        if (!Match(session.Options, row, patterns)) return false;
        RowWriter.Write(session.Output, row, columns, prefix);
        return true;
    }

    private static bool Match(Options options, MetadataRow row, List<Regex> patterns)
    {
        if (options.ExactKeys.Count > 0 && !options.ExactKeys.Contains(row.Key, StringComparer.Ordinal)) return false;
        if (patterns.Count > 0)
        {
            bool matched = false;
            foreach (Regex pattern in patterns)
            {
                if (!pattern.IsMatch(row.Key)) continue;
                matched = true;
                break;
            }
            if (!matched) return false;
        }
        return !options.HideUnknown || !row.Name.StartsWith("0x", StringComparison.Ordinal);
    }
}
