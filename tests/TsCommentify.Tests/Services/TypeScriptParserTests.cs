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

    [Fact]
    public void ParseFunctions_WithNestedGenericReturnType_ParsesCorrectly()
    {
        // Arrange
        var content = @"class DataService {
  getData(): Promise<Array<string>> {
    return Promise.resolve([]);
  }
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("getData");
        result[0].ReturnType.Should().Be("Promise<Array<string>>");
    }

    [Fact]
    public void ParseFunctions_WithInterface_ParsesInterfaceDeclaration()
    {
        // Arrange
        var content = @"interface User {
  id: string;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "User" && f.LineNumber == 1 && !f.HasComment);
    }

    [Fact]
    public void ParseFunctions_WithExportedInterface_ParsesInterfaceDeclaration()
    {
        // Arrange
        var content = @"export interface Product {
  name: string;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "Product" && f.LineNumber == 1);
    }

    [Fact]
    public void ParseFunctions_WithInterfaceProperties_ParsesEachProperty()
    {
        // Arrange
        var content = @"interface User {
  id: string;
  age: number;
  isActive?: boolean;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "id" && f.LineNumber == 2);
        result.Should().Contain(f => f.Name == "age" && f.LineNumber == 3);
        result.Should().Contain(f => f.Name == "isActive" && f.LineNumber == 4);
    }

    [Fact]
    public void ParseFunctions_WithInterfaceMethodSignature_ParsesParametersAndReturnType()
    {
        // Arrange
        var content = @"interface Repository {
  findById(id: string): User;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        var method = result.Single(f => f.Name == "findById");
        method.Parameters.Should().HaveCount(1);
        method.Parameters[0].Name.Should().Be("id");
        method.Parameters[0].Type.Should().Be("string");
        method.ReturnType.Should().Be("User");
    }

    [Fact]
    public void ParseFunctions_WithInterfaceMethodWithoutReturnType_ParsesCorrectly()
    {
        // Arrange
        var content = @"interface Logger {
  log(message: string);
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        var method = result.Single(f => f.Name == "log");
        method.Parameters.Should().HaveCount(1);
        method.ReturnType.Should().BeNull();
    }

    [Fact]
    public void ParseFunctions_WithReadonlyProperty_ParsesName()
    {
        // Arrange
        var content = @"interface Config {
  readonly apiUrl: string;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "apiUrl");
    }

    [Fact]
    public void ParseFunctions_WithCommentedInterface_DetectsComment()
    {
        // Arrange
        var content = @"/**
 * The user contract.
 */
interface User {
  id: string;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Single(f => f.Name == "User").HasComment.Should().BeTrue();
        // The property still needs a comment.
        result.Single(f => f.Name == "id").HasComment.Should().BeFalse();
    }

    [Fact]
    public void ParseFunctions_WithCommentedInterfaceMember_DetectsComment()
    {
        // Arrange
        var content = @"interface User {
  /** The unique identifier. */
  id: string;
  name: string;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Single(f => f.Name == "id").HasComment.Should().BeTrue();
        result.Single(f => f.Name == "name").HasComment.Should().BeFalse();
    }

    [Fact]
    public void ParseFunctions_WithNestedObjectProperty_DoesNotParseNestedMembersAsTopLevel()
    {
        // Arrange
        var content = @"interface Settings {
  options: {
    timeout: number;
    retries: number;
  };
  enabled: boolean;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "options");
        result.Should().Contain(f => f.Name == "enabled");
        // Nested object members are not treated as interface members.
        result.Should().NotContain(f => f.Name == "timeout");
        result.Should().NotContain(f => f.Name == "retries");
    }

    [Fact]
    public void ParseFunctions_WithIndexSignature_SkipsIt()
    {
        // Arrange
        var content = @"interface Dictionary {
  [key: string]: number;
  size: number;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "size");
        result.Should().NotContain(f => f.Name == "key");
    }

    [Fact]
    public void ParseFunctions_WithMultipleInterfaces_ParsesAll()
    {
        // Arrange
        var content = @"interface First {
  a: string;
}

