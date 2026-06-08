using FluentAssertions;
using System.Diagnostics;

namespace TsCommentify.Tests.Integration;

public class CliIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _cliPath;

    public CliIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        
        // Path to the compiled CLI
        var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        _cliPath = Path.Combine(solutionDir, "src", "TsCommentify.Cli", "bin", "Debug", "net8.0", "TsCommentify.Cli.dll");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task Cli_WithSingleFile_AddsComments()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.ts");
        var content = @"function add(a: number, b: number): number {
  return a + b;
}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("/**");
        result.Should().Contain("*/");
        result.Should().Contain("@param");
        result.Should().Contain("@returns");
    }

    [Fact]
    public async Task Cli_WithDirectory_ProcessesAllFiles()
    {
        // Arrange
        var file1 = Path.Combine(_testDirectory, "file1.ts");
        var file2 = Path.Combine(_testDirectory, "file2.tsx");
        
        await File.WriteAllTextAsync(file1, "function test1() {}");
        await File.WriteAllTextAsync(file2, "const test2 = () => {};");

        // Act
        var exitCode = await RunCliAsync(_testDirectory);

        // Assert
        exitCode.Should().Be(0);
        var result1 = await File.ReadAllTextAsync(file1);
        var result2 = await File.ReadAllTextAsync(file2);
        
        result1.Should().Contain("/**");
        result2.Should().Contain("/**");
    }

    [Fact]
    public async Task Cli_WithContractFileContainingInterface_AddsComments()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "foo.contract.ts");
        var content = @"export interface Foo {
  id: string;
  getName(): string;
}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("/**");
        result.Should().Contain("Foo.");
        result.Should().Contain("Id.");
        result.Should().Contain("Gets the name.");
        result.Should().Contain("export interface Foo");
    }

    [Fact]
    public async Task Cli_PreservesLfLineEndings()
    {
        // Arrange: file written with LF-only endings.
        var filePath = Path.Combine(_testDirectory, "lf.contract.ts");
        var content = "export interface Foo {\n  id: string;\n}\n";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert: comments are added but line endings stay LF (no CRLF re-encoding).
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("Foo.");
        result.Should().Contain("Id.");
        result.Should().NotContain("\r\n");
    }

    [Fact]
    public async Task Cli_WithMultiLineMethodSignature_DoesNotInsertCommentInsideParameterList()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "repo.contract.ts");
        var content = @"export interface Repository {
  save(
    entity: string,
    id: number
  ): void;
}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("Saves.");
        result.Should().Contain("@param {string} entity");
        result.Should().Contain("@param {number} id");
        // No phantom per-parameter member documentation injected into the signature.
        result.Should().NotContain("* Entity.");
    }

    [Fact]
    public async Task Cli_WithLoneCarriageReturnLineEndings_DoesNotCrash()
    {
        // Arrange: classic-Mac style lone-CR separators previously crashed the
        // edit phase (parse/edit line-split mismatch).
        var filePath = Path.Combine(_testDirectory, "cr.contract.ts");
        var content = "interface Foo {\r  id: string;\r}\r";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Cli_WithGenericInterfaceDefault_DocumentsMembers()
    {
        // Arrange: braces inside the generic parameter list must not hide the body.
        var filePath = Path.Combine(_testDirectory, "state.contract.ts");
        var content = @"export interface State<T = {}> {
  current: T;
  reset(): void;
}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("State.");
        result.Should().Contain("Current.");
        result.Should().Contain("Resets.");
    }

    [Fact]
    public async Task Cli_WithMultiLineSignature_DocumentsAllParamsAndReturn()
    {
        // This is a sidecar-only capability: the regex fallback cannot read a
        // signature whose parameters span multiple lines. Skip when node is absent.
        if (!NodeAvailable())
        {
            return;
        }

        var filePath = Path.Combine(_testDirectory, "multiline.ts");
        var content = @"function calculateInvoice(
  price: number,
  quantity: number
): number {
  return price * quantity;
}

