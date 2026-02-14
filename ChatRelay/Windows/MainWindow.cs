using System;
using System.Numerics;
using ChatRelay.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace ChatRelay.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private float glowTimer;

    public MainWindow(Plugin plugin)
        : base("ChatRelay##Main", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(350, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Glow effect when a number was detected and window is not focused
        if (plugin.ChatDisplay.HasPendingGlow)
        {
            if (ImGui.IsWindowFocused())
            {
                plugin.ChatDisplay.HasPendingGlow = false;
                glowTimer = 0;
            }
            else
            {
                glowTimer += ImGui.GetIO().DeltaTime;
                var pulse = (float)(0.5 + 0.5 * Math.Sin(glowTimer * 4.0));
                var color = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.6f, 0.0f, 0.4f + 0.6f * pulse));
                var drawList = ImGui.GetWindowDrawList();
                var pos = ImGui.GetWindowPos();
                var size = ImGui.GetWindowSize();
                drawList.AddRect(pos, pos + size, color, 0, 0, 2.0f + 2.0f * pulse);
            }
        }

        var manager = plugin.RelayManager;

        // Status
        var modeText = manager.ActiveMode switch
        {
            RelayMode.Server => "Server",
            RelayMode.Client => "Client",
            _ => "Disabled",
        };
        ImGui.Text($"Mode: {modeText}");
        ImGui.Text($"Status: {manager.StatusText}");

        var connected = manager.IsConnected;
        ImGui.Text($"Connected: {(connected ? "Yes" : "No")}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Controls
        var isActive = manager.ActiveMode != RelayMode.Disabled;

        if (!isActive)
        {
            if (ImGui.Button("Start Server"))
            {
                _ = manager.StartAsync(RelayMode.Server, plugin.Configuration.RemoteHost, plugin.Configuration.Port);
            }
            ImGui.SameLine();
            if (ImGui.Button("Connect as Client"))
            {
                _ = manager.StartAsync(RelayMode.Client, plugin.Configuration.RemoteHost, plugin.Configuration.Port);
            }
        }
        else
        {
            if (ImGui.Button("Stop"))
            {
                _ = manager.StopAsync();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            plugin.ChatDisplay.ClearMessages();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Message log
        ImGui.Text("Recent Relayed Messages:");
        using (var child = ImRaii.Child("MessageLog", new Vector2(0, 0), true))
        {
            if (child.Success)
            {
                var messages = plugin.ChatDisplay.RecentMessages;
                for (var i = 0; i < messages.Count; i++)
                {
                    ImGui.TextWrapped(messages[i]);
                }

                if (messages.Count > 0)
                    ImGui.SetScrollHereY(1.0f);
            }
        }
    }
}
