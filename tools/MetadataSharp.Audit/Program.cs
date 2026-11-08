using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.WebP;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.WebP;
using MetadataSharp.Tests.Tiff;

if (args.Length != 1) throw new ArgumentException("Supply an evidence output directory.");
Directory.CreateDirectory(args[0]);
byte[] jpeg = JpegFixture.Build(), png = PngFixture.Build(), webp = WebPFixture.Image(0);
byte[] tiff = StandaloneTiffFixture.Create().Bytes, large = new byte[1024 * 1024]; tiff.CopyTo(large, 0);
File.WriteAllBytes(Path.Combine(args[0], "consumer.tif"), tiff);
var exif = new ExifEdit(); exif.Set(new(ExifDirectoryKind.Ifd0, 274), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [1, 0]));
var text = new XmpEdit(); text.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("audit"));
var tiffEdit = new TiffMetadataEdit(); tiffEdit.Set(new(8, 305), ExifValue.FromRaw(TiffFieldType.Ascii, 6, Endianness.LittleEndian, "audit\0"u8));
long TiffSave(byte[] input) { using var doc = TiffMetadataDocument.Open(new MemoryStream(input, false)); using var output = new MemoryStream(); doc.Save(output, tiffEdit); return output.Length; }
var workloads = new (string Name, byte[] Input, int Iterations, Func<long> Run)[] {
    ("jpeg-open", jpeg, 100, () => { using var doc = JpegMetadataDocument.Open(new MemoryStream(jpeg, false)); return jpeg.Length; }),
    ("jpeg-exif-save", jpeg, 100, () => { using var doc = JpegMetadataDocument.Open(new MemoryStream(jpeg, false)); using var output = new MemoryStream(); doc.Save(output, exif); return output.Length; }),
    ("png-exif-save", png, 100, () => { using var doc = PngMetadataDocument.Open(new MemoryStream(png, false)); using var output = new MemoryStream(); doc.SaveExif(output, exif); return output.Length; }),
    ("webp-exif-save", webp, 100, () => { using var doc = WebPMetadataDocument.Open(new MemoryStream(webp, false)); using var output = new MemoryStream(); doc.SaveExif(output, exif); return output.Length; }),
    ("tiff-layout-4k", tiff, 100, () => TiffLayout.Read(new MemoryStream(tiff, false)).Directories.Count),
    ("tiff-edit-4k", tiff, 100, () => TiffSave(tiff)),
    ("tiff-edit-1m", large, 20, () => TiffSave(large)),
    ("xmp-create-apply", [], 100, () => XmpPacket.CreateEmpty().Apply(text)!.ByteLength)
};
var results = new List<object>();
foreach (var w in workloads)
{
    long expected = w.Run(); for (int i = 0; i < 10; i++) if (w.Run() != expected) throw new InvalidOperationException("Unstable workload result.");
    var samples = new List<object>();
    for (int sample = 0; sample < 5; sample++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long allocated = GC.GetTotalAllocatedBytes(true), start = Stopwatch.GetTimestamp();
        for (int i = 0; i < w.Iterations; i++) if (w.Run() != expected) throw new InvalidOperationException("Workload result changed.");
        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        long bytes = GC.GetTotalAllocatedBytes(true) - allocated;
        samples.Add(new { seconds, operationsPerSecond = w.Iterations / seconds, allocatedBytesPerOperation = (double)bytes / w.Iterations,
            nominalInputMiBPerSecond = w.Input.Length * (double)w.Iterations / 1048576 / seconds });
    }
    results.Add(new { name = w.Name, inputBytes = w.Input.Length, inputSha256 = Convert.ToHexString(SHA256.HashData(w.Input)), iterationsPerSample = w.Iterations, result = expected, samples });
}
var report = new { schema = 1, sourceCommit = Environment.GetEnvironmentVariable("AUDIT_SOURCE_COMMIT"),
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(), processors = Environment.ProcessorCount,
    serverGc = GCSettings.IsServerGC, stopwatchFrequency = Stopwatch.Frequency,
    allocationScope = "GC.GetTotalAllocatedBytes(true): process-wide, synchronous workload, includes document/output allocation; not retained memory",
    provenance = "MIT-authored linked test fixtures; TIFF 1-MiB input pads the 4096-byte fixture with zero unreferenced storage; XMP input is empty model",
    workloads = results };
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(args[0], "baseline.json"), json); Console.WriteLine(json);
