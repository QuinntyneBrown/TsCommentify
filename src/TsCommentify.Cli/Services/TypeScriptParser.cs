using System.Text;
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

        // Split only on '\n' (stripping a trailing '\r'). This MUST match how
        // FileProcessor.AddCommentsToFileAsync splits the file, otherwise the line
        // numbers produced here would not line up with the lines it edits (a lone
        // '\r', which File.ReadAllLines treats as a separator, would desync them).
        var lines = File.ReadAllText(filePath).Split('\n');
        for (int k = 0; k < lines.Length; k++)
        {
            lines[k] = lines[k].TrimEnd('\r');
        }

        var functions = new List<FunctionInfo>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            
            // Skip empty lines and single-line comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;

            // Check for interface declarations. An interface and its members are
            // parsed as a block so the body is not re-evaluated as functions; the
            // loop resumes after the interface's closing brace.
            if (IsInterfaceDeclaration(line))
            {
                i = ParseInterface(lines, i, functions);
                continue;
            }

            // Check for type alias declarations (e.g. `export type Tone = 'a' | 'b';`).
            // The alias header receives the comment; its right-hand side is not
            // re-evaluated as functions.
            if (IsTypeAliasDeclaration(line))
            {
                var typeAlias = ParseTypeAliasDeclaration(lines, i);
                if (typeAlias != null)
                {
                    functions.Add(typeAlias);
                }
                continue;
            }

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

    private bool IsInterfaceDeclaration(string line)
    {
        // Match: [export] [declare] interface Name [<...>] [extends ...] {
        return Regex.IsMatch(line, @"^\s*(export\s+)?(declare\s+)?interface\s+\w+");
    }

    private bool IsTypeAliasDeclaration(string line)
    {
        // Match: [export] [declare] type Name [<...>] =
        // The `type` keyword must be followed by an identifier (so `typeof`,
        // `type:` properties, and `type { X } from ...` re-exports do not match).
        return Regex.IsMatch(line, @"^\s*(export\s+)?(declare\s+)?type\s+\w+\s*(<[^=]*>)?\s*=");
    }

    private FunctionInfo? ParseTypeAliasDeclaration(string[] lines, int lineIndex)
    {
        var line = lines[lineIndex];
        var nameMatch = Regex.Match(line, @"type\s+(\w+)");
        if (!nameMatch.Success)
        {
            return null;
        }

        // A type alias has no parameters or return type; only the name is
        // documented, yielding a `/** <Readable name>. */` block.
        return new FunctionInfo(
            Name: nameMatch.Groups[1].Value,
            LineNumber: lineIndex + 1,
            Content: line,
            Parameters: new List<ParameterInfo>(),
            ReturnType: null,
            HasComment: HasCommentAbove(lines, lineIndex));
    }

    // Parses an interface declaration and its members, adding a FunctionInfo for
    // the interface itself and for each property/method signature in its body.
    // Returns the index of the interface's closing brace so the caller can skip
    // over the already-processed block.
    //
    // The body is walked character-by-character (aware of strings, template/char
    // literals and comments) so that:
    //   - braces/parentheses inside string literals or comments do not corrupt
    //     depth tracking,
    //   - multi-line member signatures are accumulated into a single logical
    //     declaration before being classified (no phantom per-parameter members),
    //   - only members that begin their own physical line are documented, since
    //     comments are inserted on the line above the member.
    private int ParseInterface(string[] lines, int startIndex, List<FunctionInfo> results)
    {
        var declLine = lines[startIndex];
        var nameMatch = Regex.Match(declLine, @"interface\s+(\w+)");
        if (nameMatch.Success)
        {
            results.Add(new FunctionInfo(
                Name: nameMatch.Groups[1].Value,
                LineNumber: startIndex + 1,
                Content: declLine,
                Parameters: new List<ParameterInfo>(),
                ReturnType: null,
                HasComment: HasCommentAbove(lines, startIndex)));
        }

        var braceDepth = 0;
        var parenDepth = 0;
        var angleDepth = 0;
        var inBlockComment = false;
        var stringDelim = '\0';

        var memberBuf = new StringBuilder();
        var memberStartLine = -1;
        var memberStartsLine = false;

        void Append(char c)
        {
            if (char.IsWhiteSpace(c))
            {
                if (memberBuf.Length > 0 && memberBuf[memberBuf.Length - 1] != ' ')
                    memberBuf.Append(' ');
            }
            else
            {
                memberBuf.Append(c);
            }
        }

        void Flush()
        {
            if (memberStartLine >= 0 && memberStartsLine)
            {
                var text = memberBuf.ToString().Trim();
                if (text.Length > 0)
                {
                    var hasComment = HasCommentAbove(lines, memberStartLine);
                    var member = ParseInterfaceMember(text, memberStartLine, hasComment);
                    if (member != null)
                        results.Add(member);
                }
            }

            memberBuf.Clear();
            memberStartLine = -1;
            memberStartsLine = false;
            parenDepth = 0;
        }

        for (int li = startIndex; li < lines.Length; li++)
        {
            var line = lines[li];

            for (int ci = 0; ci < line.Length; ci++)
            {
                var c = line[ci];
                var next = ci + 1 < line.Length ? line[ci + 1] : '\0';

                if (inBlockComment)
                {
                    if (c == '*' && next == '/') { inBlockComment = false; ci++; }
                    continue;
                }

                if (stringDelim != '\0')
                {
                    if (memberStartLine >= 0) memberBuf.Append(c);
                    if (c == '\\' && next != '\0')
                    {
                        memberBuf.Append(next);
                        ci++;
                    }
                    else if (c == stringDelim)
                    {
                        stringDelim = '\0';
                    }
                    continue;
                }

                if (c == '/' && next == '/') break;            // line comment: ignore rest
                if (c == '/' && next == '*') { inBlockComment = true; ci++; continue; }

                if (c == '"' || c == '\'' || c == '`')
                {
                    stringDelim = c;
                    if (memberStartLine >= 0) memberBuf.Append(c);
                    continue;
                }

                // Before the body opens, track the declaration's generic parameter
                // list (<...>) so that braces inside it (e.g. `interface S<T = {}>`)
                // are not mistaken for the interface body.
                if (braceDepth == 0)
                {
                    if (c == '<') { angleDepth++; continue; }
                    if (c == '>' && angleDepth > 0) { angleDepth--; continue; }
                    if (angleDepth > 0 && (c == '{' || c == '}')) continue; // inside generics
                }

                if (c == '{')
                {
                    braceDepth++;
                    if (braceDepth == 1) continue;                          // interface body opens
                    if (memberStartLine >= 0) Append(c);                    // nested object type
                    continue;
                }

                if (c == '}')
                {
                    braceDepth--;
                    if (braceDepth == 0) { Flush(); return li; }            // interface body closes
                    if (memberStartLine >= 0) Append(c);                    // nested object type
                    continue;
                }

                if (braceDepth != 1)
                {
                    // Inside a nested object type (depth >= 2): keep as member text.
                    if (c == '(') parenDepth++;
                    else if (c == ')') parenDepth--;
                    if (memberStartLine >= 0) Append(c);
                    continue;
                }

                // Directly in the interface body.
                if (c == ';' && parenDepth == 0) { Flush(); continue; }

                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;

                if (memberStartLine < 0)
                {
                    if (char.IsWhiteSpace(c)) continue;
                    memberStartLine = li;
                    // Only document members that are the first content on their line;
                    // a member sharing a line (with the opening brace or another
                    // member) cannot receive a comment via line-based insertion.
                    memberStartsLine = line.Substring(0, ci).Trim().Length == 0;
                }

                Append(c);
            }

            // Separate tokens across physical line breaks while a member is open.
            if (memberStartLine >= 0) Append(' ');
        }

        Flush();
        return lines.Length - 1;
    }

    private FunctionInfo? ParseInterfaceMember(string memberText, int startLineIndex, bool hasComment)
    {
        // Skip index signatures ([key: string]: T) and call/construct signatures
        // ((...) and new (...)) which have no meaningful member name.
        if (memberText.StartsWith("[") || memberText.StartsWith("(") || memberText.StartsWith("new "))
        {
            return null;
        }

        // Method signature: name[?]<generics>(params)[: returnType]
        var methodMatch = Regex.Match(memberText, @"^(?:readonly\s+)?(\w+)\??\s*(?:<[^>]*>)?\s*\(");
        if (methodMatch.Success)
        {
            return new FunctionInfo(
                Name: methodMatch.Groups[1].Value,
                LineNumber: startLineIndex + 1,
                Content: memberText,
                Parameters: ParseParameters(memberText),
                ReturnType: ParseInterfaceMemberReturnType(memberText),
                HasComment: hasComment);
        }

        // Property signature: name[?]: type
        var propertyMatch = Regex.Match(memberText, @"^(?:readonly\s+)?(\w+)\??\s*:");
        if (propertyMatch.Success)
        {
            return new FunctionInfo(
                Name: propertyMatch.Groups[1].Value,
                LineNumber: startLineIndex + 1,
                Content: memberText,
                Parameters: new List<ParameterInfo>(),
                ReturnType: null,
                HasComment: hasComment);
        }

        return null;
    }

    private string? ParseInterfaceMemberReturnType(string memberText)
    {
        // Interface method signatures have no '{' body, so the return type is the
        // text after the parameter list's '):' up to the (already stripped) ';'.
        var match = Regex.Match(memberText, @"\)\s*:\s*(.+?)\s*;?\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private bool HasCommentAbove(string[] lines, int lineIndex)
    {
        // Inspect the closest non-blank line above the declaration. It indicates an
        // existing doc comment only when it is genuinely a comment line: a line
        // comment (//), or part of a block comment (opens with /* or /**, or
        // continues/closes with * or */). A code line that merely contains '*/' or
        // '/**' inside a string literal or as a trailing inline comment is NOT a
        // doc comment for this declaration and must not suppress documentation.
        for (int i = lineIndex - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;

            return line.StartsWith("//")
                || line.StartsWith("/*")
                || line.StartsWith("*");
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