class Repo {
  findUser(id: string): User
  {
    return null as any;
  }
}";
        await File.WriteAllTextAsync(filePath, content);

        var exitCode = await RunCliAsync(filePath);

        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        // Multi-line signature: every parameter and the return type are documented.
        result.Should().Contain("@param {number} price");
        result.Should().Contain("@param {number} quantity");
        result.Should().Contain("@returns {number}");
        // Method whose opening brace is on the next line is found and documented.
        result.Should().Contain("Finds the user.");
        result.Should().Contain("@param {string} id");
    }

    [Fact]
    public async Task Cli_WithEnumDeclaration_DocumentsEnumAndMembers()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "tone.ts");
        var content = @"export enum ActivityTone {
  Default,
  Outdoor,
  Indoor,
}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("Activity Tone.");
        result.Should().Contain("Default.");
        result.Should().Contain("Outdoor.");
        result.Should().Contain("Indoor.");
        result.Should().Contain("export enum ActivityTone");
        // Exactly four blocks: the enum plus its three members — pins that no member
        // is dropped, duplicated, or stacked into the wrong position.
        (result.Split("/**").Length - 1).Should().Be(4);
    }

    [Fact]
    public async Task Cli_WithSingleLineEnum_DocumentsOnlyTheEnum()
    {
        // Arrange: members share the declaration line, so only the enum itself can
        // be documented — no stacked per-member comment blocks.
        var filePath = Path.Combine(_testDirectory, "direction.ts");
        var content = "export enum Direction { Up, Down }\n";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("Direction.");
        // Exactly one JSDoc block (the enum); members are not individually stacked.
        (result.Split("/**").Length - 1).Should().Be(1);
        result.Should().Contain("export enum Direction { Up, Down }");
    }

    [Fact]
    public async Task Cli_WithClassDeclaration_DocumentsClassAndMethods()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "user-service.ts");
        var content = @"export class UserService {
  getUser(id: string): User {
    return null as any;
  }
}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("User Service.");
        result.Should().Contain("Gets the user.");
        result.Should().Contain("@param {string} id");
        result.Should().Contain("export class UserService");
        // Exactly two blocks: the class and its one method.
        (result.Split("/**").Length - 1).Should().Be(2);
    }

    [Fact]
    public async Task Cli_WithNamespacedDeclarations_DocumentsNested()
    {
        // Arrange: declarations nested in a namespace must be documented end-to-end
        // (regression: the AST sidecar previously never descended into namespaces).
        var filePath = Path.Combine(_testDirectory, "api.ts");
        var content = @"export namespace Api {
  export enum Status {
    Ok,
    Error,
  }

  export class Client {
    send(): void {}
  }
}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("Status.");
        result.Should().Contain("Client.");
        result.Should().Contain("Sends.");
    }

    [Fact]
    public async Task Cli_WithDecoratedClassAlreadyDocumented_DoesNotDuplicate()
    {
        // Arrange: an existing JSDoc between a decorator and the class (the common
        // Angular placement) must be detected so no duplicate comment is inserted.
        var filePath = Path.Combine(_testDirectory, "decorated.ts");
        var content = @"@Component({})
/**
 * The existing widget doc.
 */
export class Widget {
  ping(): void {}
}";
        await File.WriteAllTextAsync(filePath, content);

        // Act
        var exitCode = await RunCliAsync(filePath);

        // Assert
        exitCode.Should().Be(0);
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("The existing widget doc.");
        // The class already has a doc; only its method earns a new block.
        result.Should().NotContain("Widget.");
        (result.Split("/**").Length - 1).Should().Be(2); // existing class doc + ping
    }

    [Fact]
    public async Task Cli_WithNonExistentPath_ReturnsError()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.ts");

        // Act
        var exitCode = await RunCliAsync(nonExistentPath);

        // Assert
        exitCode.Should().Be(1);
    }

    private async Task<int> RunCliAsync(string path)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliPath}\" \"{path}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();
        
        await process.WaitForExitAsync();

        return process.ExitCode;
    }

    private static bool NodeAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null)
            {
                return false;
            }
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
