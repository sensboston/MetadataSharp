using System.Text;
using MetadataSharp.Core;

namespace MetaTool.Cli;

/// <summary>The exit codes of the tool, each of which a script may test for.</summary>
internal static class ExitCodes
{
    /// <summary>Everything asked for was done.</summary>
    internal const int Ok = 0;

    /// <summary>An action failed: a file could not be read, written or changed, or a filter matched nothing.</summary>
    internal const int Failed = 1;

    /// <summary>The command line cannot be read: an unknown option, a missing argument or a missing action.</summary>
    internal const int Usage = 2;

    /// <summary>The file was read, but the metadata the action needs is not there.</summary>
    internal const int Absent = 3;

    /// <summary>The action asked for something this tool knowingly does not offer; nothing was changed.</summary>
    internal const int Refused = 4;
}

/// <summary>One run: the options, where its output goes, and the notes it prints.</summary>
internal sealed class Session
{
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly Stream standardInput;
    private readonly Stream standardOutput;
    private StreamReader? inputReader;

    /// <summary>Creates a session.</summary>
    /// <param name="options">The options the run was asked for.</param>
    /// <param name="output">Where the report of an action goes.</param>
    /// <param name="error">Where the notes and warnings go.</param>
    /// <param name="standardInput">Where <c>-</c> reads from.</param>
    /// <param name="standardOutput">Where the raw payloads of <c>-p C</c>, <c>-p X</c> and <c>-e -</c> go.</param>
    internal Session(Options options, TextWriter output, TextWriter error, Stream standardInput, Stream standardOutput)
    {
        Options = options;
        this.output = output;
        this.error = error;
        this.standardInput = standardInput;
        this.standardOutput = standardOutput;
    }

    /// <summary>The options of the run.</summary>
    internal Options Options { get; }

    /// <summary>Where the report of an action goes.</summary>
    internal TextWriter Output => output;

    /// <summary>Where the notes and warnings go.</summary>
    internal TextWriter Error => error;

    /// <summary>True when more than one file was given, which puts the file name before every line.</summary>
    internal bool MultiFile => Options.Files.Count > 1;

    /// <summary>Prints a warning, unless <c>-q</c> or <c>-Q m</c> silenced them.</summary>
    /// <param name="message">The warning.</param>
    internal void Warn(string message)
    {
        if (Options.Quiet || Options.LogLevel == 'm') return;
        error.WriteLine("metatool: " + message);
    }

    /// <summary>Prints a progress note when <c>-v</c> asked for one.</summary>
    /// <param name="message">The note.</param>
    internal void Note(string message)
    {
        if (Options.Verbose) output.WriteLine(message);
    }

