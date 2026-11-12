namespace MetadataSharp.Core;

/// <summary>What the GPSAltitudeRef tag says the altitude is measured from, and on which side of it.</summary>
/// <remarks>
/// The names follow Exif 3.0. Earlier versions of the standard defined only 0 and 1 and called both
/// a sea level reference.
/// </remarks>
public enum GpsAltitudeReference : byte
{
    /// <summary>At or above the ellipsoidal surface.</summary>
    AboveEllipsoid = 0,
    /// <summary>Below the ellipsoidal surface.</summary>
    BelowEllipsoid = 1,
    /// <summary>At or above the sea level reference.</summary>
    AboveSeaLevel = 2,
    /// <summary>Below the sea level reference.</summary>
    BelowSeaLevel = 3,
}

/// <summary>A position read from the GPS directory, in the form a map takes.</summary>
/// <param name="Latitude">Decimal degrees from -90 to 90; negative in the southern hemisphere.</param>
/// <param name="Longitude">Decimal degrees from -180 to 180; negative in the western hemisphere.</param>
/// <param name="Altitude">
/// Metres, negative when <paramref name="AltitudeReference" /> is a "below" value; null when the
/// directory has no altitude that can be read.
/// </param>
/// <param name="AltitudeReference">
/// The reference the altitude tag names; null when the directory has no altitude or no reference
/// for it, in which case the altitude is taken as stored, at or above its reference.
/// </param>
public sealed record GpsPosition(
    double Latitude,
    double Longitude,
    double? Altitude,
    GpsAltitudeReference? AltitudeReference);
