using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Operations;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.WebP;

internal static class SampleSupport
{
    public static byte[] ReadBytes(string path) => File.ReadAllBytes(path);

    public static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes.ToArray());
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static MetadataFormat Detect(ReadOnlySpan<byte> bytes) => ImageMetadata.Detect(bytes[..Math.Min(bytes.Length, ImageMetadata.HeaderLength)]);

    public static byte[] SaveExif(ReadOnlySpan<byte> file, ExifEdit edit)
    {
        MetadataFormat format = Detect(file);
        using var input = new MemoryStream(file.ToArray(), writable: false);
        using var output = new MemoryStream();
        switch (format)
        {
            case MetadataFormat.Jpeg: using (var d = JpegMetadataDocument.Open(input)) d.Save(output, edit); break;
            case MetadataFormat.Png: using (var d = PngMetadataDocument.Open(input)) d.SaveExif(output, edit); break;
            case MetadataFormat.WebP: using (var d = WebPMetadataDocument.Open(input)) d.SaveExif(output, edit); break;
            case MetadataFormat.Tiff: using (var d = TiffBasedMetadataDocument.Open(input)) d.SaveExif(output, edit); break;
            case MetadataFormat.Bmff or MetadataFormat.Cr3: using (var d = BmffMetadataDocument.Open(input)) d.SaveExif(output, edit); break;
            default: throw new NotSupportedException($"EXIF edits are not supported for {format}.");
        }
        return output.ToArray();
    }

    public static byte[] SaveIptc(ReadOnlySpan<byte> file, IptcEdit edit)
    {
        MetadataFormat format = Detect(file);
        using var input = new MemoryStream(file.ToArray(), writable: false);
        using var output = new MemoryStream();
        switch (format)
        {
            case MetadataFormat.Jpeg: using (var d = JpegMetadataDocument.Open(input)) d.SaveIptc(output, edit); break;
            case MetadataFormat.Png: using (var d = PngMetadataDocument.Open(input)) d.SaveIptc(output, edit); break;
            case MetadataFormat.Tiff: using (var d = TiffBasedMetadataDocument.Open(input)) d.SaveIptc(output, edit); break;
            default: throw new NotSupportedException($"IPTC edits are not supported for {format}.");
        }
        return output.ToArray();
    }

    public static byte[] SaveXmp(ReadOnlySpan<byte> file, XmpEdit edit)
    {
        MetadataFormat format = Detect(file);
        using var input = new MemoryStream(file.ToArray(), writable: false);
        using var output = new MemoryStream();
        switch (format)
        {
            case MetadataFormat.Jpeg: using (var d = JpegMetadataDocument.Open(input)) d.SaveXmp(output, edit); break;
            case MetadataFormat.Png: using (var d = PngMetadataDocument.Open(input)) d.SaveXmp(output, edit); break;
            case MetadataFormat.WebP: using (var d = WebPMetadataDocument.Open(input)) d.SaveXmp(output, edit); break;
            case MetadataFormat.Tiff: using (var d = TiffBasedMetadataDocument.Open(input)) d.SaveXmp(output, edit); break;
            case MetadataFormat.Bmff or MetadataFormat.Cr3: using (var d = BmffMetadataDocument.Open(input)) d.SaveXmp(output, edit); break;
            default: throw new NotSupportedException($"XMP edits are not supported for {format}.");
        }
        return output.ToArray();
    }

    public static byte[] SaveAll(ReadOnlySpan<byte> file, SyncEdits edits)
    {
        MetadataFormat format = Detect(file);
        using var input = new MemoryStream(file.ToArray(), writable: false);
        using var output = new MemoryStream();
        switch (format)
        {
            case MetadataFormat.Jpeg: using (var d = JpegMetadataDocument.Open(input)) d.SaveAll(output, edits); break;
            case MetadataFormat.Png: using (var d = PngMetadataDocument.Open(input)) d.SaveAll(output, edits); break;
            case MetadataFormat.WebP: using (var d = WebPMetadataDocument.Open(input)) d.SaveAll(output, edits); break;
            case MetadataFormat.Tiff: using (var d = TiffBasedMetadataDocument.Open(input)) d.SaveAll(output, edits); break;
            case MetadataFormat.Bmff or MetadataFormat.Cr3: using (var d = BmffMetadataDocument.Open(input)) d.SaveAll(output, edits); break;
            default: throw new NotSupportedException($"Combined metadata edits are not supported for {format}.");
        }
        return output.ToArray();
    }

    public static byte[] SaveIcc(ReadOnlySpan<byte> file, byte[]? icc)
    {
        MetadataFormat format = Detect(file);
        using var input = new MemoryStream(file.ToArray(), writable: false);
        using var output = new MemoryStream();
        switch (format)
        {
            case MetadataFormat.Jpeg: using (var d = JpegMetadataDocument.Open(input)) d.SaveIccProfile(output, icc); break;
            case MetadataFormat.Png: using (var d = PngMetadataDocument.Open(input)) d.SaveIccProfile(output, icc); break;
            case MetadataFormat.WebP: using (var d = WebPMetadataDocument.Open(input)) d.SaveIccProfile(output, icc); break;
            case MetadataFormat.Tiff: using (var d = TiffBasedMetadataDocument.Open(input)) d.SaveIccProfile(output, icc); break;
            case MetadataFormat.Bmff or MetadataFormat.Cr3: using (var d = BmffMetadataDocument.Open(input)) d.SaveIccProfile(output, icc); break;
            default: throw new NotSupportedException($"ICC edits are not supported for {format}.");
        }
        return output.ToArray();
    }

    public static byte[] SaveScript(ReadOnlySpan<byte> file, ModifyScript script)
    {
        byte[] current = file.ToArray();
        if (!script.Exif.IsEmpty) current = SaveExif(current, script.Exif);
        if (!script.Iptc.IsEmpty) current = SaveIptc(current, script.Iptc);
        if (!script.Xmp.IsEmpty) current = SaveXmp(current, script.Xmp);
        return current;
    }

    public static string RawText(ExifValue value)
    {
        if (value.TryGetString(out string? text)) return text ?? string.Empty;
        if (value.TryGetByteArray(out byte[]? bytes)) return Convert.ToHexString(bytes);
        if (value.TryGetUInt16Array(out ushort[]? u16)) return string.Join(" ", u16);
        if (value.TryGetUInt32Array(out uint[]? u32)) return string.Join(" ", u32);
        if (value.TryGetInt16Array(out short[]? i16)) return string.Join(" ", i16);
        if (value.TryGetInt32Array(out int[]? i32)) return string.Join(" ", i32);
        if (value.TryGetRationalArray(out UnsignedRational[]? ur)) return string.Join(" ", ur.Select(x => $"{x.Numerator}/{x.Denominator}"));
        if (value.TryGetSignedRationalArray(out SignedRational[]? sr)) return string.Join(" ", sr.Select(x => $"{x.Numerator}/{x.Denominator}"));
        return $"{value.FieldType} x {value.Count}";
    }

    public static string ExifValueText(ExifMetadata exif, ExifEntry entry) =>
        ExifTags.Interpret(exif, entry.Key) ?? RawText(entry.Value);

    public static string IptcText(IptcDataset dataset) =>
        IptcDatasetNames.Interpret(dataset.Key, dataset.Value) ?? $"{dataset.Value.ByteLength} raw bytes";
}
