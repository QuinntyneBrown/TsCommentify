using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace TsCommentify.Cli.Services;

public class CommentGenerator : ICommentGenerator
{
    private readonly ILogger<CommentGenerator> _logger;

    public CommentGenerator(ILogger<CommentGenerator> logger)
    {
        _logger = logger;
    }

    public string GenerateComment(FunctionInfo function)
    {
        _logger.LogDebug("Generating comment for function: {FunctionName}", function.Name);

        var comment = new StringBuilder();
        comment.AppendLine("/**");

        // Function/declaration description.
        comment.AppendLine($" * {GenerateDescription(function)}");

        // Parameter documentation.
        if (function.Parameters.Any())
        {
            comment.AppendLine(" *");
            foreach (var param in function.Parameters)
            {
                var paramType = param.Type ?? "any";
                comment.AppendLine($" * @param {{{paramType}}} {param.Name} - {GenerateParameterDescription(param)}");
            }
        }

        // Return documentation — only when the declaration actually returns a value.
        // A null/empty/void/never/undefined/inferred return type yields no @returns
        // tag (documenting "returns nothing" is noise, and the AST reports null when
        // there is no annotation).
        var returnDescription = GenerateReturnDescription(function);
        if (returnDescription != null)
        {
            comment.AppendLine(" *");
            comment.AppendLine($" * @returns {{{function.ReturnType}}} {returnDescription}");
        }

        comment.Append(" */");

        return comment.ToString();
    }

    // ---- Descriptions ------------------------------------------------------

