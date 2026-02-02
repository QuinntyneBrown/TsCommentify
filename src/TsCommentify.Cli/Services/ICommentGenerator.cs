namespace TsCommentify.Cli.Services;

public interface ICommentGenerator
{
    string GenerateComment(FunctionInfo function);
}
