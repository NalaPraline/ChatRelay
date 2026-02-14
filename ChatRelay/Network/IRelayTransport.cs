using System;
using System.Threading;
using System.Threading.Tasks;
using ChatRelay.Models;

namespace ChatRelay.Network;

public interface IRelayTransport : IDisposable
{
    bool IsConnected { get; }
    string StatusText { get; }
    event Action<ChatRelayMessage>? OnMessageReceived;
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    Task SendAsync(ChatRelayMessage message, CancellationToken ct);
}
