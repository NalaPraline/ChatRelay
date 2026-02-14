using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ChatRelay.Models;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace ChatRelay.Chat;

public sealed class ChatDisplay : IDisposable
{
    private readonly IFramework framework;
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private readonly ConcurrentQueue<ChatRelayMessage> messageQueue = new();
    private readonly List<string> recentMessages = new();
    private const int MaxRecentMessages = 200;
    public IReadOnlyList<string> RecentMessages => recentMessages;

    public ChatDisplay(IFramework framework, Configuration configuration, IPluginLog log)
    {
        this.framework = framework;
        this.configuration = configuration;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
    }

    public void EnqueueMessage(ChatRelayMessage message)
    {
        messageQueue.Enqueue(message);
    }

    public void LogSentMessage(ChatRelayMessage msg)
    {
        var logLine = $"[SENT] [{(XivChatType)msg.ChatType}] {msg.SenderName}";
        AddLogLine(logLine);
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        while (messageQueue.TryDequeue(out var msg))
        {
            try
            {
                DisplayMessage(msg);
            }
            catch (Exception ex)
            {
                log.Warning($"[ChatRelay] Display error: {ex.Message}");
            }
        }
    }

    private void DisplayMessage(ChatRelayMessage msg)
    {
        // Decode message for display
        string messageText;
        try
        {
            var messageBytes = Convert.FromBase64String(msg.MessageBytes);
            var messageSeString = SeString.Parse(messageBytes);
            messageText = messageSeString.TextValue;
        }
        catch
        {
            messageText = "(failed to decode)";
        }

        // Add to plugin window log
        var logLine = $"[{(XivChatType)msg.ChatType}] {msg.SenderName}: {messageText}";
        AddLogLine(logLine);
    }

    private void AddLogLine(string line)
    {
        recentMessages.Add(line);
        while (recentMessages.Count > MaxRecentMessages)
            recentMessages.RemoveAt(0);
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }
}
