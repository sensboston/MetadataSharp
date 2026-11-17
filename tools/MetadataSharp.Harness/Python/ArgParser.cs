using System.Globalization;
using System.Text;

namespace MetadataSharp.Harness.Python;

// A command-line error: argparse prints the usage and the message and exits with status 2.
internal sealed class ArgParseError(string message) : Exception(message);

// How an option takes its value: a value, a flag, a repeated value, or every following value.
internal enum ArgAction
{
    Store,
    StoreTrue,
    Append,
}

// The parsed values by destination name, with argparse's defaults filled in.
internal sealed class ParsedArgs
{
    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

    public object? this[string dest]
    {
        get => values.TryGetValue(dest, out object? value) ? value : null;
        set => values[dest] = value;
    }

    public string Str(string dest) => (string)values[dest]!;

    public string? StrOrNull(string dest) => (string?)this[dest];

    public bool Flag(string dest) => this[dest] is true;

    public long Int(string dest) => Convert.ToInt64(values[dest], CultureInfo.InvariantCulture);

    public long? IntOrNull(string dest) => this[dest] is null ? null : Int(dest);

    public List<string> List(string dest) => (List<string>?)this[dest] ?? [];
}

// The subset of argparse the gates declared: long options with values, flags, repeated options,
// choices, int values and positionals (one value, or nargs '?'/'*'/'+'), with argparse's messages.
internal sealed class ArgParser(string prog, string? description = null)
{
    private readonly List<Argument> options = [];
    private readonly List<Argument> positionals = [];

    /// <summary>add_argument for an option ("--name") or a positional ("name").</summary>
    public ArgParser Add(string name, ArgAction action = ArgAction.Store, bool required = false, object? @default = null,
                         IReadOnlyList<string>? choices = null, bool isInt = false, string? nargs = null, string? dest = null,
                         string? help = null, string? alias = null)
    {
        bool optional = name.StartsWith('-');
        var argument = new Argument(name, alias, dest ?? (optional ? name.TrimStart('-').Replace('-', '_') : name), action, required,
                                    @default, choices, isInt, nargs, help);
        (optional ? options : positionals).Add(argument);
        return this;
    }

