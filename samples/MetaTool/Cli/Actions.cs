using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Operations;
using MetadataSharp.Core.Sync;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Operations;
using MetadataSharp.Formats.Png;

namespace MetaTool.Cli;

/// <summary>Where the payloads of extract and insert live, and what happens to a file that is already there.</summary>
internal static class Payloads
{
    /// <summary>The name of a payload beside its image, or in the directory <c>-l</c> named.</summary>
    /// <param name="session">The run.</param>
    /// <param name="path">The image.</param>
    /// <param name="marker">What to add to the name of the image before the extension.</param>
    /// <param name="extension">The extension, with its dot.</param>
    /// <returns>The path of the payload.</returns>
    internal static string Beside(Session session, string path, string marker, string extension)
    {
        string stem = path == "-" ? "stdin" : Path.GetFileNameWithoutExtension(path);
        string directory = session.Options.Directory
            ?? (path == "-" ? System.IO.Directory.GetCurrentDirectory() : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        return Path.Combine(directory, stem + marker + extension);
    }

    /// <summary>The path a payload is written to, numbered when the name is taken and <c>-f</c> was not given.</summary>
    /// <param name="session">The run.</param>
    /// <param name="path">The name the payload would have.</param>
    /// <returns>The name it is written to.</returns>
    internal static string Free(Session session, string path)
    {
        if (session.Options.Force || !File.Exists(path)) return path;
        string directory = Path.GetDirectoryName(path) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int number = 1; ; number++)
        {
            string candidate = Path.Combine(directory, stem + "_" + number.ToString(CultureInfo.InvariantCulture) + extension);
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>The extension of an embedded picture, from its first bytes.</summary>
    /// <param name="bytes">The bytes of the picture.</param>
    /// <returns><c>.jpg</c>, <c>.png</c>, <c>.tif</c> or <c>.bin</c>.</returns>
    internal static string Extension(ReadOnlySpan<byte> bytes) => SampleSupport.Detect(bytes) switch
        {
            MetadataFormat.Jpeg => ".jpg",
            MetadataFormat.Png => ".png",
            MetadataFormat.Tiff => ".tif",
            MetadataFormat.WebP => ".webp",
            _ => ".bin",
        };

    /// <summary>The families of a payload insert or of a deletion, from the targets of the command line.</summary>
    /// <param name="targets">The targets.</param>
    /// <returns>The families; <see cref="MetadataFamilies.None"/> when the targets name none.</returns>
    internal static MetadataFamilies Families(Targets targets)
    {
        MetadataFamilies families = MetadataFamilies.None;
        if (targets.HasFlag(Targets.Exif)) families |= MetadataFamilies.Exif;
        if (targets.HasFlag(Targets.Iptc) || targets.HasFlag(Targets.IptcRaw)) families |= MetadataFamilies.Iptc;
        if (targets.HasFlag(Targets.Xmp)) families |= MetadataFamilies.Xmp;
        if (targets.HasFlag(Targets.Comment)) families |= MetadataFamilies.Comment;
        if (targets.HasFlag(Targets.Thumbnail)) families |= MetadataFamilies.Thumbnail;
        if (targets.HasFlag(Targets.Icc)) families |= MetadataFamilies.IccProfile;
        return families;
    }
}

/// <summary>The extract action: thumbnails, previews, sidecars, profiles, streams and EXV payloads.</summary>
internal static class ExtractAction
{
    /// <summary>Extracts the targets of every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(Session session) => session.ForEachFile(path => One(session, path));

    private static int One(Session session, string path)
    {
        Options options = session.Options;
        Targets targets = options.Targets;
        byte[] bytes = session.ReadFile(path);
        ImageMetadata image = MetadataSharp.Formats.ImageMetadata.Read(bytes);
        bool toStandardOutput = targets.HasFlag(Targets.StdInOut);
        foreach (MetadataReadError error in image.Errors) session.Warn($"{path}: {error}");

        if (targets.HasFlag(Targets.Thumbnail))
        {
            int code = Thumbnail(session, path, bytes, image, toStandardOutput);
            if (code != ExitCodes.Ok) return code;
        }
        if (targets.HasFlag(Targets.Preview))
        {
            int code = Previews(session, path, bytes, image, toStandardOutput);
            if (code != ExitCodes.Ok) return code;
        }
        if (targets.HasFlag(Targets.XmpSidecar) || targets.HasFlag(Targets.XmpRaw))
        {
            int code = Sidecar(session, path, bytes, targets.HasFlag(Targets.XmpRaw), toStandardOutput);
            if (code != ExitCodes.Ok) return code;
        }
        if (targets.HasFlag(Targets.Icc))
        {
            int code = Profile(session, path, bytes, toStandardOutput);
            if (code != ExitCodes.Ok) return code;
        }
        if (targets.HasFlag(Targets.IptcRaw))
        {
            int code = IptcStream(session, path, image, toStandardOutput);
            if (code != ExitCodes.Ok) return code;
        }
        bool anything = targets.HasFlag(Targets.Thumbnail) || targets.HasFlag(Targets.Preview) ||
            targets.HasFlag(Targets.XmpSidecar) || targets.HasFlag(Targets.XmpRaw) ||
            targets.HasFlag(Targets.Icc) || targets.HasFlag(Targets.IptcRaw);
        if (anything) return ExitCodes.Ok;
        return Exv(session, path, bytes, targets, toStandardOutput);
    }

    private static int Thumbnail(Session session, string path, byte[] bytes, ImageMetadata image, bool toStandardOutput)
    {
        byte[]? payload = MetadataOperations.ExtractThumbnail(bytes);
        if (payload is null || payload.Length == 0)
        {
            session.Warn($"{path}: image does not contain an Exif thumbnail");
            return ExitCodes.Absent;
        }
        if (toStandardOutput)
        {
            session.WriteStandardOutput(payload);
            return ExitCodes.Ok;
        }
        string mime = string.Empty;
        foreach (PreviewImage preview in image.Previews)
        {
            if (!preview.Source.StartsWith("Exif.Thumbnail", StringComparison.Ordinal)) continue;
            mime = preview.MimeType;
            break;
        }
        string extension = mime == "image/png" ? ".png" : Payloads.Extension(payload);
        string destination = Payloads.Free(session, Payloads.Beside(session, path, "-thumb", extension));
        session.WriteFile(destination, payload);
        session.Note($"writing thumbnail ({payload.Length} bytes) to {destination}");
        return ExitCodes.Ok;
    }

    private static int Previews(Session session, string path, byte[] bytes, ImageMetadata image, bool toStandardOutput)
    {
        IReadOnlyList<PreviewImage> previews = image.Previews;
        if (previews.Count == 0)
        {
            session.Warn($"{path}: the file has no preview");
            return ExitCodes.Absent;
        }
        IReadOnlyList<int> wanted = session.Options.PreviewNumbers;
        int code = ExitCodes.Ok;
        if (wanted.Count == 0)
        {
            for (int i = 0; i < previews.Count; i++)
                code = Once(session, path, bytes, previews[i], i + 1, toStandardOutput);
            return code;
        }
        foreach (int number in wanted)
        {
            if (number > previews.Count)
            {
                session.Warn($"{path}: the file has no preview {number}");
                if (code == ExitCodes.Ok) code = ExitCodes.Absent;
                continue;
            }
            int result = Once(session, path, bytes, previews[number - 1], number, toStandardOutput);
            if (code == ExitCodes.Ok) code = result;
        }
        return code;
    }

    private static int Once(Session session, string path, byte[] bytes, PreviewImage preview, int number, bool toStandardOutput)
    {
        byte[] payload = MetadataSharp.Formats.Previews.Extract(bytes, preview);
        if (toStandardOutput)
        {
            session.WriteStandardOutput(payload);
            return ExitCodes.Ok;
        }
        string destination = Payloads.Free(session, Payloads.Beside(session, path,
            "-preview" + number.ToString(CultureInfo.InvariantCulture), Payloads.Extension(payload)));
        session.WriteFile(destination, payload);
        session.Note($"writing preview {number} ({payload.Length} bytes) to {destination}");
        return ExitCodes.Ok;
    }

    private static int Sidecar(Session session, string path, byte[] bytes, bool raw, bool toStandardOutput)
    {
        byte[]? payload = MetadataOperations.ExtractXmpSidecar(bytes, convert: !raw);
        if (payload is null || payload.Length == 0)
        {
            session.Warn($"{path}: the file has no XMP packet");
            return ExitCodes.Absent;
        }
        if (toStandardOutput)
        {
            session.WriteStandardOutput(payload);
            return ExitCodes.Ok;
        }
        string destination = Payloads.Free(session, Payloads.Beside(session, path, string.Empty, ".xmp"));
        session.WriteFile(destination, payload);
        session.Note($"writing XMP sidecar ({payload.Length} bytes) to {destination}");
        return ExitCodes.Ok;
    }

    private static int Profile(Session session, string path, byte[] bytes, bool toStandardOutput)
    {
        byte[]? payload = IccProfiles.Read(bytes);
        if (payload is null || payload.Length == 0)
        {
            session.Warn($"{path}: the file has no ICC profile");
            return ExitCodes.Absent;
        }
        if (toStandardOutput)
        {
            session.WriteStandardOutput(payload);
            return ExitCodes.Ok;
        }
        string destination = Payloads.Free(session, Payloads.Beside(session, path, string.Empty, ".icc"));
        session.WriteFile(destination, payload);
        session.Note($"writing ICC profile ({payload.Length} bytes) to {destination}");
        return ExitCodes.Ok;
    }

    private static int IptcStream(Session session, string path, ImageMetadata image, bool toStandardOutput)
    {
        if (image.Iptc is not { } iptc)
        {
            session.Warn($"{path}: the file has no IPTC stream");
            return ExitCodes.Absent;
        }
        byte[] payload = new byte[iptc.ByteLength];
        iptc.CopyRawTo(payload);
        if (toStandardOutput)
        {
            session.WriteStandardOutput(payload);
            return ExitCodes.Ok;
        }
        string destination = Payloads.Free(session, Payloads.Beside(session, path, string.Empty, ".iptc"));
        session.WriteFile(destination, payload);
        session.Note($"writing IPTC stream ({payload.Length} bytes) to {destination}");
        return ExitCodes.Ok;
    }

    private static int Exv(Session session, string path, byte[] bytes, Targets targets, bool toStandardOutput)
    {
        MetadataFamilies families = Payloads.Families(targets);
        if (families == MetadataFamilies.None) families = MetadataFamilies.All;
        byte[] payload = MetadataOperations.ExtractExv(bytes, families);
        if (payload.Length == 0)
        {
            session.Warn($"{path}: nothing to extract for the targets given");
            return ExitCodes.Absent;
        }
        if (toStandardOutput)
        {
            session.WriteStandardOutput(payload);
            return ExitCodes.Ok;
        }
        string destination = Payloads.Free(session, Payloads.Beside(session, path, string.Empty, ".exv"));
        session.WriteFile(destination, payload);
        session.Note($"writing EXV payload ({payload.Length} bytes) to {destination}");
        return ExitCodes.Ok;
    }
}

/// <summary>The insert action: the same targets as extract, read from a payload beside the image or from standard input.</summary>
internal static class InsertAction
{
    /// <summary>Inserts into every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(Session session)
    {
        if (session.Options.Targets.HasFlag(Targets.IptcRaw))
        {
            session.Warn("insert -i I is not offered: the library replaces IPTC datasets, not a whole IIM stream; insert an EXV payload (-i a) instead");
            return ExitCodes.Refused;
        }
        return session.ForEachFile(path => One(session, path));
    }

    private static int One(Session session, string path)
    {
        Options options = session.Options;
        Targets targets = options.Targets;
        byte[] bytes = session.ReadFile(path);
        bool fromStandardInput = targets.HasFlag(Targets.StdInOut);
        byte[] output;

        if (targets.HasFlag(Targets.Thumbnail))
        {
            byte[] payload = Payload(session, path, "-thumb", ".jpg", fromStandardInput);
            var edit = new ExifEdit();
            edit.SetThumbnail(payload);
            output = SampleSupport.SaveExif(bytes, edit);
        }
        else if (targets.HasFlag(Targets.XmpSidecar) || targets.HasFlag(Targets.XmpRaw))
        {
            byte[] payload = Payload(session, path, string.Empty, ".xmp", fromStandardInput);
            output = MetadataOperations.InsertXmpSidecar(bytes, payload, convert: !targets.HasFlag(Targets.XmpRaw));
        }
        else if (targets.HasFlag(Targets.Icc))
        {
            byte[] payload = Payload(session, path, string.Empty, ".icc", fromStandardInput);
            output = SampleSupport.SaveIcc(bytes, payload);
        }
        else
        {
            byte[] payload = Payload(session, path, string.Empty, ".exv", fromStandardInput);
            MetadataFamilies families = Payloads.Families(targets);
            if (families == MetadataFamilies.None) families = MetadataFamilies.All;
            output = MetadataOperations.InsertExv(bytes, payload, families, out IReadOnlyList<string> notes);
            foreach (string note in notes) session.Note(note);
        }
        session.WriteFile(path, output);
        session.Note($"{path}: inserted the {Describe(targets)} of the payload");
        return ExitCodes.Ok;
    }

    private static string Describe(Targets targets) => targets switch
    {
        _ when targets.HasFlag(Targets.Thumbnail) => "thumbnail",
        _ when targets.HasFlag(Targets.XmpSidecar) => "XMP sidecar",
        _ when targets.HasFlag(Targets.XmpRaw) => "XMP packet",
        _ when targets.HasFlag(Targets.Icc) => "ICC profile",
        _ => "metadata",
    };

    // The payload of an insert: standard input, the file the -S suffix names, or the payload beside the image.
    private static byte[] Payload(Session session, string path, string marker, string extension, bool fromStandardInput)
    {
        if (fromStandardInput) return session.ReadFile("-");
        Options options = session.Options;
        string payloadPath = options.Suffix is { Length: > 0 } suffix
            ? Path.ChangeExtension(path, suffix)
            : Payloads.Beside(session, path, marker, extension);
        if (!File.Exists(payloadPath))
            throw new FileNotFoundException($"the payload {payloadPath} does not exist");
        return File.ReadAllBytes(payloadPath);
    }
}

/// <summary>The delete action: every deletable family, in place or to standard output.</summary>
internal static class DeleteAction
{
    /// <summary>Deletes from every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(Session session) => session.ForEachFile(path => One(session, path));

    private static int One(Session session, string path)
    {
        Options options = session.Options;
        MetadataFamilies families = Payloads.Families(options.Targets);
        if (families == MetadataFamilies.None)
        {
            session.Warn("delete needs a target that names metadata");
            return ExitCodes.Usage;
        }
        byte[] bytes = session.ReadFile(path);
        byte[] output = MetadataOperations.Delete(bytes, families);
        if (options.Targets.HasFlag(Targets.StdInOut)) session.WriteStandardOutput(output);
        else session.WriteFile(path, output);
        session.Note($"{path}: deleted {families}");
        return ExitCodes.Ok;
    }
}

/// <summary>The modify action: <c>-M</c> and <c>-m</c> commands and the comment of <c>-c</c>.</summary>
internal static class ModifyAction
{
    /// <summary>Modifies every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(Session session) => session.ForEachFile(path => One(session, path));

    private static int One(Session session, string path)
    {
        Options options = session.Options;
        byte[] bytes = session.ReadFile(path);
        List<string> lines = CommandLines(session);
        int commands = 0;
        if (lines.Count > 0)
        {
            ModifyScript script = ModifyScript.Parse(lines);
            commands = script.Commands.Count;
            if (!script.IsEmpty) bytes = SampleSupport.SaveScript(bytes, script);
        }
        if (options.CommentSet) bytes = SaveComment(bytes, options.Comment);
        session.WriteFile(path, bytes);
        session.Note($"{path}: {commands} command(s) applied" + (options.CommentSet ? ", comment set" : string.Empty));
        return ExitCodes.Ok;
    }

    private static List<string> CommandLines(Session session)
    {
        Options options = session.Options;
        var lines = new List<string>();
        foreach (string command in options.CommandLines) Add(lines, command);
        foreach (string file in options.CommandFiles) Add(lines, session.ReadText(file));
        return lines;
    }

    private static void Add(List<string> lines, string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length != 0) lines.Add(trimmed);
        }
    }

