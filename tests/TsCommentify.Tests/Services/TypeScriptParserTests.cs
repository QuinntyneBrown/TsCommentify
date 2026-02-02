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

    [Fact]
    public void ParseFunctions_WithClassMethod_ParsesCorrectly()
    {
        // Arrange
        var content = @"class Calculator {
  add(a: number, b: number): number {
    return a + b;
  }
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
        result[0].ReturnType.Should().Be("number");
        result[0].HasComment.Should().BeFalse();
    }

    [Fact]
    public void ParseFunctions_WithPrivateClassMethod_ParsesCorrectly()
    {
        // Arrange
        var content = @"class Service {
  private processData(data: string): void {
    console.log(data);
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("processData");
        result[0].Parameters.Should().HaveCount(1);
        result[0].Parameters[0].Name.Should().Be("data");
        result[0].ReturnType.Should().Be("void");
    }

    [Fact]
    public void ParseFunctions_WithPublicClassMethod_ParsesCorrectly()
    {
        // Arrange
        var content = @"class UserService {
  public getUser(id: string): User {
    return users[id];
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("getUser");
        result[0].Parameters.Should().HaveCount(1);
        result[0].ReturnType.Should().Be("User");
    }

    [Fact]
    public void ParseFunctions_WithProtectedClassMethod_ParsesCorrectly()
    {
        // Arrange
        var content = @"class BaseClass {
  protected validate(input: string): boolean {
    return input.length > 0;
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("validate");
        result[0].Parameters.Should().HaveCount(1);
        result[0].ReturnType.Should().Be("boolean");
    }

    [Fact]
    public void ParseFunctions_WithStaticClassMethod_ParsesCorrectly()
    {
        // Arrange
        var content = @"class MathUtils {
  static calculateSum(numbers: number[]): number {
    return numbers.reduce((a, b) => a + b, 0);
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("calculateSum");
        result[0].Parameters.Should().HaveCount(1);
        result[0].ReturnType.Should().Be("number");
    }

    [Fact]
    public void ParseFunctions_WithAsyncClassMethod_ParsesCorrectly()
    {
        // Arrange
        var content = @"class ApiService {
  async fetchData(url: string): Promise<any> {
    return await fetch(url);
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("fetchData");
        result[0].Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void ParseFunctions_WithMultipleClassMethods_ParsesAll()
    {
        // Arrange
        var content = @"class Calculator {
  add(a: number, b: number): number {
    return a + b;
  }

  private subtract(a: number, b: number): number {
    return a - b;
  }

  public multiply(a: number, b: number): number {
    return a * b;
  }

  protected divide(a: number, b: number): number {
    return a / b;
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(4);
        result[0].Name.Should().Be("add");
        result[1].Name.Should().Be("subtract");
        result[2].Name.Should().Be("multiply");
        result[3].Name.Should().Be("divide");
    }

    [Fact]
    public void ParseFunctions_WithClassMethodWithComment_DetectsComment()
    {
        // Arrange
        var content = @"class Service {
  /**
   * Process the data
   */
  processData(data: string): void {
    console.log(data);
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].HasComment.Should().BeTrue();
    }

    [Fact]
    public void ParseFunctions_WithGetter_ParsesCorrectly()
    {
        // Arrange
        var content = @"class Person {
  get name(): string {
    return this._name;
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("name");
        result[0].ReturnType.Should().Be("string");
    }

    [Fact]
    public void ParseFunctions_WithSetter_ParsesCorrectly()
    {
        // Arrange
        var content = @"class Person {
  set name(value: string) {
    this._name = value;
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("name");
        result[0].Parameters.Should().HaveCount(1);
        result[0].Parameters[0].Name.Should().Be("value");
    }

    [Fact]
    public void ParseFunctions_WithClassAndStandaloneFunctions_ParsesAll()
    {
        // Arrange
        var content = @"function standalone(): void {
  console.log('standalone');
}

class MyClass {
  method(): void {
    console.log('method');
  }
}

const arrow = (): void => {
  console.log('arrow');
};";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].Name.Should().Be("standalone");
        result[1].Name.Should().Be("method");
        result[2].Name.Should().Be("arrow");
    }

    [Fact]
    public void ParseFunctions_WithClassMethodWithoutReturnType_ParsesCorrectly()
    {
        // Arrange
        var content = @"class Logger {
  log(message: string) {
    console.log(message);
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("log");
        result[0].Parameters.Should().HaveCount(1);
        result[0].ReturnType.Should().BeNull();
    }

    [Fact]
    public void ParseFunctions_WithPublicStaticMethod_ParsesCorrectly()
    {
        // Arrange
        var content = @"class Utils {
  public static formatDate(date: Date): string {
    return date.toISOString();
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("formatDate");
        result[0].ReturnType.Should().Be("string");
    }

    [Fact]
    public void ParseFunctions_WithPrivateAsyncMethod_ParsesCorrectly()
    {
        // Arrange
        var content = @"class DataService {
  private async loadData(): Promise<void> {
    await fetch('/api/data');
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("loadData");
    }

    private string CreateTestFile(string content)
    {
        var filePath = Path.Combine(_testDirectory, $"test_{Guid.NewGuid()}.ts");
        File.WriteAllText(filePath, content);
        return filePath;
    }
}
