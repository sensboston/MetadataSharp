namespace MetadataSharp.Core;

/// <summary>
/// The value of the Orientation tag: where row 0 and column 0 of the stored image lie on the image
/// as it is meant to be seen. The names are the two edges, row first.
/// </summary>
public enum ExifOrientation
{
    /// <summary>Row 0 is the top, column 0 the left side: the image is stored as it is seen.</summary>
    TopLeft = 1,
    /// <summary>Row 0 is the top, column 0 the right side: mirrored left to right.</summary>
    TopRight = 2,
    /// <summary>Row 0 is the bottom, column 0 the right side: rotated by 180 degrees.</summary>
    BottomRight = 3,
    /// <summary>Row 0 is the bottom, column 0 the left side: mirrored top to bottom.</summary>
    BottomLeft = 4,
    /// <summary>Row 0 is the left side, column 0 the top: mirrored along the diagonal from the top left.</summary>
    LeftTop = 5,
    /// <summary>Row 0 is the right side, column 0 the top: shown upright by turning it 90 degrees clockwise.</summary>
    RightTop = 6,
    /// <summary>Row 0 is the right side, column 0 the bottom: mirrored along the diagonal from the top right.</summary>
    RightBottom = 7,
    /// <summary>Row 0 is the left side, column 0 the bottom: shown upright by turning it 90 degrees counter-clockwise.</summary>
    LeftBottom = 8,
}