    private static byte[] SaveComment(byte[] bytes, string? comment)
    {
        MetadataFormat format = SampleSupport.Detect(bytes);
        using var input = new MemoryStream(bytes, writable: false);
        using var output = new MemoryStream();
        switch (format)
        {
            case MetadataFormat.Jpeg:
                using (var document = JpegMetadataDocument.Open(input)) document.SaveComment(output, comment);
                break;
            case MetadataFormat.Png:
                using (var document = PngMetadataDocument.Open(input)) document.SaveComment(output, comment);
                break;
            default:
                throw new NotSupportedException($"a comment is not supported for {format}");
        }
        return output.ToArray();
    }
}

/// <summary>The adjust action: <c>-a</c>, <c>-Y</c>, <c>-O</c> and <c>-D</c> moved into the timestamps.</summary>
internal static class AdjustAction
{
    /// <summary>Adjusts every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(Session session) => session.ForEachFile(path => One(session, path));

    private static int One(Session session, string path)
    {
        Options options = session.Options;
        byte[] bytes = session.ReadFile(path);
        ImageMetadata image = MetadataSharp.Formats.ImageMetadata.Read(bytes);
        if (image.Exif is null && image.Iptc is null && image.Xmp is null)
        {
            session.Warn($"{path}: no timestamp to adjust");
            return ExitCodes.Absent;
        }
        var shift = new TimestampShift(options.YearAdjustment, options.MonthAdjustment, options.DayAdjustment, options.TimeAdjustment);
        SyncEdits edits = MetadataTimestamps.Adjust(image.Exif, image.Iptc, image.Xmp, shift);
        session.WriteFile(path, SampleSupport.SaveAll(bytes, edits));
        session.Note($"{path}: timestamps adjusted by {Describe(options)}");
        return ExitCodes.Ok;
    }

