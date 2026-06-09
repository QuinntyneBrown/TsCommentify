using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace TsCommentify.Cli.Services;

/// <summary>
/// <see cref="ITypeScriptParser"/> backed by the Node/TypeScript-AST sidecar.
/// Because it drives the real TypeScript compiler it sees whole declarations
/// (not single physical lines): multi-line signatures, function-typed returns,
/// brace-on-next-line methods, and multi-line generic arrows are all parsed
/// correctly, and existing-comment detection uses ts.getLeadingCommentRanges
/// instead of a line heuristic. A file with syntax errors is skipped rather than
/// mis-commented.
/// </summary>
public sealed class SidecarTypeScriptParser : ITypeScriptParser, IDisposable
{
    private readonly SidecarClient _client;
    private readonly ILogger _logger;
    private int _nextId = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private SidecarTypeScriptParser(SidecarClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Resolves node + the bundled sidecar and verifies the handshake. Returns
    /// <c>null</c> (so the caller can fall back to the regex parser) when node is
    /// not on PATH, the sidecar is missing, or the handshake fails.
    /// </summary>
    public static SidecarTypeScriptParser? TryCreate(ILogger logger, string nodePath = "node")
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "sidecar", "sidecar.js");
        if (!File.Exists(scriptPath))
        {
            logger.LogDebug("Sidecar script not found at {ScriptPath}", scriptPath);
            return null;
        }

        SidecarClient? client = null;
        try
        {
            client = new SidecarClient(nodePath, scriptPath);
            var response = client.Send("{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"ping\"}");
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.String &&
                result.GetString() == "pong")
            {
                logger.LogInformation("Using Node/TypeScript-AST sidecar parser (node + typescript).");
                return new SidecarTypeScriptParser(client, logger);
            }

            logger.LogWarning("Sidecar handshake returned an unexpected response: {Response}", response);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Sidecar unavailable ({Message})", ex.Message);
        }

        client?.Dispose();
        return null;
    }

    public IEnumerable<FunctionInfo> ParseFunctions(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return Enumerable.Empty<FunctionInfo>();
        }

        var id = _nextId++;
        var request = JsonSerializer.Serialize(new SidecarRequest(id, "parse", new ParseParams(filePath)), JsonOptions);

        ParseResult? result;
        try
        {
            var response = _client.Send(request);
            result = JsonSerializer.Deserialize<SidecarResponse>(response, JsonOptions)?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sidecar failed to parse {FilePath}; skipping file", filePath);
            return Enumerable.Empty<FunctionInfo>();
        }

        if (result == null)
        {
            return Enumerable.Empty<FunctionInfo>();
        }

        if (result.Errors.Count > 0)
        {
            // The real compiler found syntax errors — refuse to edit rather than
            // risk inserting comments into a position the file doesn't actually have.
            var first = result.Errors[0];
            _logger.LogWarning("Skipping {FilePath}: TypeScript reported a syntax error at line {Line}: {Message}",
                filePath, first.Line, first.Message);
            return Enumerable.Empty<FunctionInfo>();
        }

        var functions = result.Declarations.Select(d => new FunctionInfo(
            Name: d.Name,
            LineNumber: d.Line,
            Content: string.Empty,
            Parameters: d.Params.Select(p => new ParameterInfo(p.Name, p.Type)).ToList(),
            ReturnType: d.ReturnType,
            HasComment: d.HasComment,
            Kind: string.IsNullOrEmpty(d.Kind) ? "function" : d.Kind)).ToList();

        _logger.LogInformation("Found {Count} declarations in {FilePath} (sidecar)", functions.Count, filePath);
        return functions;
    }

    public void Dispose() => _client.Dispose();

    // ---- JSON-RPC DTOs -----------------------------------------------------

    private sealed record SidecarRequest(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("params")] ParseParams Params)
    {
        [JsonPropertyName("jsonrpc")] public string JsonRpc => "2.0";
    }

    private sealed record ParseParams([property: JsonPropertyName("file")] string File);

    private sealed class SidecarResponse
    {
        public ParseResult? Result { get; set; }
    }

    private sealed class ParseResult
    {
        public List<Declaration> Declarations { get; set; } = new();
        public List<SidecarError> Errors { get; set; } = new();
    }

    private sealed class Declaration
    {
        public string Name { get; set; } = string.Empty;
        public int Line { get; set; }
        public List<SidecarParam> Params { get; set; } = new();
        public string? ReturnType { get; set; }
        public bool HasComment { get; set; }

        // "function" | "class" | "interface" | "enum" | "type" | "method" |
        // "property" | "enum-member" — emitted by sidecar.js for every declaration.
        public string Kind { get; set; } = "function";
    }

    private sealed class SidecarParam
    {
        public string Name { get; set; } = string.Empty;
        public string? Type { get; set; }
    }

    private sealed class SidecarError
    {
        public int Line { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
