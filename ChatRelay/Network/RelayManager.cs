using System;
using System.Threading;
using System.Threading.Tasks;
using ChatRelay.Models;
using Dalamud.Plugin.Services;

namespace ChatRelay.Network;

public sealed class RelayManager : IDisposable
{
    private readonly IPluginLog log;
    private IRelayTransport? transport;
    private CancellationTokenSource? cts;

    public bool IsConnected => transport?.IsConnected == true;
    public string StatusText => transport?.StatusText ?? "Disabled";
    public RelayMode ActiveMode { get; private set; } = RelayMode.Disabled;
    public event Action<ChatRelayMessage>? OnRemoteMessage;

    public RelayManager(IPluginLog log)
    {
        this.log = log;
    }

    public async Task StartAsync(RelayMode mode, string host, int port)
    {
        await StopAsync().ConfigureAwait(false);

        cts = new CancellationTokenSource();
        ActiveMode = mode;

        transport = mode switch
        {
            RelayMode.Server => new RelayServer(port, log),
            RelayMode.Client => new RelayClient(host, port, log),
            _ => null,
        };

        if (transport == null)
        {
            ActiveMode = RelayMode.Disabled;
            return;
        }

        transport.OnMessageReceived += msg => OnRemoteMessage?.Invoke(msg);
        await transport.StartAsync(cts.Token).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (transport != null)
        {
            await transport.StopAsync().ConfigureAwait(false);
            transport.Dispose();
            transport = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        cts = null;
        ActiveMode = RelayMode.Disabled;
    }

    public async Task SendAsync(ChatRelayMessage message)
    {
        if (transport == null || cts == null)
            return;

        try
        {
            await transport.SendAsync(message, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log.Warning($"[ChatRelay] SendAsync error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
