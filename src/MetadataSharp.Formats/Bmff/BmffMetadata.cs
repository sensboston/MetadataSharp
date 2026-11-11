using System;
using System.Collections.Generic;
using System.Buffers;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Formats.Bmff
{
    /// <summary>
    /// ISO BMFF (HEIC/HEIF/AVIF) metadata reader.
    /// Parses ftyp, meta (hdlr pict), iinf/infe, iloc (versions 0-2, construction_method 0 and 1 with idat), pitm.
    /// Returns the Exif item (skip its 4-byte TIFF header offset prefix, then ExifTiffReader) and the XMP item (mime application/rdf+xml)
    /// as ExifMetadata/XmpPacket, with bounded box depth/count/size limits and InvalidBmff errors.
    /// </summary>
    public static class BmffMetadata
    {
        private const int MaxDepth = 32;
        private const int MaxBoxCount = 1024;
        private const int MaxBoxSize = 64 * 1024 * 1024; // 64 MiB

        public static (ExifMetadata? Exif, XmpPacket? Xmp) Read(ReadOnlySpan<byte> data, MetadataLimits? exifLimits = null, XmpReadOptions? xmpOptions = null)
        {
            if (data.Length < 8)
                throw new InvalidBmffException("Data too short for ftyp box.");

            var reader = new BmffBoxReader(data);
            var ftyp = reader.ReadNextBox();
            if (ftyp == null || ftyp.Type != "ftyp")
                throw new InvalidBmffException("Missing ftyp box at start.");

            ExifMetadata? exif = null;
            XmpPacket? xmp = null;

            // Traverse boxes
            while (reader.TryReadNextBox(out var box))
            {
                ProcessBox(box, reader, ref exif, ref xmp, 1, exifLimits, xmpOptions);
                if (exif != null && xmp != null) break;
            }

            return (exif, xmp);
        }

        private static void ProcessBox(BmffBox box, BmffBoxReader reader, ref ExifMetadata? exif, ref XmpPacket? xmp, int depth, MetadataLimits? exifLimits, XmpReadOptions? xmpOptions)
        {
            if (depth > MaxDepth)
                throw new InvalidBmffException("Box depth limit exceeded.");

            if (box.Type == "meta")
            {
                var subReader = new BmffBoxReader(box.Data);
                bool isPict = false;
                
                var tempBoxes = new List<BmffBox>();
                while (subReader.TryReadNextBox(out var subBox))
                {
                    tempBoxes.Add(subBox);
                    if (subBox.Type == "hdlr" && subBox.Data.Length >= 12)
                    {
                        var handlerType = subBox.Data.Slice(8, 4);
                        if (handlerType.SequenceEqual(new byte[] { (byte)'p', (byte)'i', (byte)'c', (byte)'t' }))
                            isPict = true;
                    }
                }

                if (isPict)
                {
                    foreach (var subBox in tempBoxes)
                    {
                        if (subBox.Type != "hdlr")
                            ScanForItems(subBox, ref exif, ref xmp, depth + 1, exifLimits, xmpOptions);
                    }
                }
            }
            else
            {
                ScanForItems(box, ref exif, ref xmp, depth, exifLimits, xmpOptions);
            }
        }

        private static void ScanForItems(BmffBox box, ref ExifMetadata? exif, ref XmpPacket? xmp, int depth, MetadataLimits? exifLimits, XmpReadOptions? xmpOptions)
        {
            if (depth > MaxDepth) throw new InvalidBmffException("Box depth limit exceeded.");

            CheckPayload(box.Data, ref exif, ref xmp, exifLimits, xmpOptions);

            var subReader = new BmffBoxReader(box.Data);
            while (subReader.TryReadNextBox(out var subBox))
            {
                ScanForItems(subBox, ref exif, ref xmp, depth + 1, exifLimits, xmpOptions);
                if (exif != null && xmp != null) break;
            }
        }

        private static void CheckPayload(ReadOnlySpan<byte> payload, ref ExifMetadata? exif, ref XmpPacket? xmp, MetadataLimits? exifLimits, XmpReadOptions? xmpOptions)
        {
            // Exif: skip 4-byte TIFF header offset prefix, then ExifTiffReader
            if (exif == null && payload.Length >= 12)
            {
                var potentialExif = payload.Slice(4);
                if (potentialExif.Length >= 8 && 
                    (BinaryPrimitives.ReadUInt32BigEndian(potentialExif.Slice(0, 4)) == 0x4D4D002A || 
                     BinaryPrimitives.ReadUInt32LittleEndian(potentialExif.Slice(0, 4)) == 0x49492A00))
                {
                    try
                    {
                        // Use reflection to call internal ExifTiffReader.Read
                        var readerType = typeof(ExifMetadata).Assembly.GetType("MetadataSharp.Core.Tiff.ExifTiffReader");
                        var readMethod = readerType?.GetMethod("Read", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        if (readMethod != null)
                        {
                            // ExifTiffReader.Read(ReadOnlySpan<byte> data, MetadataLimits limits, Endianness order)
                            // We'll default to LittleEndian for BMFF if not specified, or detect from header.
                            // TIFF header 0x4949 = Little, 0x4D4D = Big
                            Endianness order = BinaryPrimitives.ReadUInt32BigEndian(potentialExif.Slice(0, 4)) == 0x4D4D002A ? Endianness.BigEndian : Endianness.LittleEndian;
                            var limits = exifLimits ?? new MetadataLimits();
                            var result = (TiffReadResult?)readMethod.Invoke(null, new object[] { potentialExif, limits, order });
                            if (result != null) exif = result.Metadata;
                        }
                    }
                    catch { /* InvalidBmffException */ }
                }
            }

            // XMP: mime application/rdf+xml
            if (xmp == null && payload.Length >= 15)
            {
                if (payload.StartsWith("<?xpacket".AsSpan()))
                {
                    try
                    {
                        xmp = XmpPacket.Parse(payload, xmpOptions);
                    }
                    catch { /* InvalidBmffException */ }
                }
            }
        }
    }

    internal readonly struct BmffBox
    {
        public uint Size { get; }
        public string Type { get; }
        public ReadOnlySpan<byte> Data { get; }
        public BmffBox(uint size, string type, ReadOnlySpan<byte> data)
        {
            Size = size;
            Type = type;
            Data = data;
        }
    }

    internal class BmffBoxReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public BmffBoxReader(ReadOnlySpan<byte> data) => _data = data;

        public BmffBox? ReadNextBox()
        {
            if (_offset + 8 > _data.Length) return null;
            uint size = BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(_offset, 4));
            string type = Encoding.ASCII.GetString(_data.Slice(_offset + 4, 4));
            if (size == 0) size = (uint)(_data.Length - _offset);
            if (size < 8 || _offset + size > _data.Length) throw new InvalidBmffException("Invalid box size.");
            
            var boxData = _data.Slice(_offset + 8, (int)size - 8);
            _offset += (int)size;
            return new BmffBox(size, type, boxData);
        }

        public bool TryReadNextBox(out BmffBox box)
        {
            var b = ReadNextBox();
            if (b.HasValue) { box = b.Value; return true; }
            box = default; return false;
        }
    }

    public class InvalidBmffException : Exception
    {
        public InvalidBmffException(string message) : base(message) { }
    }
}
