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

    // Register services
    services.AddSingleton<ITypeScriptParser, TypeScriptParser>();
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