    private static string Describe(Options options)
    {
        var parts = new List<string>();
        if (options.YearAdjustment != 0) parts.Add(options.YearAdjustment.ToString(CultureInfo.InvariantCulture) + " year(s)");
        if (options.MonthAdjustment != 0) parts.Add(options.MonthAdjustment.ToString(CultureInfo.InvariantCulture) + " month(s)");
        if (options.DayAdjustment != 0) parts.Add(options.DayAdjustment.ToString(CultureInfo.InvariantCulture) + " day(s)");
        if (options.TimeAdjustment != TimeSpan.Zero) parts.Add(options.TimeAdjustment.ToString());
        return string.Join(", ", parts);
    }
}

/// <summary>The rename action: the name <c>-r</c> gives, the collision policy, and the file time of <c>-t</c> and <c>-T</c>.</summary>
internal static class RenameAction
{
    /// <summary>Renames every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(Session session) => session.ForEachFile(path => One(session, path));

    private static int One(Session session, string path)
    {
        Options options = session.Options;
        byte[] bytes = session.ReadFile(path);
        ImageMetadata image = MetadataSharp.Formats.ImageMetadata.Read(bytes);
        ExifMetadata? exif = image.Exif;
        DateTime? taken = exif?.DateTimeOriginal ?? exif?.DateTaken;
        if (taken is null)
        {
            session.Warn($"{path}: neither Exif.Photo.DateTimeOriginal nor Exif.Image.DateTime is set");
            return ExitCodes.Failed;
        }
        if (options.OnlySetFileTime)
        {
            SetFileTime(path, taken.Value);
            session.Note($"{path}: set the file time to {taken.Value.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            return ExitCodes.Ok;
        }

        string name;
        try
        {
            name = MetadataFileNames.Format(options.RenameFormat, taken.Value, path);
        }
        catch (ArgumentException exception)
        {
            session.Warn($"{path}: {exception.Message}");
            return ExitCodes.Failed;
        }
        string expanded = Expand(session, path, name, exif);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string destination = Path.Combine(directory, expanded);
        if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(path), StringComparison.Ordinal))
        {
            session.Note("this file already has the correct name");
            return ExitCodes.Ok;
        }
        destination = Payloads.Free(session, destination);
        DateTime? written = null, accessed = null;
        if (options.PreserveTimestamps)
        {
            var info = new FileInfo(path);
            written = info.LastWriteTimeUtc;
            accessed = info.LastAccessTimeUtc;
        }
        File.Move(path, destination, options.Force);
        if (written is not null && accessed is not null)
        {
            File.SetLastWriteTimeUtc(destination, written.Value);
            File.SetLastAccessTimeUtc(destination, accessed.Value);
        }
        if (options.SetFileTime) SetFileTime(destination, taken.Value);
        session.Note($"{path} -> {destination}");
        return ExitCodes.Ok;
    }

    private static void SetFileTime(string path, DateTime taken) => File.SetLastWriteTime(path, taken);

    // The :Exif.Key: tokens of a name format, replaced after the strftime conversions as the reference does.
    private static readonly System.Text.RegularExpressions.Regex KeyToken =
        new(@":(Exif\.[^:]+):", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string Expand(Session session, string path, string name, ExifMetadata? exif)
    {
        if (exif is null || !name.Contains(":Exif.", StringComparison.Ordinal)) return name;
        Dictionary<string, string> values = Reports.Values(exif);
        return KeyToken.Replace(name, match =>
        {
            string key = match.Groups[1].Value;
            if (!values.TryGetValue(key, out string? text) || text.Length == 0)
            {
                session.Warn($"{path}: {key} is empty or not included");
                return string.Empty;
            }
            return Sanitize(text);
        });
    }

    private static string Sanitize(string text)
    {
        var name = new StringBuilder(text.Length);
        foreach (char character in text) name.Append(character is '/' or '\\' or ':' or < ' ' ? '_' : character);
        return name.ToString();
    }
}

