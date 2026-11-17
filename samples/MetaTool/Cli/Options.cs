using MetadataSharp.Core.Operations;

namespace MetaTool.Cli;

/// <summary>The nine actions of the Exiv2 command line, by their short and long names.</summary>
internal enum ToolAction
{
    /// <summary>No action named yet; the first file name falls back to <see cref="Print"/>.</summary>
    None,
    /// <summary><c>pr</c> / <c>print</c>.</summary>
    Print,
    /// <summary><c>ex</c> / <c>extract</c>.</summary>
    Extract,
    /// <summary><c>in</c> / <c>insert</c>.</summary>
    Insert,
    /// <summary><c>rm</c> / <c>delete</c>.</summary>
    Erase,
    /// <summary><c>mo</c> / <c>modify</c>.</summary>
    Modify,
    /// <summary><c>ad</c> / <c>adjust</c>.</summary>
    Adjust,
    /// <summary><c>mv</c> / <c>rename</c>.</summary>
    Rename,
    /// <summary><c>fi</c> / <c>fixiso</c>.</summary>
    FixIso,
    /// <summary><c>fc</c> / <c>fixcom</c> / <c>fixcomment</c>.</summary>
    FixCom,
}

/// <summary>What the print action prints.</summary>
internal enum PrintMode
{
    /// <summary>One row per tag or property: <c>-p a</c>, <c>-p e</c>, <c>-P</c>.</summary>
    List,
    /// <summary>The report of <c>-p s</c>.</summary>
    Summary,
    /// <summary>The free-text comment of <c>-p c</c>.</summary>
    Comment,
    /// <summary>The preview listing of <c>-p p</c>.</summary>
    Preview,
    /// <summary>The ICC profile bytes of <c>-p C</c>.</summary>
    Icc,
    /// <summary>The XMP packet bytes of <c>-p X</c>.</summary>
    XmpPacket,
}

/// <summary>The metadata families a list print covers, the <c>E</c>, <c>I</c> and <c>X</c> flags of <c>-P</c>.</summary>
[Flags]
internal enum PrintFamilies
{
    /// <summary>None.</summary>
    None = 0,
    /// <summary>EXIF tags, MakerNote tags included.</summary>
    Exif = 1,
    /// <summary>IPTC datasets.</summary>
    Iptc = 2,
    /// <summary>XMP properties.</summary>
    Xmp = 4,
    /// <summary>All three.</summary>
    All = Exif | Iptc | Xmp,
}

/// <summary>The columns of a list print, the flags of <c>-P</c>.</summary>
[Flags]
internal enum PrintColumns
{
    /// <summary>No column.</summary>
    None = 0,
    /// <summary><c>x</c>: the tag number as <c>0x010f</c>.</summary>
    Tag = 1 << 0,
    /// <summary><c>g</c>: the group, <c>Image</c> or <c>CanonCs</c>.</summary>
    Group = 1 << 1,
    /// <summary><c>k</c>: the key, <c>Exif.Image.Make</c>.</summary>
    Key = 1 << 2,
    /// <summary><c>l</c>: the label of the tag table.</summary>
    Label = 1 << 3,
    /// <summary><c>d</c>: the description of the tag table.</summary>
    Description = 1 << 4,
    /// <summary><c>n</c>: the name of the tag in the tag table.</summary>
    Name = 1 << 5,
    /// <summary><c>y</c>: the type, <c>Ascii</c> or <c>Long</c>.</summary>
    Type = 1 << 6,
    /// <summary><c>c</c>: the number of values.</summary>
    Count = 1 << 7,
    /// <summary><c>s</c>: the size of the value in bytes.</summary>
    Size = 1 << 8,
    /// <summary><c>v</c>: the value as stored.</summary>
    Value = 1 << 9,
    /// <summary><c>t</c>: the value as the tag table reads it.</summary>
    Translated = 1 << 10,
    /// <summary><c>V</c>: the word <c>set</c> before the key, type and value of a modify command.</summary>
    Set = 1 << 11,
    /// <summary><c>h</c>: the value as a hexadecimal dump.</summary>
    Hex = 1 << 12,
}

/// <summary>The pieces of a file an action reads or writes: the targets of <c>-d</c>, <c>-e</c> and <c>-i</c>.</summary>
[Flags]
internal enum Targets
{
    /// <summary>None.</summary>
    None = 0,
    /// <summary><c>e</c>: the EXIF block.</summary>
    Exif = 1 << 0,
    /// <summary><c>i</c>: the IPTC stream as datasets.</summary>
    Iptc = 1 << 1,
    /// <summary><c>x</c>: the XMP packet.</summary>
    Xmp = 1 << 2,
    /// <summary><c>c</c>: the free-text comment.</summary>
    Comment = 1 << 3,
    /// <summary><c>t</c>: the thumbnail of the EXIF block.</summary>
    Thumbnail = 1 << 4,
    /// <summary><c>C</c>: the ICC profile.</summary>
    Icc = 1 << 5,
    /// <summary><c>I</c>: the IPTC stream as the raw IIM bytes it is.</summary>
    IptcRaw = 1 << 6,
    /// <summary><c>X</c>: an XMP sidecar, converted on the way out or in.</summary>
    XmpSidecar = 1 << 7,
    /// <summary><c>XX</c>: the XMP packet as the bytes it is, or an EXV payload.</summary>
    XmpRaw = 1 << 8,
    /// <summary><c>p</c>: one or more previews.</summary>
    Preview = 1 << 9,
    /// <summary><c>-</c>: read from standard input or write to standard output.</summary>
    StdInOut = 1 << 10,
    /// <summary><c>a</c>: every family that is not a picture or a profile.</summary>
    All = Exif | Iptc | Xmp | Comment,
}

