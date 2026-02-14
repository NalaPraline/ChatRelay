using System;
using System.Numerics;
using ChatRelay.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;

namespace ChatRelay.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    private static readonly (string Label, XivChatType Type)[] DefaultChatTypes =
    {
        ("Say", XivChatType.Say),
        ("Shout", XivChatType.Shout),
        ("Party", XivChatType.Party),
        ("Free Company", XivChatType.FreeCompany),
        ("Yell", XivChatType.Yell),
    };

    private static readonly (string Label, XivChatType Type)[] OptionalChatTypes =
    {
        ("Tell (Incoming)", XivChatType.TellIncoming),
        ("Tell (Outgoing)", XivChatType.TellOutgoing),
        ("Linkshell 1", XivChatType.Ls1),
        ("Linkshell 2", XivChatType.Ls2),
        ("Linkshell 3", XivChatType.Ls3),
        ("Linkshell 4", XivChatType.Ls4),
        ("Linkshell 5", XivChatType.Ls5),
        ("Linkshell 6", XivChatType.Ls6),
        ("Linkshell 7", XivChatType.Ls7),
        ("Linkshell 8", XivChatType.Ls8),
        ("CWLS 1", XivChatType.CrossLinkShell1),
        ("CWLS 2", XivChatType.CrossLinkShell2),
        ("CWLS 3", XivChatType.CrossLinkShell3),
        ("CWLS 4", XivChatType.CrossLinkShell4),
        ("CWLS 5", XivChatType.CrossLinkShell5),
        ("CWLS 6", XivChatType.CrossLinkShell6),
        ("CWLS 7", XivChatType.CrossLinkShell7),
        ("CWLS 8", XivChatType.CrossLinkShell8),
    };

    private string remoteHostInput;
    private int portInput;

    public ConfigWindow(Plugin plugin)
        : base("ChatRelay Configuration###ChatRelayConfig")
    {
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(400, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        configuration = plugin.Configuration;
        remoteHostInput = configuration.RemoteHost;
        portInput = configuration.Port;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Mode selection
        ImGui.Text("Relay Mode");
        ImGui.Separator();

        var mode = (int)configuration.Mode;
        if (ImGui.RadioButton("Disabled", ref mode, (int)RelayMode.Disabled))
        {
            configuration.Mode = RelayMode.Disabled;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Server", ref mode, (int)RelayMode.Server))
        {
            configuration.Mode = RelayMode.Server;
            configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Client", ref mode, (int)RelayMode.Client))
        {
            configuration.Mode = RelayMode.Client;
            configuration.Save();
        }

        ImGui.Spacing();

        // Network settings
        ImGui.Text("Network Settings");
        ImGui.Separator();

        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Remote Host", ref remoteHostInput, 256))
        {
            configuration.RemoteHost = remoteHostInput;
            configuration.Save();
        }

        if (ImGui.InputInt("Port", ref portInput))
        {
            portInput = Math.Clamp(portInput, 1024, 65535);
            configuration.Port = portInput;
            configuration.Save();
        }

        var autoConnect = configuration.AutoConnect;
        if (ImGui.Checkbox("Auto-connect on load", ref autoConnect))
        {
            configuration.AutoConnect = autoConnect;
            configuration.Save();
        }

        ImGui.Spacing();

        // Display options
        ImGui.Text("Display Options");
        ImGui.Separator();

        var showSent = configuration.ShowSentInLog;
        if (ImGui.Checkbox("Show local chat in plugin window", ref showSent))
        {
            configuration.ShowSentInLog = showSent;
            configuration.Save();
        }

        var showTag = configuration.ShowRelayTag;
        if (ImGui.Checkbox("Show [R] tag on relayed messages", ref showTag))
        {
            configuration.ShowRelayTag = showTag;
            configuration.Save();
        }

        var relayOwn = configuration.RelayOwnMessages;
        if (ImGui.Checkbox("Relay own messages", ref relayOwn))
        {
            configuration.RelayOwnMessages = relayOwn;
            configuration.Save();
        }

        var glowOnNumber = configuration.GlowOnNumber;
        if (ImGui.Checkbox("Glow on number in chat", ref glowOnNumber))
        {
            configuration.GlowOnNumber = glowOnNumber;
            configuration.Save();
        }

        ImGui.Spacing();

        // Chat type filters
        ImGui.Text("Chat Types (Default)");
        ImGui.Separator();
        DrawChatTypeCheckboxes(DefaultChatTypes);

        ImGui.Spacing();
        ImGui.Text("Chat Types (Optional)");
        ImGui.Separator();
        DrawChatTypeCheckboxes(OptionalChatTypes);
    }

    private void DrawChatTypeCheckboxes((string Label, XivChatType Type)[] chatTypes)
    {
        foreach (var (label, chatType) in chatTypes)
        {
            var enabled = configuration.EnabledChatTypes.Contains((ushort)chatType);
            if (ImGui.Checkbox(label, ref enabled))
            {
                if (enabled)
                    configuration.EnabledChatTypes.Add((ushort)chatType);
                else
                    configuration.EnabledChatTypes.Remove((ushort)chatType);
                configuration.Save();
            }
        }
    }
}
