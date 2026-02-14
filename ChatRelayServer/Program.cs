using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatRelayServer;

internal static class Program
{
    private static readonly List<WebSocket> Clients = new();
    private static readonly object Lock = new();
    private static int port = 14777;

    private static async Task Main(string[] args)
    {
        if (args.Length > 0 && int.TryParse(args[0], out var p))
            port = p;

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{port}/");
        listener.Start();
        Console.WriteLine($"[ChatRelayServer] Listening on port {port}");

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(cts.Token);

                if (context.Request.IsWebSocketRequest)
                {
                    _ = HandleWebSocketAsync(context, cts.Token);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }
        finally
        {
            listener.Stop();
            listener.Close();
            Console.WriteLine("[ChatRelayServer] Stopped");
        }
    }

    private static async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken ct)
    {
        var endpoint = context.Request.RemoteEndPoint?.ToString() ?? "unknown";
        WebSocket? ws = null;

        try
        {
            var wsContext = await context.AcceptWebSocketAsync(null);
            ws = wsContext.WebSocket;

            lock (Lock)
                Clients.Add(ws);

            int count;
            lock (Lock)
                count = Clients.Count;
            Console.WriteLine($"[+] Client connected: {endpoint} ({count} total)");

            var buffer = new byte[8192];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var data = new byte[result.Count];
                    Array.Copy(buffer, data, result.Count);
                    _ = BroadcastAsync(data, ws, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"[!] WebSocket error ({endpoint}): {ex.Message} | {ex.WebSocketErrorCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Error ({endpoint}): {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (ws != null)
            {
                lock (Lock)
                    Clients.Remove(ws);
                ws.Dispose();
            }

            int count;
            lock (Lock)
                count = Clients.Count;
            Console.WriteLine($"[-] Client disconnected: {endpoint} ({count} total)");
        }
    }

    private static async Task BroadcastAsync(byte[] data, WebSocket sender, CancellationToken ct)
    {
        var segment = new ArraySegment<byte>(data);
        List<WebSocket> snapshot;
        lock (Lock)
            snapshot = new List<WebSocket>(Clients);

        foreach (var client in snapshot)
        {
            if (client == sender || client.State != WebSocketState.Open)
                continue;

            try
            {
                await client.SendAsync(segment, WebSocketMessageType.Text, true, ct);
            }
            catch
            {
                // Client will be cleaned up on its receive loop
            }
        }
    }
}
