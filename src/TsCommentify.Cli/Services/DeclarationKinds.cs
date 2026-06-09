namespace TsCommentify.Cli.Services;

/// <summary>
/// Classifies <see cref="FunctionInfo.Kind"/> values. A "top-level" declaration
/// (function/class/interface/enum/type) is a unit of API surface and therefore
/// eligible for the optional annotation tags; members (method/property/
/// enum-member) are not. Single source of truth shared by
/// <see cref="CommentGenerator"/> (new comments) and <see cref="FileProcessor"/>
/// (adding a tag to an existing comment).
/// </summary>
public static class DeclarationKinds
{
    private static readonly HashSet<string> TopLevel = new(StringComparer.OrdinalIgnoreCase)
    {
        "function", "class", "interface", "enum", "type",
    };

    /// <summary>True for function/class/interface/enum/type declarations.</summary>
    public static bool IsTopLevel(string? kind) => kind != null && TopLevel.Contains(kind);
}
