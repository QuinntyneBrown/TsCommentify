namespace TsCommentify.Cli.Configuration;

public class FileProcessorOptions
{
    public const string SectionName = "FileProcessor";

    /// <summary>
    /// List of file patterns to ignore during processing.
    /// Supports wildcards like *.spec.ts and *.test.ts
    /// </summary>
    public List<string> IgnorePatterns { get; set; } = new()
    {
        "*.spec.ts",
        "*.test.ts"
    };
}
