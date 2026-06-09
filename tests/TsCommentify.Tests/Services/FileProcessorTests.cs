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

    // ---- Annotation tags on already-commented declarations ----------------

    private FileProcessor ProcessorWithTags(params string[] tags)
        => new(_parserMock.Object, _generatorMock.Object, _loggerMock.Object,
               _configurationMock.Object, new CommentAnnotationOptions { Tags = tags });

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_testDirectory, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ProcessFileAsync_WithFlagAndExistingMultiLineComment_AddsTagToBlock()
    {
        // Arrange
        var content = "/**\n * User Service.\n */\nexport class UserService {}\n";
        var filePath = WriteFile("svc.ts", content);
        var decl = new FunctionInfo("UserService", 4, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "class");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act
        await ProcessorWithTags("@deprecated").ProcessFileAsync(filePath);

        // Assert
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("@deprecated");
        (result.Split("@deprecated").Length - 1).Should().Be(1);
        result.Should().Contain("User Service.");
        result.Should().Contain("export class UserService {}");
        // An update reuses the existing block; the generator is not invoked.
        _generatorMock.Verify(g => g.GenerateComment(It.IsAny<FunctionInfo>()), Times.Never);
    }

    [Fact]
    public async Task ProcessFileAsync_WithFlagAndTagAlreadyPresent_LeavesFileByteForByteUnchanged()
    {
        // Arrange
        var content = "/**\n * User Service.\n *\n * @deprecated\n */\nexport class UserService {}\n";
        var filePath = WriteFile("svc.ts", content);
        var decl = new FunctionInfo("UserService", 6, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "class");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act
        await ProcessorWithTags("@deprecated").ProcessFileAsync(filePath);

        // Assert: idempotent — no duplicate tag, and no needless rewrite.
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Be(content);
        (result.Split("@deprecated").Length - 1).Should().Be(1);
    }

    [Fact]
    public async Task ProcessFileAsync_WithFlagAndSingleLineComment_ExpandsBlockAndAddsTag()
    {
        // Arrange
        var content = "/** Tone. */\nexport type Tone = 'a' | 'b';\n";
        var filePath = WriteFile("tone.ts", content);
        var decl = new FunctionInfo("Tone", 2, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "type");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act
        await ProcessorWithTags("@internal").ProcessFileAsync(filePath);

        // Assert: single-line block is expanded to multi-line with the tag.
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("@internal");
        result.Should().Contain(" * Tone.");
        result.Should().Contain("export type Tone = 'a' | 'b';");
        // No longer a one-line block.
        result.Should().NotContain("/** Tone. */");
    }

    [Fact]
    public async Task ProcessFileAsync_WithFlagAndMultipleMissingTags_AddsAllInListOrder()
    {
        // Arrange
        var content = "/**\n * User Service.\n */\nexport class UserService {}\n";
        var filePath = WriteFile("svc.ts", content);
        var decl = new FunctionInfo("UserService", 4, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "class");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act
        await ProcessorWithTags("@deprecated", "@internal").ProcessFileAsync(filePath);

        // Assert
        var result = await File.ReadAllTextAsync(filePath);
        result.IndexOf("@deprecated", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("@internal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessFileAsync_WithFlagAndCommentHasParam_InsertsTagBeforeParam()
    {
        // Arrange: an existing function comment that already documents a parameter.
        var content = "/**\n * Gets the user.\n *\n * @param {string} id - The id.\n */\nexport function getUser(id: string): User {}\n";
        var filePath = WriteFile("fn.ts", content);
        var decl = new FunctionInfo("getUser", 6, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "function");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act
        await ProcessorWithTags("@deprecated").ProcessFileAsync(filePath);

        // Assert: the tag lands after the description and before the existing @param.
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain("@deprecated");
        result.IndexOf("@deprecated", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("@param", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessFileAsync_WithFlagAndTagOnlyMentionedInProse_StillAddsRealTag()
    {
        // Arrange: the description mentions "@deprecated" mid-sentence; that is not an
        // actual tag, so the real tag must still be added.
        var content = "/**\n * Mentions @deprecated in prose only.\n */\nexport class Legacy {}\n";
        var filePath = WriteFile("prose.ts", content);
        var decl = new FunctionInfo("Legacy", 4, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "class");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act
        await ProcessorWithTags("@deprecated").ProcessFileAsync(filePath);

        // Assert: a real tag line is added (the prose mention did not suppress it).
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Contain(" * @deprecated");
        (result.Split("@deprecated").Length - 1).Should().Be(2); // prose mention + real tag
    }

    [Fact]
    public async Task ProcessFileAsync_WithFlagAndCommentedMember_LeavesMemberUntouched()
    {
        // Arrange: a commented member (kind=method) must NOT receive a tag.
        var content = "/**\n * Gets the user.\n */\ngetUser(): void {}\n";
        var filePath = WriteFile("member.ts", content);
        var decl = new FunctionInfo("getUser", 4, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "method");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act
        await ProcessorWithTags("@deprecated").ProcessFileAsync(filePath);

        // Assert
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Be(content);
        result.Should().NotContain("@deprecated");
    }

    [Fact]
    public async Task ProcessFileAsync_WithFlagAndNonJsDocComment_LeavesCommentUntouched()
    {
        // Arrange: a // line comment is not a JSDoc block; a @tag there is meaningless,
        // so it is left as-is rather than corrupted.
        var content = "// legacy service\nexport class Legacy {}\n";
        var filePath = WriteFile("legacy.ts", content);
        var decl = new FunctionInfo("Legacy", 2, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "class");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act
        await ProcessorWithTags("@deprecated").ProcessFileAsync(filePath);

        // Assert
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Be(content);
        result.Should().NotContain("@deprecated");
    }

    [Fact]
    public async Task ProcessFileAsync_WithoutFlag_DoesNotTouchExistingComment()
    {
        // Arrange: without an annotation flag, a documented top-level declaration is
        // left exactly as before (no update path).
        var content = "/**\n * User Service.\n */\nexport class UserService {}\n";
        var filePath = WriteFile("svc.ts", content);
        var decl = new FunctionInfo("UserService", 4, "", new List<ParameterInfo>(), null,
            HasComment: true, Kind: "class");
        _parserMock.Setup(p => p.ParseFunctions(filePath)).Returns(new List<FunctionInfo> { decl });

        // Act: _processor has no annotation tags configured.
        await _processor.ProcessFileAsync(filePath);

        // Assert
        var result = await File.ReadAllTextAsync(filePath);
        result.Should().Be(content);
    }
}
