using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TsCommentify.Cli.Services;

var rootCommand = new RootCommand("TsCommentify - Add missing comments to TypeScript functions");

var pathArgument = new Argument<string>(
    name: "path",
    description: "Path to a TypeScript file or directory containing TypeScript files");

rootCommand.AddArgument(pathArgument);

// Optional annotation flags. When set, the corresponding JSDoc modifier tag is
// appended to every generated *top-level* declaration comment
// (function/class/interface/enum/type) — not to individual members. Flags are
// combinable; the tags are emitted in a fixed, canonical order.
var deprecatedOption = new Option<bool>(
    new[] { "--deprecated", "-d" },
    "Add an @deprecated tag to generated top-level declaration comments.");
var obsoleteOption = new Option<bool>(
    new[] { "--obsolete", "-o" },
    "Add an @obsolete tag to generated top-level declaration comments.");
var internalOption = new Option<bool>(
    new[] { "--internal", "-i" },
    "Add an @internal tag to generated top-level declaration comments.");
var publicApiOption = new Option<bool>(
    new[] { "--public-api", "--publicApi", "-p" },
    "Add an @publicApi tag to generated top-level declaration comments.");

rootCommand.AddOption(deprecatedOption);
rootCommand.AddOption(obsoleteOption);
rootCommand.AddOption(internalOption);
rootCommand.AddOption(publicApiOption);

rootCommand.SetHandler(async (string path, bool deprecated, bool obsolete, bool markInternal, bool publicApi) =>
{
    // Canonical tag order (deprecation first, then visibility), independent of the
    // order the flags were passed on the command line.
    var annotationTags = new List<string>();
    if (deprecated) annotationTags.Add("@deprecated");
    if (obsolete) annotationTags.Add("@obsolete");
    if (markInternal) annotationTags.Add("@internal");
    if (publicApi) annotationTags.Add("@publicApi");

    // Build configuration
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    // Setup dependency injection
    var services = new ServiceCollection();
    
    // Add logging
    services.AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });

    // Register services.
    // Parser selection: prefer the Node/TypeScript-AST sidecar (compiler-grade
    // parsing); fall back to the in-process regex parser when node is unavailable.
    // Override with TSCOMMENTIFY_PARSER or config "Parser:Engine" = regex|sidecar|auto.
    services.AddSingleton<TypeScriptParser>();
    services.AddSingleton<ITypeScriptParser>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var engine = Environment.GetEnvironmentVariable("TSCOMMENTIFY_PARSER")
                     ?? config["Parser:Engine"]
                     ?? "auto";

        if (!string.Equals(engine, "regex", StringComparison.OrdinalIgnoreCase))
        {
            var sidecar = SidecarTypeScriptParser.TryCreate(
                loggerFactory.CreateLogger<SidecarTypeScriptParser>());
            if (sidecar != null)
            {
                return sidecar;
            }
            if (string.Equals(engine, "sidecar", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Parser engine 'sidecar' was requested but the Node sidecar is unavailable (is 'node' on PATH?).");
            }
            loggerFactory.CreateLogger<Program>().LogWarning(
                "Node sidecar unavailable; falling back to the regex parser (reduced accuracy).");
        }

        return sp.GetRequiredService<TypeScriptParser>();
    });
    services.AddSingleton<ICommentGenerator, CommentGenerator>();
    services.AddSingleton<IFileProcessor, FileProcessor>();
    services.AddSingleton<IConfiguration>(configuration);

    // Annotation tags selected via CLI flags; consumed by CommentGenerator.
    services.AddSingleton(new CommentAnnotationOptions { Tags = annotationTags });

    // Build service provider
    using var serviceProvider = services.BuildServiceProvider();
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
    var fileProcessor = serviceProvider.GetRequiredService<IFileProcessor>();

    try
    {
        logger.LogInformation("TsCommentify starting...");
        logger.LogInformation("Processing path: {Path}", path);

        // Resolve the path
        var fullPath = Path.GetFullPath(path);

        if (Directory.Exists(fullPath))
        {
            await fileProcessor.ProcessDirectoryAsync(fullPath);
        }
        else if (File.Exists(fullPath))
        {
            await fileProcessor.ProcessFileAsync(fullPath);
        }
        else
        {
            logger.LogError("Path not found: {Path}", fullPath);
            Environment.Exit(1);
        }

        logger.LogInformation("TsCommentify completed successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while processing");
        Environment.Exit(1);
    }
}, pathArgument, deprecatedOption, obsoleteOption, internalOption, publicApiOption);

return await rootCommand.InvokeAsync(args);
