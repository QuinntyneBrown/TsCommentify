using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TsCommentify.Cli.Services;

namespace TsCommentify.Tests.Services;

public class TypeScriptParserTests : IDisposable
{
    private readonly Mock<ILogger<TypeScriptParser>> _loggerMock;
    private readonly TypeScriptParser _parser;
    private readonly string _testDirectory;

    public TypeScriptParserTests()
    {
        _loggerMock = new Mock<ILogger<TypeScriptParser>>();
        _parser = new TypeScriptParser(_loggerMock.Object);
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public void ParseFunctions_WithNonExistentFile_ReturnsEmpty()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "nonexistent.ts");

        // Act
        var result = _parser.ParseFunctions(filePath);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseFunctions_WithSimpleFunction_ParsesCorrectly()
    {
        // Arrange
        var content = @"function add(a: number, b: number): number {
  return a + b;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("add");
        result[0].Parameters.Should().HaveCount(2);
        result[0].Parameters[0].Name.Should().Be("a");
        result[0].Parameters[0].Type.Should().Be("number");
        result[0].Parameters[1].Name.Should().Be("b");
        result[0].Parameters[1].Type.Should().Be("number");
        result[0].ReturnType.Should().Be("number");
        result[0].HasComment.Should().BeFalse();
    }

    [Fact]
    public void ParseFunctions_WithExportedFunction_ParsesCorrectly()
    {
        // Arrange
        var content = @"export function calculate(value: string): void {
  console.log(value);
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("calculate");
        result[0].Parameters.Should().HaveCount(1);
        result[0].HasComment.Should().BeFalse();
    }

    [Fact]
    public void ParseFunctions_WithArrowFunction_ParsesCorrectly()
    {
        // Arrange
        var content = @"const multiply = (x: number, y: number): number => {
  return x * y;
};";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("multiply");
        result[0].Parameters.Should().HaveCount(2);
        result[0].ReturnType.Should().Be("number");
    }

    [Fact]
    public void ParseFunctions_WithFunctionExpression_ParsesCorrectly()
    {
        // Arrange
        var content = @"const greet = function(name: string) {
  console.log(name);
};";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("greet");
        result[0].Parameters.Should().HaveCount(1);
        result[0].Parameters[0].Name.Should().Be("name");
    }

    [Fact]
    public void ParseFunctions_WithJSDocComment_DetectsComment()
    {
        // Arrange
        var content = @"/**
 * Adds two numbers
 * @param a First number
 * @param b Second number
 * @returns Sum of a and b
 */
function add(a: number, b: number): number {
  return a + b;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].HasComment.Should().BeTrue();
    }

    [Fact]
    public void ParseFunctions_WithSingleLineComment_DetectsComment()
    {
        // Arrange
        var content = @"// This function adds two numbers
function add(a: number, b: number): number {
  return a + b;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].HasComment.Should().BeTrue();
    }

    [Fact]
    public void ParseFunctions_WithMultipleFunctions_ParsesAll()
    {
        // Arrange
        var content = @"function add(a: number, b: number): number {
  return a + b;
}

function subtract(a: number, b: number): number {
  return a - b;
}

const multiply = (a: number, b: number): number => a * b;";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].Name.Should().Be("add");
        result[1].Name.Should().Be("subtract");
        result[2].Name.Should().Be("multiply");
    }

    [Fact]
    public void ParseFunctions_WithParametersWithDefaultValues_ParsesCorrectly()
    {
        // Arrange
        var content = @"function greet(name: string = ""World""): string {
  return `Hello, ${name}`;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Parameters.Should().HaveCount(1);
        result[0].Parameters[0].Name.Should().Be("name");
        result[0].Parameters[0].Type.Should().Be("string");
    }

    [Fact]
    public void ParseFunctions_WithAsyncFunction_ParsesCorrectly()
    {
        // Arrange
        var content = @"async function fetchData(url: string): Promise<any> {
  return await fetch(url);
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("fetchData");
    }

    [Fact]
    public void ParseFunctions_WithNoParameters_ParsesCorrectly()
    {
        // Arrange
        var content = @"function initialize(): void {
  console.log('Initialized');
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Parameters.Should().BeEmpty();
    }

    [Fact]
    public void ParseFunctions_WithComplexTypes_ParsesCorrectly()
    {
        // Arrange
        var content = @"function process(items: Array<string>, callback: (item: string) => void): boolean {
  return true;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Parameters.Should().HaveCount(2);
    }

    private string CreateTestFile(string content)
    {
        var filePath = Path.Combine(_testDirectory, $"test_{Guid.NewGuid()}.ts");
        File.WriteAllText(filePath, content);
        return filePath;
    }
}
