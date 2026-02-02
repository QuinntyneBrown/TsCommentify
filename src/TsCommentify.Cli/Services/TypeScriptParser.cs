using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace TsCommentify.Cli.Services;

public class TypeScriptParser : ITypeScriptParser
{
    private readonly ILogger<TypeScriptParser> _logger;

    public TypeScriptParser(ILogger<TypeScriptParser> logger)
    {
        _logger = logger;
    }

    public IEnumerable<FunctionInfo> ParseFunctions(string filePath)
    {
        _logger.LogInformation("Parsing TypeScript file: {FilePath}", filePath);
        
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return Enumerable.Empty<FunctionInfo>();
        }

        var lines = File.ReadAllLines(filePath);
        var functions = new List<FunctionInfo>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            
            // Skip empty lines and single-line comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;

            // Check for function declarations
            if (IsFunctionDeclaration(line))
            {
                var hasComment = HasCommentAbove(lines, i);
                var functionInfo = ParseFunctionDeclaration(lines, i, hasComment);
                if (functionInfo != null)
                {
                    functions.Add(functionInfo);
                }
            }
        }

        _logger.LogInformation("Found {Count} functions in {FilePath}", functions.Count, filePath);
        return functions;
    }

    private bool IsFunctionDeclaration(string line)
    {
        // Match various function declaration patterns:
        // function name(...) 
        // async function name(...)
        // const/let/var name = function(...)
        // const/let/var name = (...) =>
        // export function name(...)
        // public/private/protected name(...) {
        
        var patterns = new[]
        {
            @"^\s*(export\s+)?(async\s+)?function\s+\w+\s*\(",
            @"^\s*(export\s+)?(const|let|var)\s+\w+\s*=\s*(async\s+)?function\s*\(",
            @"^\s*(export\s+)?(const|let|var)\s+\w+\s*=\s*(async\s+)?\(.*?\)\s*:\s*\w+\s*=>",
            @"^\s*(export\s+)?(const|let|var)\s+\w+\s*=\s*(async\s+)?\(.*?\)\s*=>",
            @"^\s*(public|private|protected|static)?\s*\w+\s*\([^)]*\)\s*:\s*\w+\s*\{",
            @"^\s*(public|private|protected|static)?\s*\w+\s*\([^)]*\)\s*\{"
        };

        return patterns.Any(pattern => Regex.IsMatch(line, pattern));
    }

    private bool HasCommentAbove(string[] lines, int lineIndex)
    {
        if (lineIndex == 0) return false;

        // Check the line immediately above
        var previousLine = lines[lineIndex - 1].TrimStart();
        
        // Check for JSDoc style comment (/** ... */)
        if (previousLine.StartsWith("*/") || previousLine.Contains("*/"))
        {
            return true;
        }

        // Check for single-line comment starting with /// or //
        if (previousLine.StartsWith("///") || previousLine.StartsWith("//"))
        {
            return true;
        }

        // Look backwards for JSDoc comment block
        for (int i = lineIndex - 1; i >= 0; i--)
        {
            var line = lines[i].TrimStart();
            if (string.IsNullOrWhiteSpace(line))
                continue;
                
            if (line.StartsWith("/**") || line.Contains("/**"))
                return true;
                
            if (line.StartsWith("*/"))
                return true;
                
            // If we hit code, stop looking
            if (!line.StartsWith("*") && !line.StartsWith("//"))
                break;
        }

        return false;
    }

    private FunctionInfo? ParseFunctionDeclaration(string[] lines, int lineIndex, bool hasComment)
    {
        var line = lines[lineIndex];
        
        // Extract function name - try different patterns
        Match nameMatch;
        
        // First, try variable assignment pattern (covers arrow functions and function expressions)
        nameMatch = Regex.Match(line, @"(?:const|let|var)\s+(\w+)\s*=");
        
        if (!nameMatch.Success)
        {
            // Try regular function declaration
            nameMatch = Regex.Match(line, @"function\s+(\w+)\s*\(");
        }

        if (!nameMatch.Success)
        {
            return null;
        }

        var functionName = nameMatch.Groups[1].Value;

        // Extract parameters
        var parameters = ParseParameters(line);

        // Extract return type if specified
        var returnType = ParseReturnType(line);

        // Get the full function content (for now, just the declaration line)
        var content = line;

        return new FunctionInfo(
            Name: functionName,
            LineNumber: lineIndex + 1, // 1-based line numbering
            Content: content,
            Parameters: parameters,
            ReturnType: returnType,
            HasComment: hasComment
        );
    }

    private List<ParameterInfo> ParseParameters(string line)
    {
        var parameters = new List<ParameterInfo>();
        
        // Extract the parameter list
        var match = Regex.Match(line, @"\(([^)]*)\)");
        if (!match.Success)
            return parameters;

        var paramList = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(paramList))
            return parameters;

        // Split by comma, handling nested generics
        var paramStrings = SplitParameters(paramList);
        
        foreach (var paramStr in paramStrings)
        {
            var trimmed = paramStr.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Parse parameter with optional type annotation
            var paramMatch = Regex.Match(trimmed, @"^(\w+)(?:\s*:\s*(.+?))?(?:\s*=.*)?$");
            if (paramMatch.Success)
            {
                var paramName = paramMatch.Groups[1].Value;
                var paramType = paramMatch.Groups[2].Success ? paramMatch.Groups[2].Value.Trim() : null;
                parameters.Add(new ParameterInfo(paramName, paramType));
            }
        }

        return parameters;
    }

    private List<string> SplitParameters(string paramList)
    {
        var result = new List<string>();
        var current = "";
        var depth = 0;

        foreach (var ch in paramList)
        {
            if (ch == '<' || ch == '(' || ch == '{')
                depth++;
            else if (ch == '>' || ch == ')' || ch == '}')
                depth--;
            else if (ch == ',' && depth == 0)
            {
                result.Add(current);
                current = "";
                continue;
            }

            current += ch;
        }

        if (!string.IsNullOrWhiteSpace(current))
            result.Add(current);

        return result;
    }

    private string? ParseReturnType(string line)
    {
        // Match return type annotation ): type or => type
        var match = Regex.Match(line, @"\):\s*(\w+(?:<[^>]+>)?)\s*(?:\{|=>|;)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // Check for implicit return from arrow function
        if (line.Contains("=>") && !line.Contains("{"))
        {
            return "inferred";
        }

        return null;
    }
}
