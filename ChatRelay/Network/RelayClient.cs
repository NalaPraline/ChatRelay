using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ChatRelay.Models;
using Dalamud.Plugin.Services;

namespace ChatRelay.Network;

public sealed class RelayClient : IRelayTransport
{
    private readonly string host;
    private readonly int port;
    private readonly IPluginLog log;
    private ClientWebSocket? ws;
    private CancellationTokenSource? cts;
    private bool intentionalStop;

    public bool IsConnected => ws?.State == WebSocketState.Open;
    public string StatusText { get; private set; } = "Disconnected";
    public event Action<ChatRelayMessage>? OnMessageReceived;

    public RelayClient(string host, int port, IPluginLog log)
    {
        this.host = host;
        this.port = port;
        this.log = log;
    }

    public Task StartAsync(CancellationToken ct)
    {
        intentionalStop = false;
        cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ConnectLoopAsync(cts.Token);
        return Task.CompletedTask;
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        var delay = 2000;
        const int maxDelay = 30000;

        while (!ct.IsCancellationRequested && !intentionalStop)
        {
            try
            {
                ws?.Dispose();
                ws = new ClientWebSocket();
                var uri = new Uri($"ws://{host}:{port}/");
                StatusText = $"Connecting to {uri}...";
                log.Information($"[ChatRelay] Connecting to {uri}");

                await ws.ConnectAsync(uri, ct).ConfigureAwait(false);
                StatusText = $"Connected to {host}:{port}";
                log.Information($"[ChatRelay] Connected to {host}:{port}, State={ws.State}");
                delay = 2000;

                await ReceiveLoopAsync(ct).ConfigureAwait(false);
                log.Information($"[ChatRelay] Receive loop ended, State={ws?.State}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (intentionalStop || ct.IsCancellationRequested)
                    break;

                StatusText = $"Disconnected - retrying in {delay / 1000}s ({ex.Message})";
                log.Warning($"[ChatRelay] Connection lost: {ex.GetType().Name}: {ex.Message}. Retrying in {delay / 1000}s");
                if (ex.InnerException != null)
                    log.Warning($"[ChatRelay] Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");

                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                delay = Math.Min(delay * 2, maxDelay);
            }
        }

        StatusText = "Disconnected";
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
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

    public async Task SendAsync(ChatRelayMessage message, CancellationToken ct)
    {
        if (ws?.State != WebSocketState.Open)
            return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Warning($"[ChatRelay] Send error: {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        intentionalStop = true;
        cts?.Cancel();

        if (ws?.State == WebSocketState.Open)
        {
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client stopping", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }

        ws?.Dispose();
        ws = null;
        StatusText = "Disconnected";
        log.Information("[ChatRelay] Client stopped");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        cts?.Dispose();
    }
}
