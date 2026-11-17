using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using SharpFuzz;

namespace MetadataSharp.Fuzz;

/// <summary>
/// The SharpFuzz harness. <c>MetadataSharp.Fuzz TARGET</c> runs under afl-fuzz (out of process, so
/// that a stack overflow, an out-of-memory failure or a timeout is observed rather than lost);
/// <c>--replay</c> runs inputs once each, <c>--extract</c> derives packet and MakerNote seeds from
/// whole files, <c>--list</c> prints the targets. See README.md.
/// </summary>
internal static class Program
{
    private const int MaxSeedBytes = 1024 * 1024;

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--list")
        {
            DetachFromAfl();
            Console.WriteLine(string.Join('\n', FuzzTargets.Names));
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--replay")
        {
            DetachFromAfl();
            return Replay(args[1], args[2..]);
        }
        if (args.Length >= 4 && args[0] == "--extract")
        {
            DetachFromAfl();
            return Extract(args[1], args[2], args[3..]);
        }
        if (args.Length == 1 && !args[0].StartsWith('-'))
        {
            // Under afl-fuzz nothing instrumented may run before Fuzzer.OutOfProcess.Run: the coverage
            // map exists only in the child process it starts. FuzzTargets touches the libraries as it
            // initializes, so even the target name is checked inside, where Run throws for an unknown one.
            string target = args[0];
            Fuzzer.OutOfProcess.Run(input =>
            {
                using var copy = new MemoryStream();
                input.CopyTo(copy);
                FuzzTargets.Run(target, copy.ToArray());
            });
            return 0;
        }
        Console.Error.WriteLine("usage: MetadataSharp.Fuzz TARGET | --replay TARGET FILE... | --extract OUT LABEL FILE... | --list");
        return 2;
    }

    // Instrumented assemblies write their coverage into this map; outside afl-fuzz it is private.
    private static unsafe void DetachFromAfl()
    {
        if (SharpFuzz.Common.Trace.SharedMem == null)
            SharpFuzz.Common.Trace.SharedMem = (byte*)NativeMemory.AllocZeroed(1 << 16);
    }

    // One line before and one after each input, flushed, so that a runner that sees the process die
    // knows which input killed it. The elapsed time is the best of the given number of runs.
    private static int Replay(string target, string[] files)
    {
        if (!FuzzTargets.Names.Contains(target, StringComparer.Ordinal))
        {
            Console.Error.WriteLine("unknown target: " + target);
            return 2;
        }
        int repeat = 1;
        if (files.Length >= 2 && files[0] == "--repeat")
        {
            repeat = Math.Max(1, int.Parse(files[1], System.Globalization.CultureInfo.InvariantCulture));
            files = files[2..];
        }
        foreach (string file in Expand(files))
        {
            Console.Out.WriteLine("BEGIN " + file);
            Console.Out.Flush();
            byte[] data = File.ReadAllBytes(file);
            long best = long.MaxValue;
            Exception? failure = null;
            for (int run = 0; run < repeat && failure is null; run++)
            {
                var clock = Stopwatch.StartNew();
                try { FuzzTargets.Run(target, data); }
                catch (Exception exception) { failure = exception; }
                best = Math.Min(best, clock.ElapsedMilliseconds);
            }
            if (failure is null)
            {
                Console.Out.WriteLine("PASS " + best + " " + file);
            }
            else
            {
                Console.Out.WriteLine("FAIL " + best + " " + file);
                foreach (string line in failure.ToString().Split('\n'))
                    Console.Out.WriteLine("| " + line.TrimEnd('\r'));
            }
            Console.Out.WriteLine("END");
            Console.Out.Flush();
        }
        return 0;
    }

