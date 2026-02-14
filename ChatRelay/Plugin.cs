using System;
using ChatRelay.Chat;
using ChatRelay.Models;
using ChatRelay.Network;
using ChatRelay.Windows;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ChatRelay;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("ChatRelay");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    internal RelayManager RelayManager { get; init; }
    internal ChatCapture ChatCapture { get; init; }
    internal ChatDisplay ChatDisplay { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        RelayManager = new RelayManager(Log);
        ChatCapture = new ChatCapture(ChatGui, PlayerState, Configuration, Log);
        ChatDisplay = new ChatDisplay(Framework, Configuration, Log);

        // Wire chat capture → relay manager
        ChatCapture.OnChatMessage += msg =>
        {
            if (RelayManager.ActiveMode != RelayMode.Disabled)
            {
                if (Configuration.ShowSentInLog)
                    ChatDisplay.LogSentMessage(msg);
                _ = RelayManager.SendAsync(msg);
            }
        };

        // Wire relay manager → chat display
        RelayManager.OnRemoteMessage += msg => ChatDisplay.EnqueueMessage(msg);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler("/chatrelay", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open ChatRelay. Usage: /chatrelay [server|client|stop|config]"
        });
        CommandManager.AddHandler("/cr", new CommandInfo(OnCommand)
        {
            HelpMessage = "Short alias for /chatrelay"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("[ChatRelay] Plugin loaded");

        if (Configuration.AutoConnect && Configuration.Mode != RelayMode.Disabled)
            _ = RelayManager.StartAsync(Configuration.Mode, Configuration.RemoteHost, Configuration.Port);
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler("/chatrelay");
        CommandManager.RemoveHandler("/cr");

        ChatCapture.Dispose();
        ChatDisplay.Dispose();
        RelayManager.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        switch (arg)
        {
            case "server":
                _ = RelayManager.StartAsync(RelayMode.Server, Configuration.RemoteHost, Configuration.Port);
                ChatGui.Print("[ChatRelay] Starting server...");
                break;

            case "client":
                _ = RelayManager.StartAsync(RelayMode.Client, Configuration.RemoteHost, Configuration.Port);
                ChatGui.Print("[ChatRelay] Connecting to server...");
                break;

            case "stop":
                _ = RelayManager.StopAsync();
                ChatGui.Print("[ChatRelay] Stopped.");
                break;

            case "config":
                ToggleConfigUi();
                break;

            default:
                ToggleMainUi();
                break;
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
