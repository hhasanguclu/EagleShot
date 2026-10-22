using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace EagleShot.Core;

/// <summary>
/// Keeps one EagleShot per user. A second launch forwards its arguments
/// (e.g. "--region") to the running instance over a named pipe and exits.
/// </summary>
public static class SingleInstance
{
    private static readonly string PipeName = $"EagleShot-{Environment.UserName}";
    private static CancellationTokenSource? _cts;

    public static bool TrySendToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(300);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(string.Join('\u001f', args));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void StartServer(Action<string[]> onArgs)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(token) ?? "";
                    onArgs(line.Length == 0 ? Array.Empty<string>() : line.Split('\u001f'));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Another instance owns the pipe, or a client disconnected early
                    System.Diagnostics.Debug.WriteLine($"Single instance server: {ex.Message}");
                    await Task.Delay(1000, token).ContinueWith(_ => { });
                }
            }
        }, token);
    }

    public static void StopServer() => _cts?.Cancel();
}
