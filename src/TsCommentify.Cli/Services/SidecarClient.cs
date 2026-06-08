using System.Diagnostics;
using System.Text;

namespace TsCommentify.Cli.Services;

/// <summary>
/// Newline-delimited JSON-RPC transport to the Node/TypeScript-AST sidecar
/// (mirrors the SurfaceQ.Sidecar.Node mechanism). One long-lived node process is
/// spawned per client and reused for every file in a run, so the ~300ms node +
/// TypeScript-compiler startup is paid once, not per file.
/// </summary>
public sealed class SidecarClient : IDisposable
{
    // The sidecar reads source files as UTF-8 and writes UTF-8 JSON to stdout, so
    // both stdio streams must be UTF-8. Without this, .NET falls back to the
    // console's OEM code page on Windows (e.g. CP850), which mangles every
    // non-ASCII character a source comment carries. BOM-less on purpose: a BOM on
    // stdin would prefix the first request line and break the sidecar's JSON.parse.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;

    public SidecarClient(string nodePath, string scriptPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = nodePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            // Module resolution starts from the script's own directory, so a
            // bundled sidecar/node_modules/typescript is found regardless of cwd.
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add(scriptPath);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start node sidecar");
        _process = process;
        _stdin = process.StandardInput;
        _stdout = process.StandardOutput;

        // Drain stderr in the background so a chatty child can never fill the pipe
        // buffer and deadlock the request/response loop on stdout.
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();
    }

    /// <summary>Send one JSON request line and read exactly one JSON response line.</summary>
    public string Send(string jsonLine)
    {
        _stdin.WriteLine(jsonLine);
        _stdin.Flush();
        return _stdout.ReadLine()
            ?? throw new InvalidOperationException("sidecar closed stdout without responding");
    }

    public void Dispose()
    {
        try { _stdin.Close(); } catch { }
        try
        {
            if (!_process.WaitForExit(2000))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
        _process.Dispose();
    }
}