    /// <summary>parse_args(argv): raises ArgParseError for anything argparse would reject.</summary>
    public ParsedArgs Parse(IReadOnlyList<string> argv)
    {
        var result = new ParsedArgs();
        foreach (var option in options)
        {
            result[option.Dest] = option.Action switch
            {
                ArgAction.StoreTrue => option.Default ?? false,
                ArgAction.Append => option.Default is List<string> list ? new List<string>(list) : new List<string>(),
                _ => option.Default,
            };
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var loose = new List<string>();
        var unknown = new List<string>();
        bool onlyPositionals = false;
        for (int i = 0; i < argv.Count; i++)
        {
            string token = argv[i];
            if (onlyPositionals || !LooksLikeOption(token))
            {
                loose.Add(token);
                continue;
            }
            if (token == "--")
            {
                onlyPositionals = true;
                continue;
            }
            if (token is "-h" or "--help")
            {
                Console.Out.Write(Help());
                throw new HelpRequested();
            }
            string name = token;
            string? inline = null;
            int equals = token.IndexOf('=', StringComparison.Ordinal);
            if (token.StartsWith("--", StringComparison.Ordinal) && equals > 0)
            {
                name = token[..equals];
                inline = token[(equals + 1)..];
            }
            Argument? option = Find(name);
            if (option is null)
            {
                unknown.Add(token);
                continue;
            }
            seen.Add(option.Dest);
            if (option.Action == ArgAction.StoreTrue)
            {
                if (inline is not null) throw Error($"argument {option.Display}: ignored explicit argument {Py.ReprString(inline)}");
                result[option.Dest] = true;
                continue;
            }
            if (option.Nargs is "*" or "+")
            {
                var values = new List<string>();
                if (inline is not null) values.Add(inline);
                while (i + 1 < argv.Count && !LooksLikeOption(argv[i + 1])) values.Add(argv[++i]);
                if (option.Nargs == "+" && values.Count == 0) throw Error($"argument {option.Display}: expected at least one argument");
                result[option.Dest] = values.Select(value => Convert(option, value)).ToList();
                continue;
            }
            string value;
            if (inline is not null) value = inline;
            else if (i + 1 < argv.Count && !LooksLikeOption(argv[i + 1])) value = argv[++i];
            else throw Error($"argument {option.Display}: expected one argument");
            object? converted = Convert(option, value);
            if (option.Action == ArgAction.Append) ((List<string>)result[option.Dest]!).Add((string)converted!);
            else result[option.Dest] = converted;
        }
        AssignPositionals(result, loose, unknown);
        var missing = options.Where(option => option.Required && !seen.Contains(option.Dest)).Select(option => option.Display).ToList();
        missing.AddRange(positionals.Where(positional => positional.Nargs is null or "+" && result[positional.Dest] is null).Select(positional => positional.Name));
        if (missing.Count > 0) throw Error("the following arguments are required: " + string.Join(", ", missing));
        if (unknown.Count > 0) throw Error("unrecognized arguments: " + string.Join(" ", unknown));
        return result;
    }

    /// <summary>Parse, or print argparse's usage and error and return the exit status it would use.</summary>
    public bool TryParse(IReadOnlyList<string> argv, out ParsedArgs parsed, out int exitCode)
    {
        try
        {
            parsed = Parse(argv);
            exitCode = 0;
            return true;
        }
        catch (ArgParseError error)
        {
            Console.Error.Write(Usage());
            Console.Error.WriteLine($"{prog}: error: {error.Message}");
            parsed = new ParsedArgs();
            exitCode = 2;
            return false;
        }
        catch (HelpRequested)
        {
            parsed = new ParsedArgs();
            exitCode = 0;
            return false;
        }
    }

    /// <summary>parser.error(message): the usage and the message on stderr; the status argparse exits with.</summary>
    public int Fail(string message)
    {
        Console.Error.Write(Usage());
        Console.Error.WriteLine($"{prog}: error: {message}");
        return 2;
    }

    private void AssignPositionals(ParsedArgs result, List<string> loose, List<string> unknown)
    {
        int at = 0;
        for (int index = 0; index < positionals.Count; index++)
        {
            var positional = positionals[index];
            int remainingRequired = positionals.Skip(index + 1).Count(p => p.Nargs is null or "+");
            switch (positional.Nargs)
            {
                case null:
                    result[positional.Dest] = at < loose.Count ? Convert(positional, loose[at++]) : null;
                    break;
                case "?":
                    result[positional.Dest] = loose.Count - at > remainingRequired ? Convert(positional, loose[at++]) : positional.Default;
                    break;
                default:
                    int take = Math.Max(0, loose.Count - at - remainingRequired);
                    var values = loose.Skip(at).Take(take).Select(value => (string)Convert(positional, value)!).ToList();
                    at += take;
                    result[positional.Dest] = values.Count == 0 && positional.Nargs == "+" ? null : values;
                    break;
            }
        }
        unknown.AddRange(loose.Skip(at));
    }

    private object? Convert(Argument argument, string value)
    {
        if (argument.Choices is not null && !argument.Choices.Contains(value))
        {
            throw Error($"argument {argument.Display}: invalid choice: {Py.ReprString(value)} (choose from {string.Join(", ", argument.Choices.Select(Py.ReprString))})");
        }
        if (!argument.IsInt) return value;
        if (PyText.TryParseInt(value, out long number)) return number;
        throw Error($"argument {argument.Display}: invalid int value: {Py.ReprString(value)}");
    }

    private Argument? Find(string name)
    {
        var exact = options.FirstOrDefault(option => option.Name == name || option.Alias == name);
        if (exact is not null || !name.StartsWith("--", StringComparison.Ordinal)) return exact;
        var prefixed = options.Where(option => option.Name.StartsWith(name, StringComparison.Ordinal)).ToList();
        if (prefixed.Count > 1)
            throw Error($"ambiguous option: {name} could match {string.Join(", ", prefixed.Select(option => option.Name))}");
        return prefixed.SingleOrDefault();
    }

    private static bool LooksLikeOption(string token) =>
        token.Length > 1 && token[0] == '-' && !(double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && !token.Contains(' '));

    private ArgParseError Error(string message) => new(message);

    private string Usage()
    {
        var builder = new StringBuilder("usage: ").Append(prog).Append(" [-h]");
        foreach (var option in options)
        {
            string part = option.Action == ArgAction.StoreTrue ? option.Name : option.Name + " " + option.Dest.ToUpperInvariant();
            builder.Append(' ').Append(option.Required ? part : "[" + part + "]");
        }
        foreach (var positional in positionals)
        {
            builder.Append(' ').Append(positional.Nargs switch
            {
                "?" => "[" + positional.Name + "]",
                "*" => "[" + positional.Name + " ...]",
                "+" => positional.Name + " [" + positional.Name + " ...]",
                _ => positional.Choices is null ? positional.Name : "{" + string.Join(",", positional.Choices) + "}",
            });
        }
        return builder.Append('\n').ToString();
    }

    private string Help()
    {
        var builder = new StringBuilder(Usage());
        if (description is not null) builder.Append('\n').Append(description).Append('\n');
        foreach (var argument in positionals.Concat(options))
        {
            builder.Append("  ").Append(argument.Name);
            if (argument.Help is not null) builder.Append("  ").Append(argument.Help);
            builder.Append('\n');
        }
        return builder.ToString();
    }

    // One declared argument.
    private sealed record Argument(string Name, string? Alias, string Dest, ArgAction Action, bool Required, object? Default,
                                   IReadOnlyList<string>? Choices, bool IsInt, string? Nargs, string? Help)
    {
        public string Display => Name.StartsWith('-') ? (Alias is null ? Name : Alias + "/" + Name) : Name;
    }

    // -h was given: the help was printed and the program ends with status 0.
    private sealed class HelpRequested : Exception;
}
