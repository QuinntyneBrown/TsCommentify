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
    bool HasComment,
    // The declaration's kind, mirroring the sidecar's wire vocabulary:
    // "function" | "class" | "interface" | "enum" | "type" (top-level) and
    // "method" | "property" | "enum-member" (members). Only top-level kinds
    // receive the optional @deprecated/@internal/etc. annotation tags.
    // ("property" is an interface property signature; class fields have no
    // line-above insertion point and are not documented by either parser.)
    string Kind = "function");

public record ParameterInfo(string Name, string? Type);
