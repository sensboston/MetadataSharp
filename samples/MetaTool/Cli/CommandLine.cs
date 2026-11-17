using System.Globalization;
using MetadataSharp.Core.Operations;

namespace MetaTool.Cli;

/// <summary>What reading a command line gave: the options, the notes worth printing and the error, if any.</summary>
/// <param name="Options">The options the run was asked for; usable as far as reading got.</param>
/// <param name="Warnings">The notes about options that were ignored or only partly read.</param>
/// <param name="Error">Why the command line cannot be carried out, or null when it can.</param>
internal sealed record ParseOutcome(Options Options, IReadOnlyList<string> Warnings, string? Error);

/// <summary>
/// The argument grammar of the Exiv2 command line, read as the reference reads it: one token at a
/// time, letters that may be bundled after one dash, an argument attached (<c>-pa</c>) or separate
/// (<c>-p a</c>), <c>--</c> to stop reading options, and the first word that is not an option naming
/// the action.
/// </summary>
internal static class CommandLine
{
    /// <summary>The option letters that take an argument.</summary>
    private const string WithArgument = "pPdeiCcMmlSrnQKgaYOD";

    /// <summary>The option letters that take none.</summary>
    private const string WithoutArgument = "bfFhkqtTuvV";

    /// <summary>Reads a command line.</summary>
    /// <param name="args">The arguments after the program name.</param>
    /// <returns>The options, the notes and the error of the command line.</returns>
    internal static ParseOutcome Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new Options();
        var warnings = new List<string>();
        string? error = null;
        bool endOfOptions = false, firstWord = true;

        for (int i = 0; i < args.Count && error is null; i++)
        {
            string token = args[i];
            if (!endOfOptions && token == "--") { endOfOptions = true; continue; }
            if (!endOfOptions && token.Length > 1 && token[0] == '-')
            {
                error = ParseOption(token, args, ref i, options, warnings);
                continue;
            }

            // The first word that is not an option names the action; every later word is a file. When
            // the word names no action the run prints, as the reference defaults to.
            if (firstWord)
            {
                firstWord = false;
                if (TryAction(token, out ToolAction action))
                {
                    if (options.ActionSet && options.Action != action)
                        error = $"action {Name(action)} is not compatible with the options already given";
                    options.Action = action;
                    options.ActionSet = true;
                    continue;
                }
                if (!options.ActionSet)
                {
                    options.Action = ToolAction.Print;
                    options.ActionSet = true;
                }
            }
            options.Files.Add(token);
        }

