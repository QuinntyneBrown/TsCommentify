using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TsCommentify.Cli.Services;

namespace TsCommentify.Tests.Services;

public class FileProcessorTests : IDisposable
{
    private readonly Mock<ITypeScriptParser> _parserMock;
    private readonly Mock<ICommentGenerator> _generatorMock;
    private readonly Mock<ILogger<FileProcessor>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly FileProcessor _processor;
    private readonly string _testDirectory;

    public FileProcessorTests()
    {
        _parserMock = new Mock<ITypeScriptParser>();
        _generatorMock = new Mock<ICommentGenerator>();
        _loggerMock = new Mock<ILogger<FileProcessor>>();
        _configurationMock = new Mock<IConfiguration>();
        
        // Setup default configuration
        var configSection = new Mock<IConfigurationSection>();
        configSection.Setup(x => x.GetChildren()).Returns(new List<IConfigurationSection>());
        _configurationMock.Setup(x => x.GetSection(It.IsAny<string>())).Returns(configSection.Object);
        
        _processor = new FileProcessor(_parserMock.Object, _generatorMock.Object, _loggerMock.Object, _configurationMock.Object);
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
    public async Task ProcessFileAsync_WithNonExistentFile_LogsWarning()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "nonexistent.ts");

        // Act
        await _processor.ProcessFileAsync(filePath);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessFileAsync_WithNonTypeScriptFile_LogsWarning()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.txt");
        File.WriteAllText(filePath, "test content");

        // Act
        await _processor.ProcessFileAsync(filePath);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessFileAsync_WithAllFunctionsCommented_DoesNotModifyFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.ts");
        var content = "function test() {}";
        File.WriteAllText(filePath, content);

        var functions = new List<FunctionInfo>
        {
            new FunctionInfo("test", 1, content, new List<ParameterInfo>(), null, HasComment: true)
        };

        _parserMock.Setup(p => p.ParseFunctions(filePath))
            .Returns(functions);

        // Act
        await _processor.ProcessFileAsync(filePath);

        // Assert
        _generatorMock.Verify(g => g.GenerateComment(It.IsAny<FunctionInfo>()), Times.Never);
        File.ReadAllText(filePath).Should().Be(content);
    }

    [Fact]
    public async Task ProcessFileAsync_WithUncommentedFunction_AddsComment()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.ts");
        var content = "function test() {}";
        File.WriteAllText(filePath, content);

        var function = new FunctionInfo("test", 1, content, new List<ParameterInfo>(), null, HasComment: false);
        var functions = new List<FunctionInfo> { function };

        _parserMock.Setup(p => p.ParseFunctions(filePath))
            .Returns(functions);

        _generatorMock.Setup(g => g.GenerateComment(function))
            .Returns("/**\n * Test function\n */");

        // Act
        await _processor.ProcessFileAsync(filePath);

        // Assert
        _generatorMock.Verify(g => g.GenerateComment(function), Times.Once);
        var result = File.ReadAllText(filePath);
        result.Should().Contain("/**");
        result.Should().Contain("* Test function");
        result.Should().Contain("*/");
    }

    [Fact]
    public async Task ProcessFileAsync_PreservesIndentation()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.ts");
        var content = "  function test() {}";
        File.WriteAllText(filePath, content);

        var function = new FunctionInfo("test", 1, content, new List<ParameterInfo>(), null, HasComment: false);
        var functions = new List<FunctionInfo> { function };

        _parserMock.Setup(p => p.ParseFunctions(filePath))
            .Returns(functions);

        _generatorMock.Setup(g => g.GenerateComment(function))
            .Returns("/**\n * Test function\n */");

        // Act
        await _processor.ProcessFileAsync(filePath);

        // Assert
        var result = File.ReadAllText(filePath);
        result.Should().Contain("  /**");
        result.Should().Contain("  * Test function");
        result.Should().Contain("  */");
    }

    [Fact]
    public async Task ProcessFileAsync_WithMultipleUncommentedFunctions_AddsAllComments()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.ts");
        var content = @"function test1() {}
function test2() {}";
        File.WriteAllText(filePath, content);

        var function1 = new FunctionInfo("test1", 1, "function test1() {}", new List<ParameterInfo>(), null, HasComment: false);
        var function2 = new FunctionInfo("test2", 2, "function test2() {}", new List<ParameterInfo>(), null, HasComment: false);
        var functions = new List<FunctionInfo> { function1, function2 };

        _parserMock.Setup(p => p.ParseFunctions(filePath))
            .Returns(functions);

        _generatorMock.Setup(g => g.GenerateComment(function1))
            .Returns("/**\n * Test1 function\n */");

        _generatorMock.Setup(g => g.GenerateComment(function2))
            .Returns("/**\n * Test2 function\n */");

        // Act
        await _processor.ProcessFileAsync(filePath);

        // Assert
        _generatorMock.Verify(g => g.GenerateComment(It.IsAny<FunctionInfo>()), Times.Exactly(2));
        var result = File.ReadAllText(filePath);
        result.Should().Contain("Test1 function");
        result.Should().Contain("Test2 function");
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithNonExistentDirectory_LogsError()
    {
        // Arrange
        var dirPath = Path.Combine(_testDirectory, "nonexistent");

        // Act
        await _processor.ProcessDirectoryAsync(dirPath);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithTypeScriptFiles_ProcessesAll()
    {
        // Arrange
        var file1 = Path.Combine(_testDirectory, "test1.ts");
        var file2 = Path.Combine(_testDirectory, "test2.tsx");
        File.WriteAllText(file1, "function test1() {}");
        File.WriteAllText(file2, "function test2() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await _processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(It.IsAny<string>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task ProcessDirectoryAsync_ExcludesNodeModulesAndDist()
    {
        // Arrange
        var nodeModulesDir = Path.Combine(_testDirectory, "node_modules");
        var distDir = Path.Combine(_testDirectory, "dist");
        Directory.CreateDirectory(nodeModulesDir);
        Directory.CreateDirectory(distDir);

        var validFile = Path.Combine(_testDirectory, "test.ts");
        var nodeModulesFile = Path.Combine(nodeModulesDir, "lib.ts");
        var distFile = Path.Combine(distDir, "output.ts");

        File.WriteAllText(validFile, "function test() {}");
        File.WriteAllText(nodeModulesFile, "function lib() {}");
        File.WriteAllText(distFile, "function output() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await _processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(validFile), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(nodeModulesFile), Times.Never);
        _parserMock.Verify(p => p.ParseFunctions(distFile), Times.Never);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_ExcludesDefinitionFiles()
    {
        // Arrange
        var regularFile = Path.Combine(_testDirectory, "test.ts");
        var definitionFile = Path.Combine(_testDirectory, "types.d.ts");

        File.WriteAllText(regularFile, "function test() {}");
        File.WriteAllText(definitionFile, "declare function lib(): void;");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await _processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(regularFile), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(definitionFile), Times.Never);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithNestedDirectories_ProcessesRecursively()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "src");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(_testDirectory, "test1.ts");
        var file2 = Path.Combine(subDir, "test2.ts");

        File.WriteAllText(file1, "function test1() {}");
        File.WriteAllText(file2, "function test2() {}");

        _parserMock.Setup(p => p.ParseFunctions(It.IsAny<string>()))
            .Returns(new List<FunctionInfo>());

        // Act
        await _processor.ProcessDirectoryAsync(_testDirectory);

        // Assert
        _parserMock.Verify(p => p.ParseFunctions(file1), Times.Once);
        _parserMock.Verify(p => p.ParseFunctions(file2), Times.Once);
    }
}