/// <summary>What one run of the tool is asked to do: the options, the action and the files.</summary>
internal sealed class Options
{
    /// <summary>The action, or <see cref="ToolAction.None"/> until one is named.</summary>
    internal ToolAction Action { get; set; }
    /// <summary>True once an option or a word named the action.</summary>
    internal bool ActionSet { get; set; }
    /// <summary>What the print action prints.</summary>
    internal PrintMode Mode { get; set; }
    /// <summary>True once an option named what the print action prints.</summary>
    internal bool ModeSet { get; set; }
    /// <summary>The families a list print covers.</summary>
    internal PrintFamilies Families { get; set; }
    /// <summary>The columns of a list print.</summary>
    internal PrintColumns Columns { get; set; }
    /// <summary>True once <c>-P</c> named the columns.</summary>
    internal bool ColumnsSet { get; set; }
    /// <summary>The targets of the action.</summary>
    internal Targets Targets { get; set; }
    /// <summary>The previews <c>-ep2,3</c> names, one-based; empty means every preview.</summary>
    internal List<int> PreviewNumbers { get; } = [];
    /// <summary>The keys <c>-K</c> named, each of which a row has to equal.</summary>
    internal List<string> ExactKeys { get; } = [];
    /// <summary>The patterns <c>-g</c> named, at least one of which a key has to match.</summary>
    internal List<string> Patterns { get; } = [];
    /// <summary>The directory <c>-l</c> named for sidecars and detachable payloads.</summary>
    internal string? Directory { get; set; }
    /// <summary>The suffix <c>-S</c> named for the source files of insert.</summary>
    internal string? Suffix { get; set; }
    /// <summary>The format of the name <c>-r</c> gives.</summary>
    internal string RenameFormat { get; set; } = MetadataFileNames.DefaultFormat;
    /// <summary>True once <c>-r</c> named a format.</summary>
    internal bool RenameFormatSet { get; set; }
    /// <summary>The seconds <c>-a</c> named.</summary>
    internal TimeSpan TimeAdjustment { get; set; }
    /// <summary>The years <c>-Y</c> named.</summary>
    internal int YearAdjustment { get; set; }
    /// <summary>The months <c>-O</c> named.</summary>
    internal int MonthAdjustment { get; set; }
    /// <summary>The days <c>-D</c> named.</summary>
    internal int DayAdjustment { get; set; }
    /// <summary>The comment <c>-c</c> named.</summary>
    internal string? Comment { get; set; }
    /// <summary>True once <c>-c</c> named a comment.</summary>
    internal bool CommentSet { get; set; }
    /// <summary>The commands <c>-M</c> gave, in the order they were given.</summary>
    internal List<string> CommandLines { get; } = [];
    /// <summary>The command files <c>-m</c> gave, in the order they were given.</summary>
    internal List<string> CommandFiles { get; } = [];
    /// <summary>The charset <c>-n</c> named for EXIF user comments, <c>utf-8</c> by default.</summary>
    internal string CharsetName { get; set; } = "utf-8";
    /// <summary>True when <c>-k</c> asks to keep the file timestamps of an update.</summary>
    internal bool PreserveTimestamps { get; set; }
    /// <summary>True when <c>-t</c> asks to set the file time from the EXIF time as well.</summary>
    internal bool SetFileTime { get; set; }
    /// <summary>True when <c>-T</c> asks to set the file time and nothing else.</summary>
    internal bool OnlySetFileTime { get; set; }
    /// <summary>True when <c>-q</c> silences warnings.</summary>
    internal bool Quiet { get; set; }
    /// <summary>True when <c>-v</c> asks for the progress a quiet tool does not print.</summary>
    internal bool Verbose { get; set; }
    /// <summary>True when <c>-u</c> asks to hide tags without a name.</summary>
    internal bool HideUnknown { get; set; }
    /// <summary>True when <c>-f</c> asks to overwrite files that exist without asking.</summary>
    internal bool Force { get; set; }
    /// <summary>True when <c>-F</c> asks to rename rather than overwrite or ask.</summary>
    internal bool RenameOnCollision { get; set; }
    /// <summary>The level <c>-Q</c> named: <c>d</c>, <c>i</c>, <c>w</c>, <c>e</c> or <c>m</c>.</summary>
    internal char LogLevel { get; set; } = 'w';
    /// <summary>True when <c>-h</c> or <c>--help</c> asks for the usage text.</summary>
    internal bool ShowHelp { get; set; }
    /// <summary>True when <c>-V</c> or <c>--version</c> asks for the version.</summary>
    internal bool ShowVersion { get; set; }
    /// <summary>The files the run works on, in the order they were given.</summary>
    internal List<string> Files { get; } = [];

    /// <summary>The families a list print starts from when neither <c>-p</c> nor <c>-P</c> named any.</summary>
    internal PrintFamilies EffectiveFamilies => Families == PrintFamilies.None ? PrintFamilies.All : Families;

    /// <summary>The columns a list print uses when neither <c>-p</c> nor <c>-P</c> named any.</summary>
    internal PrintColumns EffectiveColumns => Columns == PrintColumns.None
        ? PrintColumns.Key | PrintColumns.Type | PrintColumns.Count | PrintColumns.Translated
        : Columns;
}
