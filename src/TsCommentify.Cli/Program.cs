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

rootCommand.SetHandler(async (string path) =>
{
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
}, pathArgument);

return await rootCommand.InvokeAsync(args);