    /// <summary>Reads a file, or standard input when the name is <c>-</c>.</summary>
    /// <param name="path">The path, or <c>-</c>.</param>
    /// <returns>The bytes of the file.</returns>
    internal byte[] ReadFile(string path)
    {
        if (path != "-") return File.ReadAllBytes(path);
        using var buffer = new MemoryStream();
        standardInput.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Reads a text file, or standard input when the name is <c>-</c>.</summary>
    /// <param name="path">The path, or <c>-</c>.</param>
    /// <returns>The text of the file.</returns>
    internal string ReadText(string path)
    {
        if (path != "-") return File.ReadAllText(path);
        inputReader ??= new StreamReader(standardInput, Encoding.UTF8, true, 4096, leaveOpen: true);
        return inputReader.ReadToEnd();
    }

    /// <summary>Writes bytes to standard output, after flushing the text that came before them.</summary>
    /// <param name="bytes">The bytes.</param>
    internal void WriteStandardOutput(ReadOnlySpan<byte> bytes)
    {
        output.Flush();
        standardOutput.Write(bytes);
        standardOutput.Flush();
    }

    /// <summary>Runs one action for every file, keeping the first code that is not <see cref="ExitCodes.Ok"/>.</summary>
    /// <param name="action">What to do with one file.</param>
    /// <returns>The exit code of the run.</returns>
    internal int ForEachFile(Func<string, int> action)
    {
        int code = ExitCodes.Ok;
        foreach (string path in Options.Files)
        {
            int result;
            try
            {
                result = action(path);
            }
            catch (FileNotFoundException)
            {
                Warn($"{path}: failed to open the file");
                result = ExitCodes.Failed;
            }
            catch (DirectoryNotFoundException)
            {
                Warn($"{path}: failed to open the file");
                result = ExitCodes.Failed;
            }
            catch (UnauthorizedAccessException exception)
            {
                Warn($"{path}: {exception.Message}");
                result = ExitCodes.Failed;
            }
            catch (MetadataException exception)
            {
                Warn($"{path}: {exception.Message}");
                result = ExitCodes.Failed;
            }
            catch (IOException exception)
            {
                Warn($"{path}: {exception.Message}");
                result = ExitCodes.Failed;
            }
            catch (NotSupportedException exception)
            {
                Warn($"{path}: {exception.Message}");
                result = ExitCodes.Refused;
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or InvalidOperationException)
            {
                Warn($"{path}: {exception.Message}");
                result = ExitCodes.Failed;
            }
            if (code == ExitCodes.Ok) code = result;
        }
        return code;
    }

    /// <summary>Writes a file, keeping its timestamps when <c>-k</c> asked for that.</summary>
    /// <param name="path">The path to write, or <c>-</c> for standard output.</param>
    /// <param name="bytes">The bytes to write.</param>
    internal void WriteFile(string path, ReadOnlySpan<byte> bytes)
    {
        if (path == "-") { WriteStandardOutput(bytes); return; }
        DateTime? written = null, accessed = null;
        if (Options.PreserveTimestamps && File.Exists(path))
        {
            var info = new FileInfo(path);
            written = info.LastWriteTimeUtc;
            accessed = info.LastAccessTimeUtc;
        }
        SampleSupport.WriteAtomic(path, bytes);
        if (written is not null && accessed is not null) RestoreTimestamps(path, written.Value, accessed.Value);
    }

    private static void RestoreTimestamps(string path, DateTime written, DateTime accessed)
    {
        File.SetLastWriteTimeUtc(path, written);
        File.SetLastAccessTimeUtc(path, accessed);
    }
}

/// <summary>The entry point of the command line: reads the arguments, dispatches the action, returns the exit code.</summary>
internal static class Runner
{
    /// <summary>The program name every note begins with.</summary>
    internal const string Name = "metatool";

    /// <summary>Runs one command line.</summary>
    /// <param name="args">The arguments after the program name.</param>
    /// <param name="output">Where the report goes.</param>
    /// <param name="error">Where the warnings go.</param>
    /// <param name="standardInput">Where <c>-</c> reads from.</param>
    /// <param name="standardOutput">Where raw payloads go.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(string[] args, TextWriter output, TextWriter error, Stream standardInput, Stream standardOutput)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ParseOutcome parsed = CommandLine.Parse(args);
        Options options = parsed.Options;
        if (options.ShowHelp || args.Length == 0)
        {
            Usage.Write(output);
            return parsed.Error is null || args.Length == 0 ? ExitCodes.Ok : ExitCodes.Usage;
        }
        if (options.ShowVersion)
        {
            output.WriteLine($"{Name} {typeof(Runner).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"} (MetadataSharp sample, Exiv2 0.28.9 command line)");
            return ExitCodes.Ok;
        }
        if (parsed.Error is not null)
        {
            error.WriteLine($"{Name}: {parsed.Error}");
            error.WriteLine($"{Name}: try '{Name} -h' for the list of actions and options");
            return ExitCodes.Usage;
        }
        foreach (string warning in parsed.Warnings)
            if (!options.Quiet) error.WriteLine($"{Name}: {warning}");

        // A run that named no print mode prints the summary, as the reference does.
        if (options.Action == ToolAction.Print && !options.ModeSet) options.Mode = PrintMode.Summary;
        var session = new Session(options, output, error, standardInput, standardOutput);
        return options.Action switch
        {
            ToolAction.Print => PrintAction.Run(session),
            ToolAction.Extract => ExtractAction.Run(session),
            ToolAction.Insert => InsertAction.Run(session),
            ToolAction.Erase => DeleteAction.Run(session),
            ToolAction.Modify => ModifyAction.Run(session),
            ToolAction.Adjust => AdjustAction.Run(session),
            ToolAction.Rename => RenameAction.Run(session),
            ToolAction.FixIso => FixAction.RunIso(session),
            ToolAction.FixCom => FixAction.RunComment(session),
            _ => ExitCodes.Usage,
        };
    }
}

/// <summary>The usage text and the version line.</summary>
internal static class Usage
{
    /// <summary>Writes the usage text: every action, every option, and one example of each action.</summary>
    /// <param name="output">Where the text goes.</param>
    internal static void Write(TextWriter output)
    {
        foreach (string line in Lines) output.WriteLine(line);
    }

