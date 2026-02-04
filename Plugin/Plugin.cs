using System;
using System.IO;
using System.Numerics;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;
using VenuePlus.UI;
using VenuePlus.State;
using VenuePlus.Services;

namespace VenuePlus.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly VenuePlusApp _app;
    private readonly WindowSystem _windowSystem = new("VenuePlus");
    private readonly VenuePlusWindow _window;
    private readonly SettingsWindow _settingsWindow;
    private readonly VipListWindow _vipListWindow;
    private readonly VenuesListWindow _venuesListWindow;
    private readonly ChangelogWindow _changelogWindow;
    private readonly UpdatePromptWindow _updatePromptWindow;
    private readonly WhisperWindow _whisperWindow;
    private readonly WhisperPresetEditorWindow _whisperEditorWindow;
    private readonly QolToolsWindow _qolToolsWindow;
    private readonly ServerAdminWindow _serverAdminWindow;
    private readonly MacroHotbarManagerWindow _macroHotbarManagerWindow;
    private readonly MacroHotbarWindow _macroHotbarWindow;
    private readonly System.Collections.Generic.Dictionary<int, MacroHotbarWindow> _macroHotbarWindowsByIndex = new();
    private readonly ChangelogService _changelogService;
    private readonly string _currentVersion;
    private readonly Action _openSettingsHandler;
    private readonly Action _openVipListHandler;
    private readonly Action _openChangelogHandler;
    private readonly Action _openWhisperHandler;
    private readonly Action _openQolToolsHandler;
    private readonly Action _openAdminPanelHandler;
    private readonly IContextMenu _contextMenu;
    private readonly ICommandManager _commandManager;
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly Dalamud.Plugin.Services.ITextureProvider _textureProvider;
    private readonly ITargetManager _targetManager;
    private readonly NameplateVipService _nameplateVipService;
    private readonly VenuePlus.Services.MacroScheduler _macroScheduler;
    private readonly IFramework _framework;
    private readonly NotificationService _notificationService;

    public Plugin(IDalamudPluginInterface pluginInterface, IContextMenu contextMenu, ICommandManager commandManager, IPluginLog pluginLog, IClientState clientState, IObjectTable objectTable, Dalamud.Plugin.Services.ITextureProvider textureProvider, ITargetManager targetManager, INamePlateGui namePlateGui, IToastGui toastGui, IChatGui chatGui, IFramework framework, INotificationManager notificationManager, ICondition condition)
    {
        _pluginInterface = pluginInterface;
        _log = pluginLog;
        _clientState = clientState;
        _objectTable = objectTable;
        _textureProvider = textureProvider;
        _targetManager = targetManager;
        _framework = framework;
        var dataPath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "venueplus.data.json");
        var settingsPath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "venueplus.settings.json");
        _app = new VenuePlusApp(dataPath, settingsPath, _log, _clientState, _objectTable, condition);

        _contextMenu = contextMenu;
        _commandManager = commandManager;

        _window = new VenuePlusWindow(_app, _textureProvider);
        _macroScheduler = new VenuePlus.Services.MacroScheduler(_log);
        _whisperWindow = new WhisperWindow(_app, _targetManager, _commandManager, _log, _macroScheduler);
        _whisperEditorWindow = new WhisperPresetEditorWindow(_app, _whisperWindow, _log);
        _whisperWindow.SetEditor(_whisperEditorWindow);
        _nameplateVipService = new NameplateVipService(namePlateGui, _app);
        _log.Debug("Creating NotificationService");
        _notificationService = new NotificationService(_app, _log, chatGui, notificationManager);
        _app.SetNotifier(_notificationService);
        _settingsWindow = new SettingsWindow(_app, _textureProvider);
        _vipListWindow = new VipListWindow(_app);
        _venuesListWindow = new VenuesListWindow(_app, _textureProvider);
        _currentVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        _changelogService = new ChangelogService();
        _changelogWindow = new ChangelogWindow(_app, _changelogService, _currentVersion);
        _updatePromptWindow = new UpdatePromptWindow(_app);
        _windowSystem.AddWindow(_window);
        _windowSystem.AddWindow(_settingsWindow);
        _windowSystem.AddWindow(_vipListWindow);
        _windowSystem.AddWindow(_venuesListWindow);
        _windowSystem.AddWindow(_changelogWindow);
        _windowSystem.AddWindow(_updatePromptWindow);
        _windowSystem.AddWindow(_whisperWindow);
        _windowSystem.AddWindow(_whisperEditorWindow);
        _qolToolsWindow = new QolToolsWindow(_app);
        _windowSystem.AddWindow(_qolToolsWindow);
        _serverAdminWindow = new ServerAdminWindow(_app);
        _windowSystem.AddWindow(_serverAdminWindow);
        _macroHotbarManagerWindow = new MacroHotbarManagerWindow(_app);
        _windowSystem.AddWindow(_macroHotbarManagerWindow);
        _macroHotbarWindow = new MacroHotbarWindow(_app, _whisperWindow, _macroScheduler, _textureProvider);
        _windowSystem.AddWindow(_macroHotbarWindow);

        _openSettingsHandler = () => { _settingsWindow.IsOpen = true; };
        _openVipListHandler = () => { _vipListWindow.IsOpen = true; };
        _openWhisperHandler = () => { _whisperWindow.IsOpen = true; };
        _app.OpenSettingsRequested += _openSettingsHandler;
        _app.OpenVipListRequested += _openVipListHandler;
        _app.OpenWhisperRequested += _openWhisperHandler;
        _app.OpenVenuesListRequested += () => { _venuesListWindow.IsOpen = true; };
        _openQolToolsHandler = () => { _qolToolsWindow.IsOpen = true; };
        _app.OpenQolToolsRequested += _openQolToolsHandler;
        _openAdminPanelHandler = () => { _serverAdminWindow.IsOpen = true; };
        _app.OpenAdminPanelRequested += _openAdminPanelHandler;
        _app.OpenMacroHotbarManagerRequested += () => { _macroHotbarManagerWindow.IsOpen = true; };
        _app.OpenMacroHotbarRequested += () => { _macroHotbarWindow.IsOpen = true; };
        _app.OpenMacroHotbarIndexRequested += OnOpenMacroHotbarIndexRequested;
        _app.CloseMacroHotbarIndexRequested += OnCloseMacroHotbarIndexRequested;
        _app.QueryMacroHotbarPositionRequested += OnQueryMacroHotbarPositionRequested;
        _app.SetMacroHotbarPositionRequested += OnSetMacroHotbarPositionRequested;
        _app.ResetMacroHotbarPositionRequested += OnResetMacroHotbarPositionRequested;

        foreach (var idx in _app.GetOpenMacroHotbarIndices())
        {
            EnsureHotbarWindow(idx).IsOpen = true;
        }

        _pluginInterface.UiBuilder.Draw += UiBuilderOnDraw;
        _framework.Update += OnFrameworkUpdate;
        _pluginInterface.UiBuilder.OpenConfigUi += UiBuilderOnOpenConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi += UiBuilderOnOpenMainUi;

        _contextMenu.OnMenuOpened += OnMenuOpened;

        _commandManager.AddHandler("/venueplus", new CommandInfo(OnVipListCommand)
        {
            HelpMessage = "Open Venue Plus window",
            ShowInHelp = true
        });
        _commandManager.AddHandler("/v+", new CommandInfo(OnVPlusCommand)
        {
            HelpMessage = "Open Venue Plus window",
            ShowInHelp = true
        });
        _commandManager.AddHandler("/vpwhisper", new CommandInfo(OnWhisperCommand)
        {
            HelpMessage = "Open whisper window",
            ShowInHelp = true
        });

        _openChangelogHandler = () => { _changelogWindow.IsOpen = true; };
        _app.OpenChangelogRequested += _openChangelogHandler;
        var prevVer = _app.GetLastInstalledVersion();
        if (string.IsNullOrWhiteSpace(prevVer) || !string.Equals(prevVer, _currentVersion, StringComparison.Ordinal))
        {
            _updatePromptWindow.SetVersions(prevVer, _currentVersion);
            _updatePromptWindow.IsOpen = true;
        }
        try { _log.Info($"Plugin initialized version={_currentVersion}"); } catch { }
        try { _app.SetClubId(_app.CurrentClubId); } catch { }
        _ = _app.ConnectRemoteAsync();
        _ = _app.TryAutoLoginAsync();
    }

    public void Dispose()
    {
        try { _log.Info("Plugin disposed"); } catch { }
        _contextMenu.OnMenuOpened -= OnMenuOpened;
        _commandManager.RemoveHandler("/venueplus");
        _commandManager.RemoveHandler("/v+");
        _commandManager.RemoveHandler("/vpwhisper");
        try { _pluginInterface.UiBuilder.Draw -= UiBuilderOnDraw; } catch { }
        try { _framework.Update -= OnFrameworkUpdate; } catch { }
        try { _pluginInterface.UiBuilder.OpenConfigUi -= UiBuilderOnOpenConfigUi; } catch { }
        try { _pluginInterface.UiBuilder.OpenMainUi -= UiBuilderOnOpenMainUi; } catch { }
        try { _window.IsOpen = false; } catch { }
        try { _settingsWindow.IsOpen = false; } catch { }
        try { _settingsWindow.IsOpen = false; } catch { }
        try { _vipListWindow.IsOpen = false; } catch { }
        try { _venuesListWindow.IsOpen = false; } catch { }
        try { _changelogWindow.IsOpen = false; } catch { }
        try { _updatePromptWindow.IsOpen = false; } catch { }
        try { _settingsWindow.Dispose(); } catch { }
        try { _venuesListWindow.Dispose(); } catch { }
        try { _changelogWindow.Dispose(); } catch { }
        try { _updatePromptWindow.Dispose(); } catch { }
        try { _windowSystem.RemoveAllWindows(); } catch { }
        try { _app.OpenSettingsRequested -= _openSettingsHandler; } catch { }
        try { _app.OpenVipListRequested -= _openVipListHandler; } catch { }
        try { _app.OpenChangelogRequested -= _openChangelogHandler; } catch { }
        try { _app.OpenWhisperRequested -= _openWhisperHandler; } catch { }
        try { _qolToolsWindow.IsOpen = false; } catch { }
        try { _app.OpenQolToolsRequested -= _openQolToolsHandler; } catch { }
        try { _serverAdminWindow.IsOpen = false; } catch { }
        try { _app.OpenAdminPanelRequested -= _openAdminPanelHandler; } catch { }
        try { _app.OpenMacroHotbarRequested -= () => { _macroHotbarWindow.IsOpen = true; }; } catch { }
        try { _app.OpenMacroHotbarManagerRequested -= () => { _macroHotbarManagerWindow.IsOpen = true; }; } catch { }
        try { _app.OpenMacroHotbarIndexRequested -= OnOpenMacroHotbarIndexRequested; } catch { }
        try { _app.CloseMacroHotbarIndexRequested -= OnCloseMacroHotbarIndexRequested; } catch { }
        try { _app.QueryMacroHotbarPositionRequested -= OnQueryMacroHotbarPositionRequested; } catch { }
        try { _app.SetMacroHotbarPositionRequested -= OnSetMacroHotbarPositionRequested; } catch { }
        try { _app.ResetMacroHotbarPositionRequested -= OnResetMacroHotbarPositionRequested; } catch { }
        try { _nameplateVipService.Dispose(); } catch { }
        try { _notificationService.Dispose(); } catch { }
        try { _window.Dispose(); } catch { }
        try { _app.DisconnectRemoteAsync().GetAwaiter().GetResult(); } catch { }
        _app.Dispose();
    }

    private void UiBuilderOnDraw()
    {
        _app.UpdateCurrentCharacterCache();
        _windowSystem.Draw();
    }

    private MacroHotbarWindow EnsureHotbarWindow(int index)
    {
        if (_macroHotbarWindowsByIndex.TryGetValue(index, out var win)) return win;
        var id = _app.GetMacroHotbarId(index);
        var title = $"Macro Hotbar - {_app.GetMacroHotbarName(index)}###mhb_{id}";
        var created = new MacroHotbarWindow(_app, _whisperWindow, _macroScheduler, _textureProvider, index, title);
        _macroHotbarWindowsByIndex[index] = created;
        _windowSystem.AddWindow(created);
        return created;
    }

    private void OnOpenMacroHotbarIndexRequested(int index)
    {
        try
        {
            var win = EnsureHotbarWindow(index);
            win.IsOpen = true;
        }
        catch { }
    }

    private void OnCloseMacroHotbarIndexRequested(int index)
    {
        try
        {
            if (_macroHotbarWindowsByIndex.TryGetValue(index, out var win))
            {
                win.IsOpen = false;
            }
        }
        catch { }
    }

    private System.Numerics.Vector2? OnQueryMacroHotbarPositionRequested(int index)
    {
        try
        {
            if (_macroHotbarWindowsByIndex.TryGetValue(index, out var win))
            {
                return win.Position ?? win.GetCurrentWindowPosition();
            }
        }
        catch { }
        return null;
    }

    private void OnSetMacroHotbarPositionRequested(int index, System.Numerics.Vector2 pos)
    {
        try
        {
            var win = EnsureHotbarWindow(index);
            win.SetExternalPosition(pos);
        }
        catch { }
    }

    private void OnResetMacroHotbarPositionRequested(int index)
    {
        try
        {
            if (_macroHotbarWindowsByIndex.TryGetValue(index, out var win))
            {
                win.ResetExternalPosition();
            }
        }
        catch { }
    }

    private void OnFrameworkUpdate(IFramework f)
    {
        _macroScheduler.Update();
    }

    private void UiBuilderOnOpenConfigUi()
    {
        _settingsWindow.IsOpen = true;
    }

    private void UiBuilderOnOpenMainUi()
    {
        _window.IsOpen = true;
    }

    private void OnVipListCommand(string command, string args)
    {
        _log.Debug("/venueplus invoked");
        _window.IsOpen = !_window.IsOpen;
    }

    private void OnVPlusCommand(string command, string args)
    {
        _log.Debug("/v+ invoked");
        _window.IsOpen = !_window.IsOpen;
    }

    private void OnWhisperCommand(string command, string args)
    {
        _log.Debug("/vpwhisper invoked");
        _whisperWindow.IsOpen = true;
    }

    

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType == ContextMenuType.Inventory)
        {
            return;
        }

        if (args.Target is not MenuTargetDefault target)
        {
            return;
        }

        var worldNameSafe = target.TargetHomeWorld.Value.Name.ToString();
        if (string.IsNullOrWhiteSpace(worldNameSafe)) { return; }
        if (string.Equals(worldNameSafe, "Dev", StringComparison.OrdinalIgnoreCase)) { return; }

        _app.EnsureSelfRights();
        var name = target.TargetName;
        var world = worldNameSafe;
        var exists = _app.GetActive().Any(e => string.Equals(e.CharacterName, name, System.StringComparison.Ordinal)
                                            && string.Equals(e.HomeWorld, world, System.StringComparison.Ordinal));
        var canInitial = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanAddVip);
        var canEditDur = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanEditVipDuration);
        if ((exists && !canEditDur) || (!exists && !canInitial))
        {
            return;
        }

        var itemText4 = new SeStringBuilder().AddText(exists ? "Change VIP duration to 4 weeks" : "Add to VIP list (4 weeks)").Build();
        var itemText12 = new SeStringBuilder().AddText(exists ? "Change VIP duration to 12 weeks" : "Add to VIP list (12 weeks)").Build();
        var itemTextLife = new SeStringBuilder().AddText(exists ? "Change VIP duration to Lifetime" : "Add to VIP list (Lifetime)").Build();

        _log.Debug($"Context menu opened for target={target.TargetName}");
        args.AddMenuItem(new MenuItem
        {
            PrefixChar = 'V',
            PrefixColor = 541,
            Name = itemText4,
            OnClicked = _ => AddFromTarget(target, VipDuration.FourWeeks)
        });
        args.AddMenuItem(new MenuItem
        {
            PrefixChar = 'V',
            PrefixColor = 541,
            Name = itemText12,
            OnClicked = _ => AddFromTarget(target, VipDuration.TwelveWeeks)
        });
        args.AddMenuItem(new MenuItem
        {
            PrefixChar = 'V',
            PrefixColor = 541,
            Name = itemTextLife,
            OnClicked = _ => AddFromTarget(target, VipDuration.Lifetime)
        });
    }

    private void AddFromTarget(MenuTargetDefault target, State.VipDuration duration)
    {
        try
        {
            var characterName = target.TargetName;
            var homeWorld = target.TargetHomeWorld.Value.Name.ToString();
            if (string.Equals(homeWorld, "Dev", StringComparison.OrdinalIgnoreCase)) return;
            _log.Debug($"Add VIP from context name={characterName} world={homeWorld} duration={duration}");
            _app.AddVip(characterName, homeWorld, duration);
        }
        catch
        {
            _window.OpenAddDialogWithPrefill(target.TargetName, string.Empty, duration);
        }
    }
}
