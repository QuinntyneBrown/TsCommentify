using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TsCommentify.Cli.Services;

namespace TsCommentify.Tests.Services;

public class CommentGeneratorTests
{
    private readonly Mock<ILogger<CommentGenerator>> _loggerMock;
    private readonly CommentGenerator _generator;

    public CommentGeneratorTests()
    {
        _loggerMock = new Mock<ILogger<CommentGenerator>>();
        _generator = new CommentGenerator(_loggerMock.Object);
    }

    [Fact]
    public void GenerateComment_WithSimpleFunction_GeneratesCorrectComment()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "add",
            LineNumber: 1,
            Content: "function add(a: number, b: number): number",
            Parameters: new List<ParameterInfo>
            {
                new("a", "number"),
                new("b", "number")
            },
            ReturnType: "number",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("/**");
        result.Should().Contain("*/");
        result.Should().Contain("Add");
        result.Should().Contain("@param {number} a");
        result.Should().Contain("@param {number} b");
        result.Should().Contain("@returns {number}");
    }

    [Fact]
    public void GenerateComment_WithNoParameters_GeneratesCommentWithoutParams()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "initialize",
            LineNumber: 1,
            Content: "function initialize(): void",
            Parameters: new List<ParameterInfo>(),
            ReturnType: "void",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("/**");
        result.Should().Contain("*/");
        result.Should().Contain("Initialize");
        result.Should().NotContain("@param");
        result.Should().Contain("@returns {void}");
    }

    [Fact]
    public void GenerateComment_WithNoReturnType_GeneratesCommentWithoutReturns()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "logMessage",
            LineNumber: 1,
            Content: "function logMessage(msg: string)",
            Parameters: new List<ParameterInfo> { new("msg", "string") },
            ReturnType: null,
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("/**");
        result.Should().Contain("*/");
        result.Should().Contain("@param {string} msg");
        result.Should().NotContain("@returns");
    }

    [Fact]
    public void GenerateComment_WithCamelCaseName_ConvertsToReadable()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "calculateTotalPrice",
            LineNumber: 1,
            Content: "function calculateTotalPrice(price: number): number",
            Parameters: new List<ParameterInfo> { new("price", "number") },
            ReturnType: "number",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("Calculate Total Price");
    }

    [Fact]
    public void GenerateComment_WithPascalCaseName_ConvertsToReadable()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "ProcessData",
            LineNumber: 1,
            Content: "function ProcessData(data: any): void",
            Parameters: new List<ParameterInfo> { new("data", "any") },
            ReturnType: "void",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("Process Data");
    }

    [Fact]
    public void GenerateComment_WithParameterWithoutType_UsesAnyType()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "process",
            LineNumber: 1,
            Content: "function process(data)",
            Parameters: new List<ParameterInfo> { new("data", null) },
            ReturnType: null,
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("@param {any} data");
    }

    [Fact]
    public void GenerateComment_WithBooleanReturn_GeneratesAppropriateDescription()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "isValid",
            LineNumber: 1,
            Content: "function isValid(input: string): boolean",
            Parameters: new List<ParameterInfo> { new("input", "string") },
            ReturnType: "boolean",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("@returns {boolean}");
        result.Should().Contain("True if successful");
    }

    [Fact]
    public void GenerateComment_WithPromiseReturn_GeneratesAppropriateDescription()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "fetchData",
            LineNumber: 1,
            Content: "async function fetchData(url: string): Promise",
            Parameters: new List<ParameterInfo> { new("url", "string") },
            ReturnType: "Promise",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("@returns {Promise}");
        result.Should().Contain("promise");
    }

    [Fact]
    public void GenerateComment_WithVoidReturn_GeneratesNoReturnValueDescription()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "cleanup",
            LineNumber: 1,
            Content: "function cleanup(): void",
            Parameters: new List<ParameterInfo>(),
            ReturnType: "void",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("@returns {void}");
        result.Should().Contain("No return value");
    }

    [Fact]
    public void GenerateComment_WithMultipleParameters_IncludesAllParams()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "createUser",
            LineNumber: 1,
            Content: "function createUser(name: string, email: string, age: number): User",
            Parameters: new List<ParameterInfo>
            {
                new("name", "string"),
                new("email", "string"),
                new("age", "number")
            },
            ReturnType: "User",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("@param {string} name");
        result.Should().Contain("@param {string} email");
        result.Should().Contain("@param {number} age");
    }

    [Fact]
    public void GenerateComment_WithAcronymInName_HandlesCorrectly()
    {
        // Arrange
        var function = new FunctionInfo(
            Name: "parseHTMLContent",
            LineNumber: 1,
            Content: "function parseHTMLContent(html: string): Document",
            Parameters: new List<ParameterInfo> { new("html", "string") },
            ReturnType: "Document",
            HasComment: false
        );

        // Act
        var result = _generator.GenerateComment(function);

        // Assert
        result.Should().Contain("Parse HTML Content");
    }
}
