using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

return await Inventory.Run(args);

internal static class Inventory
{
    private static readonly HashSet<string> InvariantTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "offset", "origin", "pointer", "budget", "limit", "relocate", "overlap", "checksum", "crc",
        "depth", "range", "segment", "box", "endian", "byteorder", "transaction", "verify", "preserve"
    };

    internal static async Task<int> Run(string[] args)
    {
        string root = "src";
        string? reportPath = null;
        bool failPublic = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--report" when i + 1 < args.Length: reportPath = args[++i]; break;
                case "--check-public": failPublic = true; break;
                case "--help":
                    Console.WriteLine("Usage: MetadataSharp.DocInventory [--root source-directory] [--report json-file] [--check-public]");
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete option: {args[i]}");
                    return 2;
            }
        }

        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"Source directory does not exist: {root}");
            return 2;
        }

        string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part =>
                part is "bin" or "obj" or "Generated"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse);
        SyntaxTree[] trees = files.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path)).ToArray();
        MetadataReference[] references = PlatformReferences();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "MetadataSharp.DocumentationInventory",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var entries = new List<DeclarationRecord>();
        foreach (SyntaxTree tree in trees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            Collect(tree.GetRoot(), model, root, entries);
        }

        entries.Sort((left, right) =>
        {
            int compare = string.CompareOrdinal(left.Path, right.Path);
            if (compare != 0) return compare;
            compare = left.Line.CompareTo(right.Line);
            return compare != 0 ? compare : string.CompareOrdinal(left.Name, right.Name);
        });

        var result = new InventoryReport
        {
            Root = Path.GetRelativePath(Environment.CurrentDirectory, root),
            SourceFiles = files.Length,
            Declarations = entries,
            Summary = Summarize(entries)
        };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        if (reportPath is null)
            Console.WriteLine(json);
        else
        {
            string target = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, json);
            Console.WriteLine($"Documentation inventory: {target}");
        }

        Console.WriteLine($"Files: {result.SourceFiles}; declarations: {result.Summary.Total}; public: {result.Summary.PublicDeclarations}; public gaps: {result.Summary.PublicGaps}; non-trivial internal/private gaps: {result.Summary.NonPublicPurposeGaps}.");
        if (!failPublic) return 0;
        if (result.Summary.PublicGaps == 0) return 0;

        foreach (DeclarationRecord item in entries.Where(entry => entry.Public && entry.PublicGaps.Count != 0))
            Console.Error.WriteLine($"{item.Path}({item.Line}): {item.Name}: missing {string.Join(", ", item.PublicGaps)}");
        return 1;
    }

    private static MetadataReference[] PlatformReferences()
    {
        string? trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trusted)) throw new InvalidOperationException("The runtime did not provide its platform reference assemblies.");
        return trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static void Collect(SyntaxNode root, SemanticModel model, string sourceRoot, List<DeclarationRecord> records)
    {
        foreach (SyntaxNode node in root.DescendantNodesAndSelf())
        {
            if (node is FieldDeclarationSyntax field)
            {
                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    if (model.GetDeclaredSymbol(variable) is IFieldSymbol symbol)
                        Add(field, symbol, model, sourceRoot, records, variable.Identifier.ValueText, "field", trivial: false);
                }
                continue;
            }

            if (node is EventFieldDeclarationSyntax eventField)
            {
                foreach (VariableDeclaratorSyntax variable in eventField.Declaration.Variables)
                {
                    if (model.GetDeclaredSymbol(variable) is IEventSymbol symbol)
                        Add(eventField, symbol, model, sourceRoot, records, variable.Identifier.ValueText, "event field", trivial: false);
                }
                continue;
            }

            switch (node)
            {
                case BaseTypeDeclarationSyntax type when model.GetDeclaredSymbol(type) is INamedTypeSymbol symbol:
                    Add(type, symbol, model, sourceRoot, records, type.Identifier.ValueText, "type", trivial: false);
                    break;
                case DelegateDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, declaration.Identifier.ValueText, "delegate", trivial: false);
                    break;
                case MethodDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IMethodSymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, Signature(symbol), "method", trivial: false);
                    break;
                case ConstructorDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IMethodSymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, Signature(symbol), "constructor", trivial: false);
                    break;
                case DestructorDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IMethodSymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, Signature(symbol), "destructor", trivial: false);
                    break;
                case PropertyDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IPropertySymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, declaration.Identifier.ValueText, "property", trivial: false);
                    break;
                case IndexerDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IPropertySymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, "this[]", "indexer", trivial: false);
                    break;
                case EventDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IEventSymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, declaration.Identifier.ValueText, "event", trivial: false);
                    break;
                case EnumMemberDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IFieldSymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, declaration.Identifier.ValueText, "enum member", trivial: false);
                    break;
                case OperatorDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IMethodSymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, declaration.OperatorToken.Text, "operator", trivial: false);
                    break;
                case ConversionOperatorDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration) is IMethodSymbol symbol:
                    Add(declaration, symbol, model, sourceRoot, records, declaration.ImplicitOrExplicitKeyword.Text + " operator", "conversion", trivial: false);
                    break;
            }
        }
    }

    private static void Add(SyntaxNode node, ISymbol symbol, SemanticModel model, string sourceRoot,
        List<DeclarationRecord> records, string name, string kind, bool trivial)
    {
        string xml = symbol.GetDocumentationCommentXml(expandIncludes: false, cancellationToken: default) ?? string.Empty;
        XElement doc = ParseDocumentation(xml);
        IMethodSymbol? callable = symbol as IMethodSymbol ?? (symbol as INamedTypeSymbol)?.DelegateInvokeMethod;
        string[] parameterNames = callable is not null
            ? callable.Parameters.Select(parameter => parameter.Name).ToArray()
            : node is RecordDeclarationSyntax record && record.ParameterList is not null
                ? record.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText).ToArray()
                : [];
        string[] documentedParams = doc.Descendants("param").Select(element => (string?)element.Attribute("name"))
            .Where(value => value is not null).Cast<string>().ToArray();
        string[] missingParams = parameterNames.Except(documentedParams, StringComparer.Ordinal).ToArray();
        string[] extraParams = documentedParams.Except(parameterNames, StringComparer.Ordinal).ToArray();
        string[] typeParameterNames = symbol switch
        {
            INamedTypeSymbol named => named.TypeParameters.Select(parameter => parameter.Name).ToArray(),
            IMethodSymbol genericMethod => genericMethod.TypeParameters.Select(parameter => parameter.Name).ToArray(),
            _ => []
        };
        string[] documentedTypeParams = doc.Descendants("typeparam").Select(element => (string?)element.Attribute("name"))
            .Where(value => value is not null).Cast<string>().ToArray();
        string[] missingTypeParams = typeParameterNames.Except(documentedTypeParams, StringComparer.Ordinal).ToArray();
        bool isMethod = symbol is IMethodSymbol { MethodKind: not MethodKind.PropertyGet and not MethodKind.PropertySet };
        bool hasReturn = callable is not null && !callable.ReturnsVoid && callable.MethodKind is not MethodKind.Constructor and not MethodKind.StaticConstructor;
        bool requiresException = isMethod && HasDirectThrow(node);
        int summaryCount = doc.Descendants("summary").Count();
        int returnCount = doc.Descendants("returns").Count();
        int exceptionCount = doc.Descendants("exception").Count();
        bool inheritsAll = doc.Descendants("inheritdoc").Any(element => string.IsNullOrWhiteSpace((string?)element.Attribute("path")));
        bool inheritsExceptions = doc.Descendants("inheritdoc").Any(element =>
            ((string?)element.Attribute("path"))?.Contains("exception", StringComparison.OrdinalIgnoreCase) == true);
        bool summaryPresent = summaryCount != 0 || inheritsAll;
        bool returnsPresent = returnCount != 0 || inheritsAll;
        bool exceptionsPresent = exceptionCount != 0 || inheritsAll || inheritsExceptions;
        int lineCount = node.GetLocation().GetLineSpan().EndLinePosition.Line - node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        bool nonTrivial = !trivial && (lineCount > 5 || HasInvariantSignal(node));
        bool xmlSummary = summaryCount != 0 || doc.Elements("inheritdoc").Any();
        bool purposeComment = xmlSummary || HasPurposeLineComment(node);
        string access = symbol.DeclaredAccessibility.ToString();
        bool publicApi = IsExternallyVisible(symbol);
        var gaps = new List<string>();

        if (publicApi)
        {
            if (!summaryPresent) gaps.Add("summary");
            if (missingParams.Length != 0) gaps.Add("param " + string.Join(", ", missingParams));
            if (extraParams.Length != 0) gaps.Add("unknown param " + string.Join(", ", extraParams));
            if (missingTypeParams.Length != 0) gaps.Add("typeparam " + string.Join(", ", missingTypeParams));
            if (hasReturn && !returnsPresent) gaps.Add("returns");
            if (requiresException && !exceptionsPresent) gaps.Add("exception");
        }

        string path = Path.GetRelativePath(sourceRoot, node.SyntaxTree.FilePath).Replace(Path.DirectorySeparatorChar, '/');
        records.Add(new DeclarationRecord
        {
            Path = path,
            Line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            Kind = kind,
            Name = name,
            Accessibility = access,
            Public = publicApi,
            NonTrivial = nonTrivial,
            PurposeComment = purposeComment,
            Summary = summaryPresent,
            Parameters = params,
            DocumentedParameters = documentedParams,
            MissingParameters = missingParams,
            MissingTypeParameters = missingTypeParams,
            Returns = returnsPresent,
            ExceptionCount = exceptionCount,
            ExceptionRequiredByBody = requiresException,
            PublicGaps = gaps
        });
    }

    private static XElement ParseDocumentation(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return new XElement("doc");
        try
        {
            XElement parsed = XElement.Parse("<doc>" + xml + "</doc>", LoadOptions.None);
            return parsed;
        }
        catch
        {
            return new XElement("doc");
        }
    }

    private static bool IsExternallyVisible(ISymbol symbol)
    {
        for (ISymbol? current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamespaceSymbol or IAssemblySymbol or IModuleSymbol) continue;
            if (current is INamedTypeSymbol && current.ContainingType is null)
            {
                if (current.DeclaredAccessibility != Accessibility.Public) return false;
                continue;
            }
            if (current is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor && ctor.ContainingType.TypeKind == TypeKind.Interface)
                continue;
            if (current.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Protected and not Accessibility.ProtectedOrInternal)
                return false;
        }
        return true;
    }

    private static bool HasDirectThrow(SyntaxNode node) => node.DescendantNodes(descendIntoChildren: child =>
            child is not LocalFunctionStatementSyntax && child is not AnonymousFunctionExpressionSyntax)
        .Any(child => child is ThrowStatementSyntax or ThrowExpressionSyntax ||
            child is InvocationExpressionSyntax invocation && invocation.Expression.ToString().Contains("ThrowIf", StringComparison.Ordinal));

    private static bool HasInvariantSignal(SyntaxNode node)
    {
        foreach (SyntaxToken token in node.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) && InvariantTerms.Contains(token.ValueText)) return true;
        }
        return node.DescendantNodes().Any(child => child is ThrowStatementSyntax or ThrowExpressionSyntax);
    }

    private static bool HasPurposeLineComment(SyntaxNode node)
    {
        foreach (SyntaxTrivia trivia in node.GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) continue;
            string text = trivia.ToString().Trim('/', '*', ' ', '\t', '\r', '\n', '-');
            if (text.Length >= 18 && text.Count(char.IsLetter) >= 12) return true;
        }
        return false;
    }

    private static string Signature(IMethodSymbol symbol) =>
        symbol.Name + "(" + string.Join(", ", symbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) + ")";

    private static InventorySummary Summarize(List<DeclarationRecord> entries) => new()
    {
        Total = entries.Count,
        PublicDeclarations = entries.Count(entry => entry.Public),
        PublicGaps = entries.Sum(entry => entry.PublicGaps.Count),
        PublicSummaryGaps = entries.Count(entry => entry.Public && !entry.Summary),
        PublicParameterGaps = entries.Sum(entry => entry.Public ? entry.MissingParameters.Length : 0),
        PublicTypeParameterGaps = entries.Sum(entry => entry.Public ? entry.MissingTypeParameters.Length : 0),
        PublicReturnGaps = entries.Count(entry => entry.Public && (entry.Kind is "method" or "operator" or "conversion" or "delegate") && !entry.Returns),
        PublicExceptionGaps = entries.Count(entry => entry.Public && entry.ExceptionRequiredByBody && entry.ExceptionCount == 0),
        NonTrivialInternalPrivate = entries.Count(entry => !entry.Public && entry.NonTrivial),
        NonPublicPurposeGaps = entries.Count(entry => !entry.Public && entry.NonTrivial && !entry.PurposeComment)
    };

    private sealed class InventoryReport
    {
        public required string Root { get; init; }
        public required int SourceFiles { get; init; }
        public required List<DeclarationRecord> Declarations { get; init; }
        public required InventorySummary Summary { get; init; }
    }

    private sealed class InventorySummary
    {
        public int Total { get; init; }
        public int PublicDeclarations { get; init; }
        public int PublicGaps { get; init; }
        public int PublicSummaryGaps { get; init; }
        public int PublicParameterGaps { get; init; }
        public int PublicTypeParameterGaps { get; init; }
        public int PublicReturnGaps { get; init; }
        public int PublicExceptionGaps { get; init; }
        public int NonTrivialInternalPrivate { get; init; }
        public int NonPublicPurposeGaps { get; init; }
    }

    private sealed class DeclarationRecord
    {
        public required string Path { get; init; }
        public required int Line { get; init; }
        public required string Kind { get; init; }
        public required string Name { get; init; }
        public required string Accessibility { get; init; }
        public required bool Public { get; init; }
        public required bool NonTrivial { get; init; }
        public required bool PurposeComment { get; init; }
        public required bool Summary { get; init; }
        public required string[] Parameters { get; init; }
        public required string[] DocumentedParameters { get; init; }
        public required string[] MissingParameters { get; init; }
        public required string[] MissingTypeParameters { get; init; }
        public required bool Returns { get; init; }
        public required int ExceptionCount { get; init; }
        public required bool ExceptionRequiredByBody { get; init; }
        public required List<string> PublicGaps { get; init; }
    }
}
