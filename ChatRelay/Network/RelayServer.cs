using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ChatRelay.Models;
using Dalamud.Plugin.Services;

namespace ChatRelay.Network;

public sealed class RelayServer : IRelayTransport
{
    private readonly int port;
    private readonly IPluginLog log;
    private TcpListener? tcpListener;
    private CancellationTokenSource? cts;
    private readonly List<WebSocket> clients = new();
    private readonly object clientsLock = new();
    private bool listening;

    public bool IsConnected => listening;
    public string StatusText { get; private set; } = "Stopped";
    public event Action<ChatRelayMessage>? OnMessageReceived;

    public RelayServer(int port, IPluginLog log)
    {
        this.port = port;
        this.log = log;
    }

    public Task StartAsync(CancellationToken ct)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            listening = true;
            StatusText = $"Listening on port {port}";
            log.Information($"[ChatRelay] Server started on port {port}");
        }
        catch (SocketException ex)
        {
            StatusText = $"Error: {ex.Message}";
            log.Error($"[ChatRelay] Failed to start server: {ex.Message}");
            return Task.CompletedTask;
        }

        _ = AcceptLoopAsync(cts.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listening)
        {
            try
            {
                var tcpClient = await tcpListener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleTcpClientAsync(tcpClient, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    log.Warning($"[ChatRelay] Accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        var stream = tcpClient.GetStream();
        try
        {
            // Read full HTTP upgrade request (until \r\n\r\n)
            var headerBuffer = new System.Collections.Generic.List<byte>(4096);
            var tempBuffer = new byte[1024];
            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(tempBuffer, 0, tempBuffer.Length, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    tcpClient.Close();
                    return;
                }

                for (var i = 0; i < read; i++)
                    headerBuffer.Add(tempBuffer[i]);

                if (Encoding.UTF8.GetString(headerBuffer.ToArray()).Contains("\r\n\r\n"))
                    break;
            }

            var request = Encoding.UTF8.GetString(headerBuffer.ToArray());

            var keyMatch = Regex.Match(request, @"Sec-WebSocket-Key:\s*(\S+)", RegexOptions.IgnoreCase);
            if (!keyMatch.Success)
            {
                tcpClient.Close();
                return;
            }

            var key = keyMatch.Groups[1].Value;
            var acceptKey = Convert.ToBase64String(
                SHA1.HashData(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-5AB9DC11B65A")));

            // Send HTTP 101 Switching Protocols
            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                           "Upgrade: websocket\r\n" +
                           "Connection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
            var responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct).ConfigureAwait(false);

            // Create WebSocket from the upgraded stream
            var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer = true,
                KeepAliveInterval = TimeSpan.FromSeconds(30),
            });

            lock (clientsLock)
                clients.Add(ws);

            int clientCount;
            lock (clientsLock)
                clientCount = clients.Count;
            StatusText = $"Listening on port {port} ({clientCount} client(s))";
            log.Information("[ChatRelay] Client connected");

            await ReceiveLoopAsync(ws, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                log.Warning($"[ChatRelay] Handshake error: {ex.Message}");
        }
        finally
        {
            tcpClient.Close();
        }
    }

    private async Task ReceiveLoopAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var msg = JsonSerializer.Deserialize<ChatRelayMessage>(json);
                        if (msg != null)
                            OnMessageReceived?.Invoke(msg);
                    }
                    catch (JsonException ex)
                    {
                        log.Warning($"[ChatRelay] Malformed JSON: {ex.Message}");
                    }
                }
            }
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        finally
        {
            lock (clientsLock)
                clients.Remove(ws);

            int clientCount;
            lock (clientsLock)
                clientCount = clients.Count;
            StatusText = listening
                ? $"Listening on port {port} ({clientCount} client(s))"
                : "Stopped";

            log.Information("[ChatRelay] Client disconnected");
            ws.Dispose();
        }
    }

    public async Task SendAsync(ChatRelayMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        List<WebSocket> snapshot;
        lock (clientsLock)
            snapshot = new List<WebSocket>(clients);

        foreach (var ws in snapshot)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.SendAsync(segment, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log.Warning($"[ChatRelay] Send error: {ex.Message}");
                }
            }
        }
    }

    public async Task StopAsync()
    {
        cts?.Cancel();
        listening = false;
        StatusText = "Stopping...";

        List<WebSocket> snapshot;
        lock (clientsLock)
        {
            snapshot = new List<WebSocket>(clients);
            clients.Clear();
        }

        foreach (var ws in snapshot)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            finally
            {
                ws.Dispose();
            }
        }

        try
        {
            tcpListener?.Stop();
        }
        catch { }

        tcpListener = null;
        StatusText = "Stopped";
        log.Information("[ChatRelay] Server stopped");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        cts?.Dispose();
    }
}
