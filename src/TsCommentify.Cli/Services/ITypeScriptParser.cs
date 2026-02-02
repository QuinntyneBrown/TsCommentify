namespace TsCommentify.Cli.Services;

public interface ITypeScriptParser
{
    IEnumerable<FunctionInfo> ParseFunctions(string filePath);
}

public record FunctionInfo(
    string Name,
    int LineNumber,
    string Content,
    List<ParameterInfo> Parameters,
    string? ReturnType,
    bool HasComment);

public record ParameterInfo(string Name, string? Type);