interface Second {
  b: number;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "First");
        result.Should().Contain(f => f.Name == "Second");
        result.Should().Contain(f => f.Name == "a");
        result.Should().Contain(f => f.Name == "b");
    }

    [Fact]
    public void ParseFunctions_WithInterfaceAndFunction_ParsesBoth()
    {
        // Arrange
        var content = @"interface Greeter {
  greeting: string;
}

function greet(name: string): string {
  return name;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "Greeter");
        result.Should().Contain(f => f.Name == "greeting");
        result.Should().Contain(f => f.Name == "greet" && f.ReturnType == "string");
    }

    [Fact]
    public void ParseFunctions_WithInterfaceExtends_ParsesInterfaceName()
    {
        // Arrange
        var content = @"export interface Admin extends User {
  permissions: string[];
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "Admin");
        result.Should().Contain(f => f.Name == "permissions");
    }

    [Fact]
    public void ParseFunctions_WithMultiLineMethodSignature_ParsesSingleMemberWithoutPhantoms()
    {
        // Arrange
        var content = @"interface Repository {
  save(
    entity: User,
    options: SaveOptions
  ): Promise<void>;
  id: string;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        var save = result.Single(f => f.Name == "save");
        save.Parameters.Should().HaveCount(2);
        save.Parameters[0].Name.Should().Be("entity");
        save.Parameters[1].Name.Should().Be("options");
        save.ReturnType.Should().Be("Promise<void>");
        // Wrapped parameter lines must NOT be emitted as phantom interface members.
        result.Should().NotContain(f => f.Name == "entity");
        result.Should().NotContain(f => f.Name == "options");
        result.Should().Contain(f => f.Name == "id");
    }

    [Fact]
    public void ParseFunctions_WithMultiLineFunctionTypedProperty_DoesNotEmitPhantomParameters()
    {
        // Arrange
        var content = @"interface Form {
  submit: (
    data: FormData
  ) => Promise<void>;
  label: string;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "submit");
        result.Should().Contain(f => f.Name == "label");
        result.Should().NotContain(f => f.Name == "data");
    }

    [Fact]
    public void ParseFunctions_WithBraceInStringLiteralType_DoesNotDropFollowingMembers()
    {
        // Arrange
        var content = @"interface Closer {
  symbol: ""}"";
  missedOne: number;
  missedTwo: number;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "symbol");
        result.Should().Contain(f => f.Name == "missedOne");
        result.Should().Contain(f => f.Name == "missedTwo");
    }

    [Fact]
    public void ParseFunctions_WithBraceInStringLiteral_DoesNotLeakIntoNextInterface()
    {
        // Arrange
        var content = @"interface First {
  opener: ""{"";
  alpha: number;
}

interface Second {
  beta: number;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "First");
        result.Should().Contain(f => f.Name == "alpha");
        result.Should().Contain(f => f.Name == "Second");
        result.Should().Contain(f => f.Name == "beta");
    }

    [Fact]
    public void ParseFunctions_WithTrailingInlineBlockComment_DoesNotSuppressNextMember()
    {
        // Arrange
        var content = @"interface User {
  a: string; /* note */
  b: number;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert: a trailing inline /* */ comment must not be mistaken for a doc
        // comment on the following member.
        result.Single(f => f.Name == "b").HasComment.Should().BeFalse();
        result.Should().Contain(f => f.Name == "a");
    }

    [Fact]
    public void ParseFunctions_WithStarSlashInStringLiteral_DoesNotSuppressNextMember()
    {
        // Arrange
        var content = @"interface Parser {
  closeComment: ""*/"";
  shouldBeCommented: number;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "closeComment");
        result.Single(f => f.Name == "shouldBeCommented").HasComment.Should().BeFalse();
    }

    [Fact]
    public void ParseFunctions_WithTrailingInlineCommentAboveFunction_StillDocumentsFunction()
    {
        // Arrange
        var content = @"const sep = ""*/"";
function realFunction(x: number): number {
  return x;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Single(f => f.Name == "realFunction").HasComment.Should().BeFalse();
    }

    [Fact]
    public void ParseFunctions_WithInlineFirstMember_StillParsesRemainingMembers()
    {
        // Arrange: the first member shares the declaration line; subsequent members
        // on their own lines must still be parsed correctly (no corruption).
        var content = @"interface A { id: string;
  name: string;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "A");
        result.Should().Contain(f => f.Name == "name");
        // 'id' shares the declaration line, so it cannot receive a line-based
        // comment and is intentionally not emitted as a documentable member.
        result.Should().NotContain(f => f.Name == "id");
    }

    [Fact]
    public void ParseFunctions_WithGenericDefaultObjectType_ParsesMembers()
    {
        // Arrange: the `{}` lives in the generic parameter list, not the body.
        var content = @"interface State<T = {}> {
  current: T;
  set(next: T): void;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "State");
        result.Should().Contain(f => f.Name == "current");
        result.Should().Contain(f => f.Name == "set");
    }

    [Fact]
    public void ParseFunctions_WithGenericConstraintObjectType_ParsesMembers()
    {
        // Arrange
        var content = @"interface Repo<T extends { id: number }> {
  add(item: T): void;
  count: number;
}";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().Contain(f => f.Name == "Repo");
        result.Should().Contain(f => f.Name == "add");
        result.Should().Contain(f => f.Name == "count");
    }

    [Fact]
    public void ParseFunctions_WithLoneCarriageReturnEndings_DoesNotThrow()
    {
        // Arrange: classic-Mac style lone-CR separators. ParseFunctions and the
        // file writer must agree on line splitting; this must not throw.
        var content = "interface Foo {\r  id: string;\r}\r";
        var filePath = Path.Combine(_testDirectory, $"cr_{Guid.NewGuid()}.ts");
        File.WriteAllText(filePath, content);

        // Act
        Action act = () => _parser.ParseFunctions(filePath).ToList();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ParseFunctions_WithUnionTypeAlias_ParsesTypeDeclaration()
    {
        // Arrange
        var content = @"export type ActivityTone = 'default' | 'outdoor' | 'indoor' | 'food';";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().ContainSingle();
        result[0].Name.Should().Be("ActivityTone");
        result[0].LineNumber.Should().Be(1);
        result[0].Parameters.Should().BeEmpty();
        result[0].ReturnType.Should().BeNull();
        result[0].HasComment.Should().BeFalse();
    }

    [Fact]
    public void ParseFunctions_WithGenericTypeAlias_ParsesTypeDeclaration()
    {
        // Arrange
        var content = @"type Nullable<T> = T | null;";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().ContainSingle(f => f.Name == "Nullable");
    }

    [Fact]
    public void ParseFunctions_WithCommentedTypeAlias_DetectsComment()
    {
        // Arrange
        var content = @"/** The tone of an activity. */
export type ActivityTone = 'default' | 'food';";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Single(f => f.Name == "ActivityTone").HasComment.Should().BeTrue();
    }

    [Fact]
    public void ParseFunctions_WithTypeofAndTypeProperty_DoesNotTreatAsTypeAlias()
    {
        // Arrange: neither `typeof` nor a `type:` property is a type alias.
        var content = @"interface Token {
  type: string;
}

const original = typeof window;";
        var filePath = CreateTestFile(content);

        // Act
        var result = _parser.ParseFunctions(filePath).ToList();

        // Assert
        result.Should().NotContain(f => f.Name == "window");
        result.Should().NotContain(f => f.Name == "original");
        result.Should().Contain(f => f.Name == "type");
    }

    private string CreateTestFile(string content)
    {
        var filePath = Path.Combine(_testDirectory, $"test_{Guid.NewGuid()}.ts");
        File.WriteAllText(filePath, content);
        return filePath;
    }
}
