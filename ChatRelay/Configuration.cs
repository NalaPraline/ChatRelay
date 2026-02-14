using System;
using System.Collections.Generic;
using ChatRelay.Models;
using Dalamud.Configuration;
using Dalamud.Game.Text;

namespace ChatRelay;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public RelayMode Mode { get; set; } = RelayMode.Disabled;
    public string RemoteHost { get; set; } = "localhost";
    public int Port { get; set; } = 14777;
    public bool AutoConnect { get; set; } = false;
    public bool ShowRelayTag { get; set; } = true;
    public bool ShowSentInLog { get; set; } = true;
    public bool RelayOwnMessages { get; set; } = false;
    public bool GlowOnNumber { get; set; } = false;

    public HashSet<ushort> EnabledChatTypes { get; set; } = new()
    {
        (ushort)XivChatType.Say,
        (ushort)XivChatType.Shout,
        (ushort)XivChatType.Party,
        (ushort)XivChatType.FreeCompany,
        (ushort)XivChatType.Yell,
    };

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
