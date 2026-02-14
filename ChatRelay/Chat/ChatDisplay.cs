using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    private readonly HashSet<string> recentHashes = new();
    private readonly Queue<(string hash, DateTime time)> hashExpiry = new();
    private const int MaxRecentMessages = 200;
    public IReadOnlyList<string> RecentMessages => recentMessages;
    public bool HasPendingGlow { get; set; }

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
        string messageText;
        try
        {
            var messageBytes = Convert.FromBase64String(msg.MessageBytes);
            var messageSeString = SeString.Parse(messageBytes);
            messageText = messageSeString.TextValue;
        }
        catch
        {
            messageText = "";
        }

        var hash = $"{msg.SenderName}:{msg.ChatType}:{messageText}";
        PurgeExpiredHashes();
        if (!recentHashes.Add(hash))
            return;
        hashExpiry.Enqueue((hash, DateTime.Now));

        var logLine = $"[SENT] [{(XivChatType)msg.ChatType}] {msg.SenderName}: {messageText}";
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

        // Dedup check
        var hash = $"{msg.SenderName}:{msg.ChatType}:{messageText}";
        PurgeExpiredHashes();
        if (!recentHashes.Add(hash))
            return;
        hashExpiry.Enqueue((hash, DateTime.Now));

        // Check for glow trigger
        if (configuration.GlowOnNumber && Regex.IsMatch(messageText, @"\d"))
            HasPendingGlow = true;

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

    private void PurgeExpiredHashes()
    {
        while (hashExpiry.Count > 0 && hashExpiry.Peek().time < DateTime.Now.AddSeconds(-5))
            recentHashes.Remove(hashExpiry.Dequeue().hash);
    }

    public void ClearMessages()
    {
        recentMessages.Clear();
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }
}