        if (error is null && !options.ShowHelp && !options.ShowVersion)
        {
            options.Action = options.ActionSet ? options.Action : ToolAction.Print;
            error = Validate(options);
        }
        return new ParseOutcome(options, warnings, error);
    }

    /// <summary>The word of an action, as the usage text writes it.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The short name of the action.</returns>
    internal static string Name(ToolAction action) => action switch
    {
        ToolAction.Print => "print",
        ToolAction.Extract => "extract",
        ToolAction.Insert => "insert",
        ToolAction.Erase => "delete",
        ToolAction.Modify => "modify",
        ToolAction.Adjust => "adjust",
        ToolAction.Rename => "rename",
        ToolAction.FixIso => "fixiso",
        ToolAction.FixCom => "fixcom",
        _ => "none",
    };

    private static bool TryAction(string word, out ToolAction action)
    {
        action = word switch
        {
            "pr" or "print" => ToolAction.Print,
            "ex" or "extract" => ToolAction.Extract,
            "in" or "insert" => ToolAction.Insert,
            "rm" or "delete" => ToolAction.Erase,
            "ad" or "adjust" => ToolAction.Adjust,
            "mo" or "modify" => ToolAction.Modify,
            "mv" or "rename" => ToolAction.Rename,
            "fi" or "fixiso" => ToolAction.FixIso,
            "fc" or "fixcom" or "fixcomment" => ToolAction.FixCom,
            _ => ToolAction.None,
        };
        return action != ToolAction.None;
    }

    // One token of options: letters after the dash, and after the one that takes an argument, the
    // rest of the token or the next token.
    private static string? ParseOption(string token, IReadOnlyList<string> args, ref int index, Options options, List<string> warnings)
    {
        for (int j = 1; j < token.Length; j++)
        {
            char letter = token[j];
            string? value = null;
            if (WithArgument.IndexOf(letter) >= 0)
            {
                if (j + 1 < token.Length) value = token[(j + 1)..];
                else if (index + 1 < args.Count) value = args[++index];
                else return $"option -{letter} requires an argument";
                j = token.Length;
            }
            else if (WithoutArgument.IndexOf(letter) < 0)
            {
                return $"unrecognized option -{letter}";
            }

            string? error = letter switch
            {
                'h' => Help(options),
                'V' => Version(options),
                'b' => null, // Obsolete in the reference; accepted and ignored so that old scripts run.
                'p' => Print(value!, options, warnings),
                'P' => PrintFlags(value!, options, warnings),
                'd' => Target(value!, TargetUse.Delete, options),
                'e' => Target(value!, TargetUse.Extract, options),
                'C' => Target(value!, TargetUse.Extract, options),
                'i' => Target(value!, TargetUse.Insert, options),
                'c' => Comment(value!, options),
                'm' => CommandFile(value!, options),
                'M' => CommandLineOption(value!, options),
                'l' => Directory(value!, options),
                'S' => Suffix(value!, options),
                'r' => RenameFormat(value!, options),
                'n' => Charset(value!, options),
                'Q' => LogLevel(value!, options),
                'K' => ExactKey(value!, options),
                'g' => Pattern(value!, options),
                'a' => TimeAdjust(value!, options),
                'Y' => YearAdjust(value!, options),
                'O' => MonthAdjust(value!, options),
                'D' => DayAdjust(value!, options),
                'f' => Force(options),
                'F' => RenameOnCollision(options),
                'k' => Preserve(options),
                'q' => Quiet(options),
                't' => SetFileTime(options),
                'T' => OnlySetFileTime(options),
                'u' => HideUnknown(options),
                'v' => Verbose(options),
                _ => $"unrecognized option -{letter}",
            };
            if (error is not null) return error;
        }
        return null;
    }

    private static string? Help(Options options) { options.ShowHelp = true; return null; }

    private static string? Version(Options options) { options.ShowVersion = true; return null; }

    private static string? Force(Options options) { options.Force = true; return null; }

    private static string? RenameOnCollision(Options options) { options.RenameOnCollision = true; return null; }

    private static string? Preserve(Options options) { options.PreserveTimestamps = true; return null; }

    private static string? Quiet(Options options) { options.Quiet = true; return null; }

    private static string? SetFileTime(Options options) { options.SetFileTime = true; return null; }

    private static string? OnlySetFileTime(Options options) { options.OnlySetFileTime = true; return null; }

    private static string? HideUnknown(Options options) { options.HideUnknown = true; return null; }

    private static string? Verbose(Options options) { options.Verbose = true; return null; }

    // -p: the shorthand modes of the reference, with the families and columns each of them stands for.
    private static string? Print(string value, Options options, List<string> warnings)
    {
        if (options.ActionSet && options.Action != ToolAction.Print)
            return "option -p is not compatible with a previous option";
        if (options.ModeSet || options.ColumnsSet)
        {
            warnings.Add($"ignoring surplus option -p{value}");
            return null;
        }
        if (value.Length == 0) return "option -p requires an argument";
        options.Action = ToolAction.Print;
        options.ActionSet = true;
        options.ModeSet = true;
        switch (value[0])
        {
            case 's': options.Mode = PrintMode.Summary; return null;
            case 'c': options.Mode = PrintMode.Comment; return null;
            case 'p': options.Mode = PrintMode.Preview; return null;
            case 'C': options.Mode = PrintMode.Icc; return null;
            case 'X': options.Mode = PrintMode.XmpPacket; return null;
            case 'a': options.Families = PrintFamilies.All; options.Columns = PrintColumns.Key | PrintColumns.Type | PrintColumns.Count | PrintColumns.Translated; return null;
            case 'e': options.Families = PrintFamilies.Exif; options.Columns = PrintColumns.Key | PrintColumns.Type | PrintColumns.Count | PrintColumns.Value; return null;
            case 't': options.Families = PrintFamilies.Exif; options.Columns = PrintColumns.Key | PrintColumns.Type | PrintColumns.Count | PrintColumns.Translated; return null;
            case 'v': options.Families = PrintFamilies.Exif; options.Columns = PrintColumns.Tag | PrintColumns.Group | PrintColumns.Name | PrintColumns.Type | PrintColumns.Count | PrintColumns.Value; return null;
            case 'h': options.Families = PrintFamilies.Exif; options.Columns = PrintColumns.Tag | PrintColumns.Group | PrintColumns.Name | PrintColumns.Type | PrintColumns.Count | PrintColumns.Size | PrintColumns.Hex; return null;
            case 'i': options.Families = PrintFamilies.Iptc; options.Columns = PrintColumns.Key | PrintColumns.Type | PrintColumns.Count | PrintColumns.Translated; return null;
            case 'x': options.Families = PrintFamilies.Xmp; options.Columns = PrintColumns.Key | PrintColumns.Type | PrintColumns.Count | PrintColumns.Translated; return null;
            case 'R' or 'S':
                return $"print mode -p{value[0]} is not offered: the library exposes metadata, not the structure dump of a file";
            default:
                return $"unrecognized print mode -p{value[0]}";
        }
    }

    // -P: the columns, and with them the families a list print covers.
    private static string? PrintFlags(string value, Options options, List<string> warnings)
    {
        if (options.ActionSet && options.Action != ToolAction.Print)
            return "option -P is not compatible with a previous option";
        if (options.ColumnsSet)
        {
            warnings.Add($"ignoring surplus option -P{value}");
            return null;
        }
        if (value.Length == 0) return "option -P requires an argument";
        options.Action = ToolAction.Print;
        options.ActionSet = true;
        options.Mode = PrintMode.List;
        options.ModeSet = true;
        options.ColumnsSet = true;
        foreach (char flag in value)
        {
            switch (flag)
            {
                case 'E': options.Families |= PrintFamilies.Exif; break;
                case 'I': options.Families |= PrintFamilies.Iptc; break;
                case 'X': options.Families |= PrintFamilies.Xmp; break;
                case 'x': options.Columns |= PrintColumns.Tag; break;
                case 'g': options.Columns |= PrintColumns.Group; break;
                case 'k': options.Columns |= PrintColumns.Key; break;
                case 'l': options.Columns |= PrintColumns.Label; break;
                case 'd': options.Columns |= PrintColumns.Description; break;
                case 'n': options.Columns |= PrintColumns.Name; break;
                case 'y': options.Columns |= PrintColumns.Type; break;
                case 'c': options.Columns |= PrintColumns.Count; break;
                case 's': options.Columns |= PrintColumns.Size; break;
                case 'v': options.Columns |= PrintColumns.Value; break;
                case 't': options.Columns |= PrintColumns.Translated; break;
                case 'V': options.Columns |= PrintColumns.Set | PrintColumns.Key | PrintColumns.Type | PrintColumns.Value; break;
                case 'h': options.Columns |= PrintColumns.Hex; break;
                default: return $"unrecognized print item -P{flag}";
            }
        }
        return null;
    }

    private enum TargetUse { Delete, Extract, Insert }

    // -d, -e, -i, -C: the targets, with the two-letter XX of the raw XMP packet and the preview
    // numbers of -ep1,3.
    private static string? Target(string value, TargetUse use, Options options)
    {
        ToolAction action = use switch
        {
            TargetUse.Delete => ToolAction.Erase,
            TargetUse.Extract => ToolAction.Extract,
            _ => ToolAction.Insert,
        };
        if (options.ActionSet && options.Action != action)
            return $"option -{(use == TargetUse.Delete ? 'd' : use == TargetUse.Extract ? 'e' : 'i')} is not compatible with a previous option";
        options.Action = action;
        options.ActionSet = true;

        for (int i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case 'e': options.Targets |= Targets.Exif; break;
                case 'i': options.Targets |= Targets.Iptc; break;
                case 'x': options.Targets |= Targets.Xmp; break;
                case 'c': options.Targets |= Targets.Comment; break;
                case 't': options.Targets |= Targets.Thumbnail; break;
                case 'C': options.Targets |= Targets.Icc; break;
                case 'I': options.Targets |= Targets.IptcRaw; break;
                case '-': options.Targets |= Targets.StdInOut; break;
                case 'a': options.Targets |= Targets.All; break;
                case 'X':
                    if (i > 0)
                    {
                        // -eXX: the XMP packet as it is, not a sidecar converted on the way.
                        options.Targets |= Targets.XmpRaw;
                        options.Targets &= ~(Targets.XmpSidecar | Targets.Exif | Targets.Iptc | Targets.Xmp);
                    }
                    else
                    {
                        options.Targets |= Targets.XmpSidecar | Targets.Exif | Targets.Iptc | Targets.Xmp;
                    }
                    break;
                case 'p':
                    if (use != TargetUse.Extract) return $"unrecognized target -{(use == TargetUse.Insert ? 'i' : 'd')}p";
                    options.Targets |= Targets.Preview;
                    i += PreviewNumbers(value, i + 1, options);
                    break;
                default:
                    return $"unrecognized target {value[i]}";
            }
        }
        return null;
    }

    // The numbers of -ep2,3 or -ep: at least one has to follow, and every one of them counts from one.
    private static int PreviewNumbers(string value, int start, Options options)
    {
        int at = start;
        while (at < value.Length)
        {
            int end = at;
            while (end < value.Length && char.IsAsciiDigit(value[end])) end++;
            if (end == at) break;
            if (!int.TryParse(value.AsSpan(at, end - at), NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1)
                return end - start;
            options.PreviewNumbers.Add(number);
            at = end;
            if (at < value.Length && value[at] == ',') at++;
            else break;
        }
        return at - start;
    }

    private static string? Comment(string value, Options options)
    {
        if (options.ActionSet && options.Action is not (ToolAction.Modify or ToolAction.Extract or ToolAction.Insert))
            return "option -c is not compatible with a previous option";
        options.Action = options.ActionSet ? options.Action : ToolAction.Modify;
        options.ActionSet = true;
        options.Comment = Unescape(value);
        options.CommentSet = true;
        return null;
    }

    private static string? CommandFile(string value, Options options)
    {
        if (options.ActionSet && options.Action is not (ToolAction.Modify or ToolAction.Extract or ToolAction.Insert))
            return "option -m is not compatible with a previous option";
        options.Action = options.ActionSet ? options.Action : ToolAction.Modify;
        options.ActionSet = true;
        options.CommandFiles.Add(value);
        return null;
    }

    private static string? CommandLineOption(string value, Options options)
    {
        if (options.ActionSet && options.Action is not (ToolAction.Modify or ToolAction.Extract or ToolAction.Insert))
            return "option -M is not compatible with a previous option";
        options.Action = options.ActionSet ? options.Action : ToolAction.Modify;
        options.ActionSet = true;
        options.CommandLines.Add(value);
        return null;
    }

    private static string? Directory(string value, Options options) { options.Directory = value; return null; }

    private static string? Suffix(string value, Options options) { options.Suffix = value; return null; }

    private static string? RenameFormat(string value, Options options)
    {
        options.RenameFormat = value.Length == 0 ? MetadataFileNames.DefaultFormat : value;
        options.RenameFormatSet = true;
        return null;
    }

    private static string? Charset(string value, Options options)
    {
        options.CharsetName = value;
        try
        {
            System.Text.Encoding.GetEncoding(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return $"unknown charset {value}";
        }
        return null;
    }

    private static string? LogLevel(string value, Options options)
    {
        if (value.Length == 0) return "option -Q requires an argument";
        char level = char.ToLowerInvariant(value[0]);
        if (level is not ('d' or 'i' or 'w' or 'e' or 'm')) return $"unknown log level -Q{value}";
        options.LogLevel = level;
        options.Quiet = level == 'm';
        return null;
    }

    private static string? ExactKey(string value, Options options) { options.ExactKeys.Add(value); return null; }

    private static string? Pattern(string value, Options options) { options.Patterns.Add(value); return null; }

    // -a [-][HH[:MM[:SS]]]: the seconds to move every timestamp by.
    private static string? TimeAdjust(string value, Options options)
    {
        int at = 0;
        bool negative = false;
        if (at < value.Length && (value[at] == '-' || value[at] == '+'))
        {
            negative = value[at] == '-';
            at++;
        }
        int hours = 0, minutes = 0, seconds = 0;
        if (!ReadNumber(value, ref at, out hours)) return $"invalid time adjustment {value}";
        if (at < value.Length && value[at] == ':')
        {
            at++;
            if (!ReadNumber(value, ref at, out minutes) || minutes > 59) return $"invalid time adjustment {value}";
            if (at < value.Length && value[at] == ':')
            {
                at++;
                if (!ReadNumber(value, ref at, out seconds) || seconds > 59) return $"invalid time adjustment {value}";
            }
        }
        if (at != value.Length) return $"invalid time adjustment {value}";
        TimeSpan span = new(0, hours, minutes, seconds);
        options.TimeAdjustment = negative ? span.Negate() : span;
        return null;
    }

    private static bool ReadNumber(string text, ref int at, out int number)
    {
        int start = at;
        while (at < text.Length && char.IsAsciiDigit(text[at])) at++;
        number = 0;
        return at > start && int.TryParse(text.AsSpan(start, at - start), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static string? YearAdjust(string value, Options options) =>
        ReadSigned(value, out int years) ? Set(() => options.YearAdjustment = years) : $"invalid year adjustment {value}";

    private static string? MonthAdjust(string value, Options options) =>
        ReadSigned(value, out int months) ? Set(() => options.MonthAdjustment = months) : $"invalid month adjustment {value}";

    private static string? DayAdjust(string value, Options options) =>
        ReadSigned(value, out int days) ? Set(() => options.DayAdjustment = days) : $"invalid day adjustment {value}";

    private static string? Set(Action assign) { assign(); return null; }

    private static bool ReadSigned(string text, out int number) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);

    // The escapes the reference reads in a comment or a command: \\ \n \r \t and \uXXXX.
    private static string Unescape(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal)) return text;
        var text2 = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 == text.Length) { text2.Append(text[i]); continue; }
            i++;
            switch (text[i])
            {
                case '\\': text2.Append('\\'); break;
                case 'n': text2.Append('\n'); break;
                case 'r': text2.Append('\r'); break;
                case 't': text2.Append('\t'); break;
                case 'u' when i + 4 < text.Length: AppendUnicode(text, ref i, text2); break;
                default: text2.Append('\\').Append(text[i]); break;
            }
        }
        return text2.ToString();
    }

    private static void AppendUnicode(string text, ref int at, System.Text.StringBuilder target)
    {
        if (int.TryParse(text.AsSpan(at + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
        {
            target.Append((char)code);
            at += 4;
        }
        else
        {
            target.Append("\\u");
        }
    }

    // What an action needs before it can run, and the files every action needs (help and version need none).
    private static string? Validate(Options options)
    {
        if (options.ShowHelp || options.ShowVersion) return null;
        if (options.Files.Count == 0) return "no file given";
        switch (options.Action)
        {
            case ToolAction.Extract when options.Targets == Targets.None:
                return "extract needs a target: -e a, e, t, i, x, c, C, I, X, XX or p[n,...]";
            case ToolAction.Insert when options.Targets == Targets.None:
                return "insert needs a target: -i a, e, t, i, x, c, C, X or XX";
            case ToolAction.Erase when options.Targets == Targets.None:
                return "delete needs a target: -d a, e, t, i, x, c, C, I or -";
            case ToolAction.Modify when options.CommandLines.Count == 0 && options.CommandFiles.Count == 0 && !options.CommentSet:
                return "modify needs a command: -M 'set key value', -m file or -c comment";
            case ToolAction.Adjust when options.TimeAdjustment == TimeSpan.Zero &&
                options.YearAdjustment == 0 && options.MonthAdjustment == 0 && options.DayAdjustment == 0:
                return "adjust needs an amount: -a time, -Y years, -O months or -D days";
            default:
                return null;
        }
    }
}
