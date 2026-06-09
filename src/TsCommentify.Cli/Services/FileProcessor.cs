using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TsCommentify.Cli.Configuration;

namespace TsCommentify.Cli.Services;

public class FileProcessor : IFileProcessor
{
    private readonly ITypeScriptParser _parser;
    private readonly ICommentGenerator _commentGenerator;
    private readonly ILogger<FileProcessor> _logger;
    private readonly FileProcessorOptions _options;
    private readonly CommentAnnotationOptions _annotations;

    public FileProcessor(
        ITypeScriptParser parser,
        ICommentGenerator commentGenerator,
        ILogger<FileProcessor> logger,
        IConfiguration configuration,
        CommentAnnotationOptions? annotations = null)
    {
        _parser = parser;
        _commentGenerator = commentGenerator;
        _logger = logger;
        _annotations = annotations ?? new CommentAnnotationOptions();
        _options = configuration.GetSection(FileProcessorOptions.SectionName).Get<FileProcessorOptions>()
            ?? new FileProcessorOptions();
        
        // Apply default ignore patterns if none are configured
        if (_options.IgnorePatterns.Count == 0)
        {
            _options.IgnorePatterns.Add("*.spec.ts");
            _options.IgnorePatterns.Add("*.test.ts");
        }
    }

    public async Task ProcessFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return;
        }

        if (!IsTypeScriptFile(filePath))
        {
            _logger.LogWarning("Not a TypeScript file: {FilePath}", filePath);
            return;
        }

        _logger.LogInformation("Processing file: {FilePath}", filePath);

        var functions = _parser.ParseFunctions(filePath).ToList();

        // Declarations with no comment get a freshly generated block (which already
        // includes any requested annotation tags for top-level kinds).
        var toInsert = functions.Where(f => !f.HasComment).ToList();

        // When annotation flags are set, top-level declarations that ALREADY have a
        // comment are candidates for an in-place update: each requested tag is added
        // to the existing JSDoc block if it is not already present. (Members and
        // declarations documented with a non-JSDoc comment are left untouched.)
        var toUpdate = _annotations.Tags.Count > 0
            ? functions.Where(f => f.HasComment && DeclarationKinds.IsTopLevel(f.Kind)).ToList()
            : new List<FunctionInfo>();

        if (toInsert.Count == 0 && toUpdate.Count == 0)
        {
            _logger.LogInformation("All declarations in {FilePath} already documented", filePath);
            return;
        }

        _logger.LogInformation(
            "Found {Insert} declaration(s) to document and {Update} commented declaration(s) to check for annotation tags in {FilePath}",
            toInsert.Count, toUpdate.Count, filePath);

        var changed = await ApplyChangesAsync(filePath, toInsert, toUpdate);

        if (changed)
        {
            _logger.LogInformation("Successfully updated {FilePath}", filePath);
        }
        else
        {
            _logger.LogInformation("No changes needed in {FilePath} (annotation tags already present)", filePath);
        }
    }

    public async Task ProcessDirectoryAsync(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogError("Directory not found: {DirectoryPath}", directoryPath);
            return;
        }

        _logger.LogInformation("Processing directory: {DirectoryPath}", directoryPath);

        var tsFiles = Directory.GetFiles(directoryPath, "*.ts", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(directoryPath, "*.tsx", SearchOption.AllDirectories))
            .Where(f => !f.Contains("node_modules") && !f.Contains("dist") && !f.Contains(".d.ts"))
            .Where(f => !ShouldIgnoreFile(f))
            .ToList();

        _logger.LogInformation("Found {Count} TypeScript files to process", tsFiles.Count);

        foreach (var file in tsFiles)
        {
            await ProcessFileAsync(file);
        }

        _logger.LogInformation("Directory processing complete");
    }

    private bool IsTypeScriptFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".ts" || extension == ".tsx";
    }

    private bool ShouldIgnoreFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        
        foreach (var pattern in _options.IgnorePatterns)
        {
            if (MatchesPattern(fileName, pattern))
            {
                _logger.LogDebug("Ignoring file {FilePath} due to pattern {Pattern}", filePath, pattern);
                return true;
            }
        }
        
        return false;
    }

    private bool MatchesPattern(string fileName, string pattern)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // Applies all edits — new comment blocks for undocumented declarations, plus
    // annotation-tag additions to already-commented top-level declarations — to the
    // file. Returns true if anything actually changed (an update is a no-op when the
    // requested tag is already present). Every edit is applied from the bottom of the
    // file upward so earlier edits never shift the line indices of declarations not
    // yet handled.
    private async Task<bool> ApplyChangesAsync(
        string filePath, List<FunctionInfo> inserts, List<FunctionInfo> updates)
    {
        var text = await File.ReadAllTextAsync(filePath);

        // Preserve the file's existing line-ending style and trailing-newline state
        // so editing produces a minimal diff rather than re-encoding the whole file
        // (e.g. LF -> CRLF on Windows).
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var hadTrailingNewline = text.EndsWith("\n");

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (hadTrailingNewline && lines.Count > 0)
        {
            // Drop the empty element produced by the final newline.
            lines.RemoveAt(lines.Count - 1);
        }

        var ops = inserts.Select(f => (function: f, isInsert: true))
            .Concat(updates.Select(f => (function: f, isInsert: false)))
            .OrderByDescending(o => o.function.LineNumber)
            .ToList();

        var changed = false;
        foreach (var (function, isInsert) in ops)
        {
            if (isInsert)
            {
                InsertNewComment(lines, function);
                changed = true;
            }
            else if (TryAddTagsToExistingComment(lines, function))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        var result = string.Join(newline, lines) + (hadTrailingNewline ? newline : string.Empty);
        await File.WriteAllTextAsync(filePath, result);
        return true;
    }

    // Inserts a freshly generated comment block above the declaration.
    private void InsertNewComment(List<string> lines, FunctionInfo function)
    {
        var comment = _commentGenerator.GenerateComment(function);
        var commentLines = comment.Split('\n', StringSplitOptions.None);

        // Insert comment before the declaration (line numbers are 1-based).
        var insertIndex = function.LineNumber - 1;
        var indent = GetIndentation(lines[insertIndex]);

        for (int i = commentLines.Length - 1; i >= 0; i--)
        {
            var commentLine = commentLines[i].TrimEnd();
            lines.Insert(insertIndex, indent + commentLine);
        }
    }

    // Adds each requested annotation tag that is missing from the declaration's
    // existing JSDoc block. Returns true if the block was modified. Only /** ... */
    // blocks are updated; a // or non-doc /* */ comment is left untouched (a JSDoc
    // tag there would be meaningless). Returns false (no change) for anything it
    // cannot safely locate, so a file is never corrupted.
    private bool TryAddTagsToExistingComment(List<string> lines, FunctionInfo function)
    {
        var declIndex = function.LineNumber - 1;
        if (declIndex <= 0 || declIndex >= lines.Count)
        {
            return false;
        }

        // Find the doc comment's closing line directly above the declaration, looking
        // past blank lines and decorators (including multi-line ones like @Component).
        var blockEnd = FindCommentCloserAbove(lines, declIndex - 1);
        if (blockEnd < 0 || !IsCleanCommentCloser(lines[blockEnd]))
        {
            // Not present, or a line with code after the */ — never edit it.
            return false;
        }

        var blockStart = blockEnd;
        while (blockStart >= 0 && !lines[blockStart].TrimStart().StartsWith("/*"))
        {
            blockStart--;
        }
        if (blockStart < 0 || !lines[blockStart].TrimStart().StartsWith("/**"))
        {
            // Not a JSDoc block (malformed, or a plain /* */ comment).
            return false;
        }

        var missing = _annotations.Tags
            .Where(tag => !BlockContainsTag(lines, blockStart, blockEnd, tag))
            .ToList();
        if (missing.Count == 0)
        {
            return false;
        }

        var indent = GetIndentation(lines[blockStart]);

        if (blockStart == blockEnd)
        {
            ExpandSingleLineBlock(lines, blockStart, indent, missing);
        }
        else
        {
            var insertAt = FindTagInsertionIndex(lines, blockStart, blockEnd);
            var toInsert = missing.Select(tag => $"{indent} * {tag}").ToList();

            // Match the generator's layout: a blank separator between the description
            // and a fresh tag group. Only when appending at the end of the block (no
            // existing tag to group with) and there is a non-blank description line
            // immediately above to separate from.
            if (insertAt == blockEnd && insertAt - 1 > blockStart && !IsBlankDocLine(lines[insertAt - 1]))
            {
                toInsert.Insert(0, $"{indent} *");
            }

            lines.InsertRange(insertAt, toInsert);
        }

        return true;
    }

    // Walks up from `start`, skipping blank lines and decorators (including multi-line
    // decorators such as @Component({ ... }) via bracket balancing), and returns the
    // index of the doc comment's closing line if one sits directly above — else -1.
    // Stops at the first code statement, so an unrelated comment is never targeted.
    private static int FindCommentCloserAbove(List<string> lines, int start)
    {
        var i = start;
        while (i >= 0)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
            {
                i--;
                continue;
            }
            if (trimmed.EndsWith("*/"))
            {
                return i; // a block-comment closer (the caller validates it is clean)
            }
            var decoratorStart = DecoratorStartAt(lines, i);
            if (decoratorStart >= 0)
            {
                i = decoratorStart - 1;
                continue;
            }
            return -1; // a code line: no doc comment directly above
        }
        return -1;
    }

    // If line `i` is the last line of a decorator (`@Name`, possibly spanning lines via
    // balanced (), {} or []), returns the index of the line that begins it; else -1.
    // Bracket counting ignores string contents, so a decorator with an unbalanced
    // bracket inside a string literal is simply not skipped (a safe no-op).
    private static int DecoratorStartAt(List<string> lines, int i)
    {
        var depth = 0;
        for (var j = i; j >= 0 && i - j < 256; j--)
        {
            depth += ClosingMinusOpeningBrackets(lines[j]);
            if (depth < 0)
            {
                return -1; // opened more than closed going up: not a self-contained decorator
            }
            if (depth == 0 && lines[j].TrimStart().StartsWith("@"))
            {
                return j;
            }
        }
        return -1;
    }

    private static int ClosingMinusOpeningBrackets(string line)
    {
        var delta = 0;
        foreach (var c in line)
        {
            if (c is ')' or '}' or ']')
            {
                delta++;
            }
            else if (c is '(' or '{' or '[')
            {
                delta--;
            }
        }
        return delta;
    }

    // True only for a line that is exactly a JSDoc closer: a complete single-line
    // `/** ... */`, or a `*`-gutter body line ending in `*/`. A line with code after
    // the `*/` (e.g. a regex literal, or `/** x */ code`) is rejected so it is never
    // mangled.
    private static bool IsCleanCommentCloser(string line)
    {
        var t = line.Trim();
        if (!t.EndsWith("*/"))
        {
            return false;
        }
        // Both forms guarantee nothing follows the */ (the line ends with it) and
        // nothing precedes it but the gutter/opener.
        return t.StartsWith("/**") || t.StartsWith("*");
    }

    // A doc-block body line carrying no text: blank, or just the ` *` gutter.
    private static bool IsBlankDocLine(string line)
    {
        var t = line.Trim();
        return t.Length == 0 || t == "*";
    }

    // A tag is present only when it appears as an actual JSDoc tag — at the START of a
    // comment body line (after the /** or * gutter) — not merely mentioned in prose or
    // inside an @example. Matched as a whole token, so @internal != @internalFoo.
    private static bool BlockContainsTag(List<string> lines, int blockStart, int blockEnd, string tag)
    {
        var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(tag) + @"\b";
        for (var j = blockStart; j <= blockEnd; j++)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(JsDocLineBody(lines[j]), pattern))
            {
                return true;
            }
        }
        return false;
    }

    // The content of a JSDoc line after its `/**` opener or `*` gutter.
    private static string JsDocLineBody(string line)
    {
        var s = line.TrimStart();
        if (s.StartsWith("/**"))
        {
            s = s.Substring(3);
        }
        else if (s.StartsWith("*"))
        {
            s = s.Substring(1);
        }
        return s.TrimStart();
    }

    // The index to insert new tag lines at: before the first existing @-tag body line
    // (so tags group together after the description), else before the closing */.
    private static int FindTagInsertionIndex(List<string> lines, int blockStart, int blockEnd)
    {
        for (var j = blockStart + 1; j < blockEnd; j++)
        {
            if (JsDocLineBody(lines[j]).StartsWith("@"))
            {
                return j;
            }
        }
        return blockEnd;
    }

    // Rewrites a single-line `/** text */` block as a multi-line block so the new tag
    // lines can be added beneath the description.
    private static void ExpandSingleLineBlock(List<string> lines, int index, string indent, List<string> tags)
    {
        var inner = lines[index].Trim();
        if (inner.StartsWith("/**"))
        {
            inner = inner.Substring(3);
        }
        if (inner.EndsWith("*/"))
        {
            inner = inner.Substring(0, inner.Length - 2);
        }
        inner = inner.Trim();

        var rebuilt = new List<string> { $"{indent}/**" };
        if (inner.Length > 0)
        {
            rebuilt.Add($"{indent} * {inner}");
            rebuilt.Add($"{indent} *");
        }
        rebuilt.AddRange(tags.Select(tag => $"{indent} * {tag}"));
        rebuilt.Add($"{indent} */");

        lines.RemoveAt(index);
        lines.InsertRange(index, rebuilt);
    }

    private string GetIndentation(string line)
    {
        var indent = "";
        foreach (var ch in line)
        {
            if (ch == ' ' || ch == '\t')
                indent += ch;
            else
                break;
        }
        return indent;
    }
}
