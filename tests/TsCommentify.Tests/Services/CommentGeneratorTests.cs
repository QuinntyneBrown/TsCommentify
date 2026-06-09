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
        // A void return documents nothing — no @returns tag is emitted.
        result.Should().NotContain("@returns");
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
        result.Should().Contain("Calculates the total price.");
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
        result.Should().Contain("Processes the data.");
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
        result.Should().Contain("True if valid; otherwise false.");
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
    public void GenerateComment_WithVoidReturn_OmitsReturnsTag()
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

        // Assert: documenting "returns nothing" is noise; no @returns for void.
        result.Should().Contain("Cleanup.");
        result.Should().NotContain("@returns");
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
    public void GenerateComment_WithActionVerbName_UsesConjugatedDescription()
    {
        var function = new FunctionInfo(
            Name: "getUserProfile",
            LineNumber: 1,
            Content: string.Empty,
            Parameters: new List<ParameterInfo>(),
            ReturnType: "Profile",
            HasComment: false);

        var result = _generator.GenerateComment(function);

        result.Should().Contain("Gets the user profile.");
    }

    [Fact]
    public void GenerateComment_WithQuestionVerbName_UsesDeterminesWhetherAndNameAwareBoolean()
    {
        var function = new FunctionInfo(
            Name: "hasAccess",
            LineNumber: 1,
            Content: string.Empty,
            Parameters: new List<ParameterInfo>(),
            ReturnType: "boolean",
            HasComment: false);

        var result = _generator.GenerateComment(function);

        result.Should().Contain("Determines whether access.");
        result.Should().Contain("True if access; otherwise false.");
    }

    [Fact]
    public void GenerateComment_WithPromiseOfTypeReturn_DescribesResolvedType()
    {
        var function = new FunctionInfo(
            Name: "loadUser",
            LineNumber: 1,
            Content: string.Empty,
            Parameters: new List<ParameterInfo> { new("id", "string") },
            ReturnType: "Promise<User>",
            HasComment: false);

        var result = _generator.GenerateComment(function);

        result.Should().Contain("@returns {Promise<User>}");
        result.Should().Contain("A promise that resolves to the user.");
    }

    [Fact]
    public void GenerateComment_WithArrayReturn_DescribesAsArray()
    {
        var function = new FunctionInfo(
            Name: "listUsers",
            LineNumber: 1,
            Content: string.Empty,
            Parameters: new List<ParameterInfo>(),
            ReturnType: "User[]",
            HasComment: false);

        var result = _generator.GenerateComment(function);

        result.Should().Contain("An array of user values.");
    }

    [Fact]
    public void GenerateComment_WithFunctionTypedReturn_KeepsFullTypeAndGenericDescription()
    {
        // The return type the regex parser used to drop entirely.
        var function = new FunctionInfo(
            Name: "makeAdder",
            LineNumber: 1,
            Content: string.Empty,
            Parameters: new List<ParameterInfo> { new("base", "number") },
            ReturnType: "(n: number) => number",
            HasComment: false);

        var result = _generator.GenerateComment(function);

        result.Should().Contain("@returns {(n: number) => number}");
        result.Should().Contain("The result of the operation.");
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
        result.Should().Contain("Parses the HTML content.");
    }

    // ---- Annotation tags (CLI flags) --------------------------------------

    private static CommentGenerator GeneratorWith(params string[] tags)
        => new(new Mock<ILogger<CommentGenerator>>().Object,
               new CommentAnnotationOptions { Tags = tags });

    private static FunctionInfo Decl(string name, string kind, params ParameterInfo[] parameters)
        => new(
            Name: name,
            LineNumber: 1,
            Content: string.Empty,
            Parameters: parameters.ToList(),
            ReturnType: null,
            HasComment: false,
            Kind: kind);

    [Theory]
    [InlineData("function")]
    [InlineData("class")]
    [InlineData("interface")]
    [InlineData("enum")]
    [InlineData("type")]
    public void GenerateComment_WithAnnotationFlag_AddsTagToTopLevelDeclaration(string kind)
    {
        var generator = GeneratorWith("@deprecated");

        var result = generator.GenerateComment(Decl("Widget", kind));

        // Emitted as a proper JSDoc body line.
        result.Should().Contain(" * @deprecated");
    }

    [Theory]
    [InlineData("method")]
    [InlineData("property")]
    [InlineData("enum-member")]
    public void GenerateComment_WithAnnotationFlag_DoesNotTagMembers(string kind)
    {
        var generator = GeneratorWith("@deprecated", "@internal");

        var result = generator.GenerateComment(Decl("member", kind));

        result.Should().NotContain("@deprecated");
        result.Should().NotContain("@internal");
    }

    [Fact]
    public void GenerateComment_WithoutAnnotationFlags_EmitsNoTags()
    {
        var generator = GeneratorWith(); // no flags -> unchanged output

        var result = generator.GenerateComment(Decl("UserService", "class"));

        result.Should().Contain("User Service.");
        result.Should().NotContain("@deprecated");
        result.Should().NotContain("@obsolete");
        result.Should().NotContain("@internal");
        result.Should().NotContain("@publicApi");
    }

    [Fact]
    public void GenerateComment_WithMultipleAnnotationTags_EmitsAllInSuppliedOrder()
    {
        var generator = GeneratorWith("@deprecated", "@obsolete", "@internal", "@publicApi");

        var result = generator.GenerateComment(Decl("UserService", "class"));

        result.Should().Contain("@deprecated");
        result.Should().Contain("@obsolete");
        result.Should().Contain("@internal");
        result.Should().Contain("@publicApi");
        result.IndexOf("@deprecated", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("@obsolete", StringComparison.Ordinal));
        result.IndexOf("@obsolete", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("@internal", StringComparison.Ordinal));
        result.IndexOf("@internal", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("@publicApi", StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateComment_WithAnnotationOnFunction_PlacesTagAfterDescriptionBeforeParams()
    {
        var generator = GeneratorWith("@deprecated");

        var result = generator.GenerateComment(
            Decl("calculateTotal", "function", new ParameterInfo("price", "number")));

        result.Should().Contain("@deprecated");
        result.Should().Contain("@param {number} price");
        // Order: description -> tag -> params.
        result.IndexOf("Calculates the total.", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("@deprecated", StringComparison.Ordinal));
        result.IndexOf("@deprecated", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("@param", StringComparison.Ordinal));
    }
}
