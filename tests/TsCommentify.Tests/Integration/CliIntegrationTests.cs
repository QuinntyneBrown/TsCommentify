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
}
