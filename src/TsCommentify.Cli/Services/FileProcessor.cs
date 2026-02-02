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

    public FileProcessor(
        ITypeScriptParser parser,
        ICommentGenerator commentGenerator,
        ILogger<FileProcessor> logger,
        IConfiguration configuration)
    {
        _parser = parser;
        _commentGenerator = commentGenerator;
        _logger = logger;
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

        var functions = _parser.ParseFunctions(filePath);
        var functionsWithoutComments = functions.Where(f => !f.HasComment).ToList();

        if (!functionsWithoutComments.Any())
        {
            _logger.LogInformation("All functions in {FilePath} already have comments", filePath);
            return;
        }

        _logger.LogInformation("Found {Count} functions without comments in {FilePath}", 
            functionsWithoutComments.Count, filePath);

        await AddCommentsToFileAsync(filePath, functionsWithoutComments);
        
        _logger.LogInformation("Successfully updated {FilePath}", filePath);
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

    private async Task AddCommentsToFileAsync(string filePath, List<FunctionInfo> functions)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        var newLines = new List<string>();

        // Sort functions by line number in descending order to avoid index shifting
        var sortedFunctions = functions.OrderByDescending(f => f.LineNumber).ToList();

        var currentLines = lines.ToList();

        foreach (var function in sortedFunctions)
        {
            var comment = _commentGenerator.GenerateComment(function);
            var commentLines = comment.Split('\n', StringSplitOptions.None);
            
            // Insert comment before the function (line numbers are 1-based)
            var insertIndex = function.LineNumber - 1;
            
            // Preserve indentation of the function
            var functionLine = currentLines[insertIndex];
            var indent = GetIndentation(functionLine);
            
            // Insert comment lines with proper indentation
            for (int i = commentLines.Length - 1; i >= 0; i--)
            {
                var commentLine = commentLines[i].TrimEnd();
                currentLines.Insert(insertIndex, indent + commentLine);
            }
        }

        await File.WriteAllLinesAsync(filePath, currentLines);
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
