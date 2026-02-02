using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TsCommentify.Cli.Services;

namespace TsCommentify.Tests.Services;

public class FileProcessorIgnoreTests : IDisposable
{
    private readonly Mock<ITypeScriptParser> _parserMock;
    private readonly Mock<ICommentGenerator> _generatorMock;
    private readonly Mock<ILogger<FileProcessor>> _loggerMock;
    private readonly string _testDirectory;

    public FileProcessorIgnoreTests()
    {
        _parserMock = new Mock<ITypeScriptParser>();
        _generatorMock = new Mock<ICommentGenerator>();
        _loggerMock = new Mock<ILogger<FileProcessor>>();
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

    private FileProcessor CreateProcessorWithIgnorePatterns(List<string> ignorePatterns)
    {
        var configData = new Dictionary<string, string?>();
        for (int i = 0; i < ignorePatterns.Count; i++)
        {
            configData[$"FileProcessor:IgnorePatterns:{i}"] = ignorePatterns[i];
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new FileProcessor(_parserMock.Object, _generatorMock.Object, _loggerMock.Object, configuration);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_IgnoresSpecFiles_ByDefault()
    {
        // Arrange
        var processor = CreateProcessorWithIgnorePatterns(new List<string> { "*.spec.ts", "*.test.ts" });
        
        var regularFile = Path.Combine(_testDirectory, "component.ts");
        var specFile = Path.Combine(_testDirectory, "component.spec.ts");
        var testFile = Path.Combine(_testDirectory, "component.test.ts");

        File.WriteAllText(regularFile, "function test() {}");
        File.WriteAllText(specFile, "function testSpec() {}");
        File.WriteAllText(testFile, "function testTest() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(regularFile), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(specFile), Times.Never);
        _parserMock.Verify(p => p.ParseFunctions(testFile), Times.Never);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_IgnoresTestFiles_ByDefault()
    {
        // Arrange
        var processor = CreateProcessorWithIgnorePatterns(new List<string> { "*.spec.ts", "*.test.ts" });
        
        var regularFile = Path.Combine(_testDirectory, "service.ts");
        var testFile = Path.Combine(_testDirectory, "service.test.ts");

        File.WriteAllText(regularFile, "function service() {}");
        File.WriteAllText(testFile, "function testService() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(regularFile), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(testFile), Times.Never);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithCustomIgnorePattern_IgnoresMatchingFiles()
    {
        // Arrange
        var processor = CreateProcessorWithIgnorePatterns(new List<string> { "*.spec.ts", "*.test.ts", "*.mock.ts" });
        
        var regularFile = Path.Combine(_testDirectory, "service.ts");
        var mockFile = Path.Combine(_testDirectory, "service.mock.ts");

        File.WriteAllText(regularFile, "function service() {}");
        File.WriteAllText(mockFile, "function mockService() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(regularFile), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(mockFile), Times.Never);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_UsesDefaultIgnorePatterns_WhenNoConfigProvided()
    {
        // Arrange - no configuration provided, should use defaults
        var configuration = new ConfigurationBuilder().Build();
        var processor = new FileProcessor(_parserMock.Object, _generatorMock.Object, _loggerMock.Object, configuration);
        
        var regularFile = Path.Combine(_testDirectory, "service.ts");
        var specFile = Path.Combine(_testDirectory, "service.spec.ts");
        var testFile = Path.Combine(_testDirectory, "service.test.ts");

        File.WriteAllText(regularFile, "function service() {}");
        File.WriteAllText(specFile, "function specService() {}");
        File.WriteAllText(testFile, "function testService() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await processor.ProcessDirectoryAsync(_testDirectory);

        // Assert - spec and test files should be ignored by default
        _parserMock.Verify(p => p.ParseFunctions(regularFile), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(specFile), Times.Never);
        _parserMock.Verify(p => p.ParseFunctions(testFile), Times.Never);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithWildcardPattern_MatchesCorrectly()
    {
        // Arrange
        var processor = CreateProcessorWithIgnorePatterns(new List<string> { "test-*.ts" });
        
        var regularFile = Path.Combine(_testDirectory, "component.ts");
        var testFile1 = Path.Combine(_testDirectory, "test-utils.ts");
        var testFile2 = Path.Combine(_testDirectory, "test-helpers.ts");

        File.WriteAllText(regularFile, "function component() {}");
        File.WriteAllText(testFile1, "function testUtils() {}");
        File.WriteAllText(testFile2, "function testHelpers() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(regularFile), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(testFile1), Times.Never);
        _parserMock.Verify(p => p.ParseFunctions(testFile2), Times.Never);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_IgnoresSpecFilesInSubdirectories()
    {
        // Arrange
        var processor = CreateProcessorWithIgnorePatterns(new List<string> { "*.spec.ts", "*.test.ts" });
        
        var subDir = Path.Combine(_testDirectory, "src");
        Directory.CreateDirectory(subDir);

        var regularFile = Path.Combine(subDir, "component.ts");
        var specFile = Path.Combine(subDir, "component.spec.ts");

        File.WriteAllText(regularFile, "function component() {}");
        File.WriteAllText(specFile, "function testComponent() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(regularFile), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(specFile), Times.Never);
    }
}