/// <summary>The two repairs: the ISO speed from the MakerNote and the user comment in the charset of <c>-n</c>.</summary>
internal static class FixAction
{
    /// <summary>Runs fixiso on every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int RunIso(Session session) => session.ForEachFile(path => Iso(session, path));

    /// <summary>Runs fixcom on every file of the run.</summary>
    /// <param name="session">The run.</param>
    /// <returns>The exit code.</returns>
    internal static int RunComment(Session session) => session.ForEachFile(path => Comment(session, path));

    private static int Iso(Session session, string path)
    {
        byte[] bytes = session.ReadFile(path);
        ImageMetadata image = MetadataSharp.Formats.ImageMetadata.Read(bytes);
        ExifEdit? edit = image.Exif is null ? null : MetadataOperations.FixIso(image.Exif);
        if (edit is null)
        {
            session.Warn($"{path}: no ISO speed to set from the MakerNote");
            return ExitCodes.Absent;
        }
        session.WriteFile(path, SampleSupport.SaveExif(bytes, edit));
        session.Note($"{path}: ISO speed set from the MakerNote");
        return ExitCodes.Ok;
    }

    private static int Comment(Session session, string path)
    {
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(session.Options.CharsetName);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            session.Warn($"unknown charset {session.Options.CharsetName}");
            return ExitCodes.Usage;
        }
        byte[] bytes = session.ReadFile(path);
        ImageMetadata image = MetadataSharp.Formats.ImageMetadata.Read(bytes);
        ExifEdit? edit = image.Exif is null ? null : MetadataOperations.FixUserComment(image.Exif, encoding);
        if (edit is null)
        {
            session.Warn($"{path}: no user comment to rewrite");
            return ExitCodes.Absent;
        }
        session.WriteFile(path, SampleSupport.SaveExif(bytes, edit));
        session.Note($"{path}: user comment rewritten in {encoding.WebName}");
        return ExitCodes.Ok;
    }
}