    // Whole files in, a JSON line per file out (what it holds, or what reading it threw), and the
    // seeds of the packet and MakerNote targets written to OUT/iptc, OUT/xmp and OUT/maker.
    private static int Extract(string output, string label, string[] files)
    {
        foreach (string kind in new[] { "iptc", "xmp", "maker" })
            Directory.CreateDirectory(Path.Combine(output, kind));
        foreach (string file in Expand(files))
        {
            Console.Out.WriteLine("BEGIN " + file);
            Console.Out.Flush();
            var record = new Dictionary<string, object?> { ["path"] = file, ["label"] = label };
            try
            {
                byte[] data = File.ReadAllBytes(file);
                string extension = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                ImageMetadata metadata = ImageMetadata.Read(data);
                record["format"] = metadata.Format.ToString();
                record["exif"] = metadata.Exif is not null;
                record["iptc"] = metadata.Iptc is not null;
                record["xmp"] = metadata.Xmp is not null;
                record["icc"] = !metadata.IccProfile.IsEmpty;
                record["comments"] = metadata.Comments.Count;
                record["previews"] = metadata.Previews.Count;
                record["errors"] = metadata.Errors.Select(error => error.Family + ":" + error.Code).ToArray();
                if (metadata.Iptc is { } iptc)
                    record["iptc_seed"] = Seed(output, "iptc", label, extension, Raw(iptc));
                if (metadata.Xmp is { } xmp)
                    record["xmp_seed"] = Seed(output, "xmp", label, extension, Raw(xmp));
                string? maker = metadata.Exif?.MakerNote?.Format.ToString();
                record["maker"] = maker;
                if (MakerSeed(data, metadata.Exif) is { } makerSeed)
                    record["maker_seed"] = Seed(output, "maker", label + "-" + maker, extension, makerSeed);
            }
            catch (MetadataException refusal) when (Enum.IsDefined(refusal.Code))
            {
                record["refused"] = refusal.Code.ToString();
            }
            catch (Exception exception)
            {
                record["crash"] = exception.ToString();
            }
            Console.Out.WriteLine("JSON " + JsonSerializer.Serialize(record));
            Console.Out.WriteLine("END");
            Console.Out.Flush();
        }
        return 0;
    }

    // "@list" stands for the paths the file lists, one a line: a corpus is longer than a command line.
    private static IEnumerable<string> Expand(string[] arguments)
    {
        foreach (string argument in arguments)
        {
            if (!argument.StartsWith('@'))
            {
                yield return argument;
                continue;
            }
            foreach (string line in File.ReadLines(argument[1..]))
                if (line.Length != 0) yield return line;
        }
    }

    private static byte[] Raw(IptcPacket packet)
    {
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        return bytes;
    }

    private static byte[] Raw(XmpPacket packet)
    {
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        return bytes;
    }

    // A MakerNote seed keeps the note where its TIFF header put it, so that offsets counted from the
    // header still resolve: [make index][byte order][note start][note length][model length][model][TIFF].
    private static byte[]? MakerSeed(byte[] data, ExifMetadata? exif)
    {
        if (exif?.MakerNote is not { } note ||
            !exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Exif, 0x927C), out ExifEntry? entry))
            return null;
        int vendor = FuzzTargets.VendorIndex(exif.Make);
        // A Panasonic RW2 names the vendor of its note in its header rather than in a Make tag.
        if (vendor < 0 && note.Format == MakerNoteFormat.Panasonic) vendor = Array.IndexOf(FuzzTargets.Makes, "Panasonic");
        if (vendor < 0 || entry.Value.ByteLength < 8) return null;
        byte[] raw = new byte[entry.Value.ByteLength];
        entry.Value.CopyRawTo(raw);
        int at = data.AsSpan().IndexOf(raw);
        long tiffStart = at - note.Offset;
        if (at < 0 || tiffStart < 0) return null;
        byte[] model = Encoding.ASCII.GetBytes((exif.Model ?? string.Empty).TrimEnd('\0'));
        if (model.Length > 64) model = model[..64];
        int header = FuzzTargets.MakerHeaderLength + model.Length;
        long available = Math.Min(data.Length - tiffStart, MaxSeedBytes - header);
        if (note.Offset + raw.Length > available) return null;
        byte[] seed = new byte[header + available];
        seed[0] = (byte)vendor;
        seed[1] = (byte)(exif.ByteOrder == Endianness.BigEndian ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(seed.AsSpan(2, 4), (uint)note.Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(seed.AsSpan(6, 4), (uint)raw.Length);
        seed[10] = (byte)model.Length;
        model.CopyTo(seed, FuzzTargets.MakerHeaderLength);
        data.AsSpan((int)tiffStart, (int)available).CopyTo(seed.AsSpan(header));
        return seed;
    }

    private static string Seed(string output, string kind, string label, string extension, byte[] bytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string path = Path.Combine(output, kind, $"{label}-{(extension.Length == 0 ? "none" : extension)}-{hash[..16]}.bin");
        File.WriteAllBytes(path, bytes.Length > MaxSeedBytes ? bytes[..MaxSeedBytes] : bytes);
        return path;
    }
}