    // Common leading verbs mapped to their third-person-singular form, so a name
    // like "calculateTotal" reads as "Calculates the total." rather than the bare
    // "Calculate Total." This is deterministic (no inference of behaviour from the
    // body) but produces accurate, natural prose for the overwhelmingly common
    // verb-first naming convention.
    private static readonly Dictionary<string, string> ActionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["get"] = "Gets", ["set"] = "Sets", ["create"] = "Creates", ["build"] = "Builds",
        ["make"] = "Makes", ["update"] = "Updates", ["delete"] = "Deletes", ["remove"] = "Removes",
        ["fetch"] = "Fetches", ["load"] = "Loads", ["save"] = "Saves", ["send"] = "Sends",
        ["find"] = "Finds", ["read"] = "Reads", ["write"] = "Writes", ["retrieve"] = "Retrieves",
        ["calculate"] = "Calculates", ["compute"] = "Computes", ["validate"] = "Validates",
        ["parse"] = "Parses", ["handle"] = "Handles", ["process"] = "Processes", ["log"] = "Logs",
        ["convert"] = "Converts", ["map"] = "Maps", ["render"] = "Renders", ["transform"] = "Transforms",
        ["register"] = "Registers", ["initialize"] = "Initializes", ["init"] = "Initializes",
        ["reset"] = "Resets", ["toggle"] = "Toggles", ["add"] = "Adds", ["apply"] = "Applies",
        ["ensure"] = "Ensures", ["clear"] = "Clears", ["close"] = "Closes", ["open"] = "Opens",
        ["start"] = "Starts", ["stop"] = "Stops", ["run"] = "Runs", ["execute"] = "Executes",
        ["sort"] = "Sorts", ["filter"] = "Filters", ["format"] = "Formats", ["emit"] = "Emits",
        ["list"] = "Lists", ["count"] = "Counts", ["check"] = "Checks", ["resolve"] = "Resolves",
    };

    // Leading verbs that phrase a yes/no question; used for both the description
    // ("Determines whether ...") and the boolean @returns text.
    private static readonly HashSet<string> QuestionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "is", "are", "was", "were", "has", "have", "had", "can", "could", "should",
        "will", "would", "does", "do", "did", "exists", "contains", "includes",
        "matches", "supports", "needs", "allows",
    };

    private string GenerateDescription(FunctionInfo function)
    {
        var words = SplitWords(function.Name);
        if (words.Count == 0)
        {
            return $"{ConvertToReadable(function.Name)}.";
        }

        var first = words[0];
        var rest = LowerJoin(words.Skip(1));

        if (ActionVerbs.TryGetValue(first, out var conjugated))
        {
            return rest.Length == 0 ? $"{conjugated}." : $"{conjugated} the {rest}.";
        }

        if (QuestionVerbs.Contains(first) && rest.Length > 0)
        {
            return $"Determines whether {rest}.";
        }

        return $"{ConvertToReadable(function.Name)}.";
    }

    private string GenerateParameterDescription(ParameterInfo parameter)
    {
        var words = SplitWords(parameter.Name);
        var readable = words.Count > 0 ? LowerJoin(words) : parameter.Name.ToLowerInvariant();
        return $"The {readable}.";
    }

    // Returns null to signal that no @returns tag should be emitted.
    private string? GenerateReturnDescription(FunctionInfo function)
    {
        var returnType = function.ReturnType?.Trim();

        if (string.IsNullOrEmpty(returnType) ||
            returnType is "void" or "undefined" or "never" or "inferred")
        {
            return null;
        }

        if (returnType is "boolean" or "bool")
        {
            return BooleanReturnDescription(function);
        }

        // Promise / Promise<T>
        if (returnType == "Promise")
        {
            return "A promise that resolves when the operation completes.";
        }
        var promise = Regex.Match(returnType, @"^Promise\s*<\s*(.+)\s*>$", RegexOptions.Singleline);
        if (promise.Success)
        {
            var inner = promise.Groups[1].Value.Trim();
            if (inner is "void" or "undefined" or "unknown" or "never")
            {
                return "A promise that resolves when the operation completes.";
            }
            return IsSimpleTypeName(inner)
                ? $"A promise that resolves to the {LowerType(inner)}."
                : "A promise that resolves with the result of the operation.";
        }

        // Arrays: T[] or Array<T>
        var array = Regex.Match(returnType, @"^(.+)\[\]$") is { Success: true } a
            ? a.Groups[1].Value.Trim()
            : Regex.Match(returnType, @"^Array\s*<\s*(.+)\s*>$") is { Success: true } b
                ? b.Groups[1].Value.Trim()
                : null;
        if (array != null && IsSimpleTypeName(array))
        {
            return $"An array of {LowerType(array)} values.";
        }

        if (IsSimpleTypeName(returnType))
        {
            return $"The {LowerType(returnType)}.";
        }

        return "The result of the operation.";
    }

    private string BooleanReturnDescription(FunctionInfo function)
    {
        var words = SplitWords(function.Name);
        if (words.Count > 1 && QuestionVerbs.Contains(words[0]))
        {
            return $"True if {LowerJoin(words.Skip(1))}; otherwise false.";
        }
        return "True on success; otherwise false.";
    }

    // ---- Word helpers ------------------------------------------------------

    // Splits an identifier into display words, reusing ConvertToReadable so
    // camelCase, PascalCase, acronyms (HTML), and snake/kebab separators are all
    // handled consistently.
    private List<string> SplitWords(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return new List<string>();
        }
        var normalized = identifier.Replace('_', ' ').Replace('-', ' ');
        return ConvertToReadable(normalized)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    // Joins words lowercased, but preserves all-caps acronyms (e.g. "HTML").
    private static string LowerJoin(IEnumerable<string> words)
        => string.Join(" ", words.Select(LowerWord));

    private static string LowerWord(string word)
        => word.Length > 1 && word.All(char.IsUpper) ? word : word.ToLowerInvariant();

    private static bool IsSimpleTypeName(string type)
        => Regex.IsMatch(type, @"^[A-Za-z_][A-Za-z0-9_]*$");

    private string LowerType(string type)
        => LowerJoin(SplitWords(type));

    private string ConvertToReadable(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return identifier;

        // Insert space before capital letters (for camelCase and PascalCase)
        var result = new StringBuilder();

        for (int i = 0; i < identifier.Length; i++)
        {
            var ch = identifier[i];

            // Add space before uppercase letter if:
            // - It's not the first character
            // - Previous character is lowercase
            // - OR previous character is uppercase but next is lowercase (for acronyms)
            if (i > 0 && char.IsUpper(ch))
            {
                var prev = identifier[i - 1];
                var hasNext = i < identifier.Length - 1;
                var next = hasNext ? identifier[i + 1] : '\0';

                if (char.IsLower(prev) || (char.IsUpper(prev) && hasNext && char.IsLower(next)))
                {
                    result.Append(' ');
                }
            }

            result.Append(ch);
        }

        // Capitalize first letter
        var readable = result.ToString();
        if (readable.Length > 0)
        {
            readable = char.ToUpper(readable[0]) + readable.Substring(1);
        }

        return readable;
    }
}
