using System;
using ChatRelay.Models;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace ChatRelay.Chat;

public sealed class ChatCapture : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly IPlayerState playerState;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    public event Action<ChatRelayMessage>? OnChatMessage;

    public ChatCapture(IChatGui chatGui, IPlayerState playerState, Configuration configuration, IPluginLog log)
    {
        this.chatGui = chatGui;
        this.playerState = playerState;
        this.configuration = configuration;
        this.log = log;

        chatGui.ChatMessage += OnChatMessageReceived;
    }

    private void OnChatMessageReceived(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!configuration.EnabledChatTypes.Contains((ushort)type))
            return;

        if (!configuration.RelayOwnMessages && playerState.IsLoaded)
        {
            var localName = playerState.CharacterName?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(localName) && sender.ToString().Contains(localName))
                return;
        }

        try
        {
            var homeWorld = string.Empty;
            if (playerState.IsLoaded && playerState.HomeWorld.IsValid)
                homeWorld = playerState.HomeWorld.Value.Name.ToString();

            var relayMsg = new ChatRelayMessage
            {
                Type = "chat",
                ChatType = (ushort)type,
                SenderName = sender.ToString(),
                SenderBytes = Convert.ToBase64String(sender.Encode()),
                MessageBytes = Convert.ToBase64String(message.Encode()),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SourceWorld = homeWorld,
            };

            OnChatMessage?.Invoke(relayMsg);
        }
        catch (Exception ex)
        {
            log.Warning($"[ChatRelay] Capture error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessageReceived;
    }
}
