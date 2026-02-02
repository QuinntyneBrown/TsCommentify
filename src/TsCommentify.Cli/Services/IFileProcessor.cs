namespace TsCommentify.Cli.Services;

public interface IFileProcessor
{
    Task ProcessFileAsync(string filePath);
    Task ProcessDirectoryAsync(string directoryPath);
}
