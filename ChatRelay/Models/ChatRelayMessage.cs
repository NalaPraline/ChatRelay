using System;
using System.Text.Json.Serialization;

namespace ChatRelay.Models;

public sealed class ChatRelayMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "chat";

    [JsonPropertyName("chatType")]
    public ushort ChatType { get; set; }

    [JsonPropertyName("senderName")]
    public string SenderName { get; set; } = string.Empty;

    [JsonPropertyName("senderBytes")]
    public string SenderBytes { get; set; } = string.Empty;

    [JsonPropertyName("messageBytes")]
    public string MessageBytes { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("sourceWorld")]
    public string SourceWorld { get; set; } = string.Empty;
}
