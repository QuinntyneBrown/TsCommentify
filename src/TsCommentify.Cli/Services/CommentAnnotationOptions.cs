namespace TsCommentify.Cli.Services;

/// <summary>
/// JSDoc modifier tags (e.g. <c>@deprecated</c>, <c>@internal</c>) that the user
/// asked — via CLI flags — to append to every generated <em>top-level</em>
/// declaration comment (function/class/interface/enum/type). Members
/// (methods, properties, enum members) intentionally do not receive them: marking
/// the declaration already covers its surface. The list is empty when no
/// annotation flag is passed, in which case generation is unchanged.
/// </summary>
public sealed class CommentAnnotationOptions
{
    /// <summary>
    /// The fully-formed tags to emit, in the order they should appear
    /// (e.g. <c>["@deprecated", "@internal"]</c>). Empty by default.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
