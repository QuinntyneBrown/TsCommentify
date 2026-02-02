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
        // [public/private/protected] [static] [async] name(...) {
        // get/set name(...) {

        // Skip control flow statements (if, switch, while, for, etc.)
        if (IsControlFlowStatement(line))
        {
            return false;
        }

        var patterns = new[]
        {
            @"^\s*(export\s+)?(async\s+)?function\s+\w+\s*\(",
            @"^\s*(export\s+)?(const|let|var)\s+\w+\s*=\s*(async\s+)?function\s*\(",
            @"^\s*(export\s+)?(const|let|var)\s+\w+\s*=\s*(async\s+)?\(.*?\)\s*:\s*\w+\s*=>",
            @"^\s*(export\s+)?(const|let|var)\s+\w+\s*=\s*(async\s+)?\(.*?\)\s*=>",
            // Class methods: [access] [static] [async] methodName
            // With return types (including generic types like Promise<any> and arrays like string[])
            @"^\s*(?:(?:public|private|protected)\s+)?(?:static\s+)?(?:async\s+)?\w+\s*\([^)]*\)\s*:\s*[\w<>,\s\[\]]+\s*\{",
            // Without return types
            @"^\s*(?:(?:public|private|protected)\s+)?(?:static\s+)?(?:async\s+)?\w+\s*\([^)]*\)\s*\{",
            // Getters and setters: [access] [static] get/set name
            @"^\s*(?:(?:public|private|protected)\s+)?(?:static\s+)?(?:get|set)\s+\w+\s*\([^)]*\)\s*:\s*[\w<>,\s\[\]]+\s*\{",
            @"^\s*(?:(?:public|private|protected)\s+)?(?:static\s+)?(?:get|set)\s+\w+\s*\([^)]*\)\s*\{"
        };

        return patterns.Any(pattern => Regex.IsMatch(line, pattern));
    }

    private bool IsControlFlowStatement(string line)
    {
        // Patterns for control flow statements that should not receive comments
        var controlFlowPatterns = new[]
        {
            @"^\s*if\s*\(",           // if statements
            @"^\s*else\s+if\s*\(",    // else if statements
            @"^\s*else\s*\{",         // else blocks
            @"^\s*switch\s*\(",       // switch statements
            @"^\s*case\s+",           // case labels
            @"^\s*default\s*:",       // default label
            @"^\s*while\s*\(",        // while loops
            @"^\s*for\s*\(",          // for loops
            @"^\s*do\s*\{",           // do-while loops
            @"^\s*try\s*\{",          // try blocks
            @"^\s*catch\s*\(",        // catch blocks
            @"^\s*finally\s*\{",      // finally blocks
        };

        return controlFlowPatterns.Any(pattern => Regex.IsMatch(line, pattern));
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
            // Try class method pattern: [access] [static] [async] methodName
            nameMatch = Regex.Match(line, @"^\s*(?:(?:public|private|protected)\s+)?(?:static\s+)?(?:async\s+)?(\w+)\s*\(");
        }

        if (!nameMatch.Success)
        {
            // Try getter/setter pattern: [access] [static] get/set name
            nameMatch = Regex.Match(line, @"^\s*(?:(?:public|private|protected)\s+)?(?:static\s+)?(?:get|set)\s+(\w+)\s*\(");
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
        // This handles generic types like Promise<any>, Array<string>, etc.
        var match = Regex.Match(line, @"\):\s*([\w<>,\[\]\s]+?)\s*(?:\{|=>)");
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Check for implicit return from arrow function
        if (line.Contains("=>") && !line.Contains("{"))
        {
            return "inferred";
        }

        return null;
    }
}
