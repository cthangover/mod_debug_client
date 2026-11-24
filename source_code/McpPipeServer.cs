using System.IO.Pipes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cthangover.Mods.DebugClient.Mcp;

/// <summary>Owns the same-user pipe and forwards MCP tools to the kernel without loading game APIs in the adapter.</summary>
public sealed class McpPipeServer
{
    private const int MaxResponseBytes = 256 * 1024;
    private static readonly JsonSerializerOptions TextOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly Func<JsonObject, CancellationToken, Task<JsonObject>> dispatch;
    private readonly Action<string> logWarning;
    private readonly CancellationTokenSource stop = new();
    private readonly Task serving;
    private readonly Lazy<Task> stopping;
    private readonly NamedPipeServerStream pipe;

    /// <summary>Process-addressed pipe name used by the stdio relay.</summary>
    public string PipeName { get; } = $"cthangover-mcp-{Environment.ProcessId}";

    /// <summary>Opens the endpoint first so a bind failure reaches the mod initializer before it announces readiness.</summary>
    public McpPipeServer(Func<JsonObject, CancellationToken, Task<JsonObject>> dispatch, Action<string> logWarning)
    {
        this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        this.logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
        pipe = OpenPipe();
        serving = Task.Run(ServeAsync);
        stopping = new Lazy<Task>(StopCoreAsync);
    }

    /// <summary>Closes the pipe before returning and exposes the worker's asynchronous drain to application shutdown.</summary>
    public Task StopAsync() => stopping.Value;

    private async Task StopCoreAsync()
    {
        var cancellations = stop.CancelAsync();
        pipe.Dispose();
        try
        {
            await cancellations.ConfigureAwait(false);
        }
        catch (AggregateException error)
        {
            logWarning("MCP cancellation callback failed: " + error.GetBaseException().Message);
        }
        finally
        {
            await serving.ConfigureAwait(false);
            stop.Dispose();
        }
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    // On Unix the runtime may accept a socket before checking its user ID.
                    continue;
                }

                try
                {
                    await RunSessionAsync(stop.Token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The client disconnected during the session.
                }
                catch (Exception error) when (!stop.IsCancellationRequested)
                {
                    logWarning("MCP session failed: " + error.Message);
                }

                if (!stop.IsCancellationRequested)
                    pipe.Disconnect();
            }
        }
        catch (Exception error) when (stop.IsCancellationRequested
            && error is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            // Closing the pipe wakes any pending accept or read.
        }
        catch (Exception error)
        {
            logWarning("MCP listener stopped: " + error.Message);
        }
        finally
        {
            pipe.Dispose();
        }
    }

    private NamedPipeServerStream OpenPipe() => new(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task RunSessionAsync(CancellationToken cancellation)
    {
        using var session = new PipeSessionStream(pipe);
        var transport = new StreamServerTransport(session, session, "cthangover-debug-client");
        await using var server = McpServer.Create(transport, new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "cthangover-debug-client", Version = "0.1.0" },
            Handlers = new McpServerHandlers { ListToolsHandler = McpCommandTools.List, CallToolHandler = CallToolAsync }
        });
        await server.RunAsync(cancellation).ConfigureAwait(false);
    }

    private async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stop.Token);
        try
        {
            var request = McpCommandTools.CreateRequest(context.Params);
            linked.Token.ThrowIfCancellationRequested();
            // Releasing this wait is independent of the game thread completing a command.
            var reply = await dispatch(request, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
            return ToToolResult(reply);
        }
        catch (ArgumentException error)
        {
            return Failure(error.Message);
        }
    }

    private static CallToolResult ToToolResult(JsonObject reply)
    {
        var content = reply.ToJsonString(TextOptions);
        if (Encoding.UTF8.GetByteCount(content) > MaxResponseBytes)
            return Failure("Response exceeds 256 KiB. Narrow the query or request fewer rows.");

        using var structured = JsonDocument.Parse(content);
        return new CallToolResult
        {
            IsError = reply["ok"]?.GetValue<bool>() != true,
            StructuredContent = structured.RootElement.Clone(),
            Content = [new TextContentBlock { Text = content }]
        };
    }

    private static CallToolResult Failure(string message)
        => ToToolResult(new JsonObject { ["ok"] = false, ["error"] = message });
}
