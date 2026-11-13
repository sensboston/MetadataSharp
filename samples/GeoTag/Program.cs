using System.Globalization;
using System.Xml.Linq;
using MetadataSharp.Core;
using MetadataSharp.Formats;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("geotag --gpx track.gpx [--offset +hh:mm] [--max-gap 5m] [--dry-run] image...");
    return 0;
}
try
{
    string? gpxPath = Value("--gpx");
    if (gpxPath is null) throw new ArgumentException("--gpx is required.");
    TimeSpan offset = TimeSpan.TryParse(Value("--offset") ?? "00:00", CultureInfo.InvariantCulture, out var parsedOffset) ? parsedOffset : throw new ArgumentException("Invalid --offset.");
    TimeSpan maxGap = TimeSpan.TryParse(Value("--max-gap") ?? "5m", CultureInfo.InvariantCulture, out var parsedGap) ? parsedGap : ParseGap(Value("--max-gap") ?? "5m");
    bool dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
    string[] images = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal) && a != gpxPath && a != Value("--offset") && a != Value("--max-gap")).ToArray();
    if (images.Length == 0) throw new ArgumentException("Provide at least one image.");
    TrackPoint[] points = LoadTrack(gpxPath);
    foreach (string path in images)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ImageMetadata image = ImageMetadata.Read(bytes);
        DateTimeOffset? taken = image.Exif?.DateTakenWithOffset;
        if (taken is null && image.Exif?.DateTaken is DateTime local) taken = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc));
        if (taken is null) { Console.WriteLine($"skip (no capture time): {path}"); continue; }
        DateTimeOffset wanted = taken.Value.ToUniversalTime() + offset;
        TrackPoint? nearest = points.OrderBy(p => Math.Abs((p.Time - wanted).Ticks)).FirstOrDefault();
        if (nearest is null || Math.Abs((nearest.Time - wanted).Ticks) > maxGap.Ticks)
        {
            Console.WriteLine($"skip (no track point within {maxGap}): {path}"); continue;
        }
        Console.WriteLine($"{path}: {nearest.Latitude:F6}, {nearest.Longitude:F6} at {nearest.Time:O}" + (dryRun ? " [dry-run]" : string.Empty));
        if (!dryRun)
        {
            var edit = new ExifEdit();
            SetCoordinate(edit, 0x0001, 0x0002, nearest.Latitude, 'N', 'S');
            SetCoordinate(edit, 0x0003, 0x0004, nearest.Longitude, 'E', 'W');
            edit.Set(new(ExifDirectoryKind.Gps, 0x001D), nearest.Time.UtcDateTime.ToString("yyyy:MM:dd", CultureInfo.InvariantCulture));
            edit.Set(new(ExifDirectoryKind.Gps, 0x0007), new UnsignedRational[] {
                new((uint)nearest.Time.Hour, 1), new((uint)nearest.Time.Minute, 1), new((uint)nearest.Time.Second * 1_000_000u + (uint)(nearest.Time.Ticks % TimeSpan.TicksPerSecond / 10), 1_000_000) });
            SampleSupport.WriteAtomic(path, SampleSupport.SaveExif(bytes, edit));
        }
    }
    return 0;

    string? Value(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
}
catch (Exception e) when (e is IOException or MetadataException or ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine($"geotag: {e.Message}"); return 1;
}

static TimeSpan ParseGap(string value)
{
    char unit = char.ToLowerInvariant(value[^1]);
    string number = unit is 's' or 'm' or 'h' ? value[..^1] : value;
    double amount = double.Parse(number, CultureInfo.InvariantCulture);
    return unit switch { 's' => TimeSpan.FromSeconds(amount), 'h' => TimeSpan.FromHours(amount), _ => TimeSpan.FromMinutes(amount) };
}

static TrackPoint[] LoadTrack(string path)
{
    XDocument xml = XDocument.Load(path);
    return xml.Descendants().Where(e => e.Name.LocalName == "trkpt")
        .Select(e => new TrackPoint(
            double.Parse((string?)e.Attribute("lat") ?? throw new FormatException("GPX point has no latitude."), CultureInfo.InvariantCulture),
            double.Parse((string?)e.Attribute("lon") ?? throw new FormatException("GPX point has no longitude."), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(e.Elements().First(c => c.Name.LocalName == "time").Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)))
        .OrderBy(p => p.Time).ToArray();
}

static void SetCoordinate(ExifEdit edit, ushort refTag, ushort valueTag, double coordinate, char positive, char negative)
{
    double absolute = Math.Abs(coordinate);
    uint degrees = (uint)Math.Floor(absolute);
    double minutesValue = (absolute - degrees) * 60;
    uint minutes = (uint)Math.Floor(minutesValue);
    uint secondsMillionths = (uint)Math.Round((minutesValue - minutes) * 60 * 1_000_000, MidpointRounding.AwayFromZero);
    if (secondsMillionths >= 60_000_000) { secondsMillionths = 0; minutes++; }
    if (minutes >= 60) { minutes = 0; degrees++; }
    edit.Set(new(ExifDirectoryKind.Gps, refTag), coordinate < 0 ? negative.ToString() : positive.ToString());
    edit.Set(new(ExifDirectoryKind.Gps, valueTag), new UnsignedRational[] { new(degrees, 1), new(minutes, 1), new(secondsMillionths, 1_000_000) });
}

internal sealed record TrackPoint(double Latitude, double Longitude, DateTimeOffset Time);
