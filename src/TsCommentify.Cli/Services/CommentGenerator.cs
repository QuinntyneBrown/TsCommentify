using System.Text;
using Microsoft.Extensions.Logging;

namespace TsCommentify.Cli.Services;

public class CommentGenerator : ICommentGenerator
{
    private readonly ILogger<CommentGenerator> _logger;

    public CommentGenerator(ILogger<CommentGenerator> logger)
    {
        _logger = logger;
    }

    public string GenerateComment(FunctionInfo function)
    {
        _logger.LogDebug("Generating comment for function: {FunctionName}", function.Name);

        var comment = new StringBuilder();
        comment.AppendLine("/**");
        
        // Add function description
        comment.AppendLine($" * {GenerateDescription(function)}");
        
        // Add parameter documentation if there are parameters
        if (function.Parameters.Any())
        {
            comment.AppendLine(" *");
            foreach (var param in function.Parameters)
            {
                var paramType = param.Type ?? "any";
                comment.AppendLine($" * @param {{{paramType}}} {param.Name} - {GenerateParameterDescription(param)}");
            }
        }
        
        // Add return documentation if return type is specified
        if (!string.IsNullOrEmpty(function.ReturnType))
        {
            comment.AppendLine(" *");
            comment.AppendLine($" * @returns {{{function.ReturnType}}} {GenerateReturnDescription(function)}");
        }
        
        comment.Append(" */");
        
        return comment.ToString();
    }

    private string GenerateDescription(FunctionInfo function)
    {
        // Generate a meaningful description based on function name
        var name = function.Name;
        
        // Convert camelCase or PascalCase to readable format
        var readable = ConvertToReadable(name);
        
        return $"{readable}.";
    }

    private string GenerateParameterDescription(ParameterInfo parameter)
    {
        // Generate description based on parameter name
        var readable = ConvertToReadable(parameter.Name);
        return $"The {readable.ToLower()}";
    }

    private string GenerateReturnDescription(FunctionInfo function)
    {
        var returnType = function.ReturnType;
        
        if (returnType == "void")
            return "No return value";
        
        if (returnType == "Promise")
            return "A promise that resolves when the operation is complete";
        
        if (returnType == "boolean" || returnType == "bool")
            return "True if successful, false otherwise";
        
        return $"The result of the operation";
    }

    private string ConvertToReadable(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return identifier;

        // Insert space before capital letters (for camelCase and PascalCase)
        var result = new StringBuilder();
        
        for (int i = 0; i < identifier.Length; i++)
        {
            var ch = identifier[i];
            
            // Add space before uppercase letter if:
            // - It's not the first character
            // - Previous character is lowercase
            // - OR previous character is uppercase but next is lowercase (for acronyms)
            if (i > 0 && char.IsUpper(ch))
            {
                var prev = identifier[i - 1];
                var hasNext = i < identifier.Length - 1;
                var next = hasNext ? identifier[i + 1] : '\0';
                
                if (char.IsLower(prev) || (char.IsUpper(prev) && hasNext && char.IsLower(next)))
                {
                    result.Append(' ');
                }
            }
            
            result.Append(ch);
        }
        
        // Capitalize first letter
        var readable = result.ToString();
        if (readable.Length > 0)
        {
            readable = char.ToUpper(readable[0]) + readable.Substring(1);
        }
        
        return readable;
    }
}