    private static readonly string[] Lines =
    [
        "Usage: metatool [options] [action] file ...",
        "",
        "Actions (short or long name):",
        "  pr | print      Print metadata; what is printed is chosen by -p and -P",
        "  ex | extract    Extract metadata, thumbnails, previews, ICC profiles or sidecars",
        "  in | insert     Insert metadata, thumbnails, previews, ICC profiles or sidecars",
        "  rm | delete     Delete metadata from the file",
        "  ad | adjust     Adjust the EXIF timestamps of the file",
        "  mo | modify     Apply modify commands (-M, -m) and set the comment (-c)",
        "  mv | rename     Rename the file after its EXIF timestamp (-r format)",
        "  fi | fixiso     Set the ISO speed from the MakerNote",
        "  fc | fixcom     Rewrite the EXIF user comment in the charset of -n",
        "",
        "Print modes (-p):",
        "  s  summary of the file and the common camera settings",
        "  a  every EXIF tag, IPTC dataset and XMP property",
        "  e  EXIF tags with their values          t  EXIF tags with interpreted values",
        "  v  EXIF tags with group, name, type, count and value",
        "  h  EXIF tags with a hexadecimal dump of every value",
        "  i  IPTC datasets                        x  XMP properties",
        "  c  the free-text comment                p  the preview listing",
        "  C  the ICC profile bytes                X  the XMP packet bytes",
        "",
        "Print columns (-P): E I X families, x tag, g group, k key, l label, d description,",
        "  n name, y type, c count, s size, v value, V modify command, t interpreted value, h hex dump",
        "",
        "Options:",
        "  -d target   delete target: a, e, t, i, x, c, C, I, -",
        "  -e target   extract target: a, e, t, i, x, c, C, I, X, XX, p[n,...], -",
        "  -i target   insert target: a, e, t, i, x, c, C, X, XX, -",
        "  -K key      print only rows whose key is exactly this one; repeatable",
        "  -g pattern  print only rows whose key matches this regular expression; repeatable",
        "  -M command  apply a modify command; repeatable",
        "  -m file     apply the modify commands of a file, or of standard input for -; repeatable",
        "  -c text     set the free-text comment as part of modify",
        "  -r format   name format of rename: strftime conversions and :basename:, :dirname:, :Exif.Key:",
        "  -n charset  charset of the EXIF user comment, utf-8 by default",
        "  -l dir      directory of the extracted or inserted payloads",
        "  -S suffix   suffix of the payload files of insert",
        "  -a time     adjust the timestamps by [-]HH[:MM[:SS]]",
        "  -Y years    adjust the timestamps by whole years",
        "  -O months   adjust the timestamps by whole months",
        "  -D days     adjust the timestamps by whole days",
        "  -k          keep the file timestamps of the files that are updated",
        "  -t          set the file timestamp from the EXIF timestamp in rename",
        "  -T          set the EXIF timestamp on the file and do nothing else",
        "  -f          overwrite files that exist, without asking",
        "  -F          add a number to a name that exists instead of asking",
        "  -u          hide tags that have no name in the tag table",
        "  -q          silence warnings",
        "  -Q level    log level: d, i, w, e or m (mute)",
        "  -v          say what is being done",
        "  -V          print the version and exit",
        "  -h          print this text and exit",
        "  --          read every later word as a file name, whatever it begins with",
        "",
        "Exit codes: 0 done, 1 an action failed, 2 the command line cannot be read,",
        "  3 the metadata the action needs is not there, 4 the action is knowingly not offered.",
        "",
        "Examples:",
        "  metatool -p a photo.jpg                      every tag of a file",
        "  metatool -P Exgnycv -g DateTime photo.jpg    the columns of -p v, filtered",
        "  metatool -M 'set Exif.Image.Artist \"Ada\"' photo.jpg",
        "  metatool -e t photo.jpg                      write photo-thumb.jpg",
        "  metatool -i x photo.jpg                      insert photo.xmp into photo.jpg",
        "  metatool -d a copy.jpg                       delete every deletable family",
        "  metatool -r ':basename:%Y%m%d_%H%M%S' -t photo.jpg",
        "  metatool -Y -1 -O 2 ad photo.jpg             move the timestamps back a year and on two months",
        "  metatool -e - -p X photo.jpg | metatool -i XX- target.jpg",
    ];
}
