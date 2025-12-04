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
    private readonly ChangelogService _changelogService;
    private readonly string _currentVersion;
    private readonly Action _openSettingsHandler;
    private readonly Action _openVipListHandler;
    private readonly Action _openChangelogHandler;
    private readonly IContextMenu _contextMenu;
    private readonly ICommandManager _commandManager;
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly Dalamud.Plugin.Services.ITextureProvider _textureProvider;
    private readonly ITargetManager _targetManager;
    private readonly VenuePlus.UI.Components.VipTargetOverlay _vipTargetOverlay;
    private readonly NameplateVipService _nameplateVipService;

    public Plugin(IDalamudPluginInterface pluginInterface, IContextMenu contextMenu, ICommandManager commandManager, IPluginLog pluginLog, IClientState clientState, IObjectTable objectTable, Dalamud.Plugin.Services.ITextureProvider textureProvider, ITargetManager targetManager, INamePlateGui namePlateGui)
    {
        _pluginInterface = pluginInterface;
        _log = pluginLog;
        _clientState = clientState;
        _objectTable = objectTable;
        _textureProvider = textureProvider;
        _targetManager = targetManager;
        var dataPath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "venueplus.data.json");
        var settingsPath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "venueplus.settings.json");
        _app = new VenuePlusApp(dataPath, settingsPath, _log, _clientState, _objectTable);

        _contextMenu = contextMenu;
        _commandManager = commandManager;

        _window = new VenuePlusWindow(_app, _textureProvider);
        _vipTargetOverlay = new VenuePlus.UI.Components.VipTargetOverlay(_targetManager);
        _nameplateVipService = new NameplateVipService(namePlateGui, _app);
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

        _openSettingsHandler = () => { _settingsWindow.IsOpen = true; };
        _openVipListHandler = () => { _vipListWindow.IsOpen = true; };
        _app.OpenSettingsRequested += _openSettingsHandler;
        _app.OpenVipListRequested += _openVipListHandler;
        _app.OpenVenuesListRequested += () => { _venuesListWindow.IsOpen = true; };

        _pluginInterface.UiBuilder.Draw += UiBuilderOnDraw;
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

        _openChangelogHandler = () => { _changelogWindow.IsOpen = true; };
        _app.OpenChangelogRequested += _openChangelogHandler;
        var prevVer = _app.GetLastInstalledVersion();
        if (string.IsNullOrWhiteSpace(prevVer) || !string.Equals(prevVer, _currentVersion, StringComparison.Ordinal))
        {
            _updatePromptWindow.SetVersions(prevVer, _currentVersion);
            _updatePromptWindow.IsOpen = true;
        }
        _ = _app.ConnectRemoteAsync();
        _ = _app.TryAutoLoginAsync();
    }

    public void Dispose()
    {
        _contextMenu.OnMenuOpened -= OnMenuOpened;
        _commandManager.RemoveHandler("/venueplus");
        _commandManager.RemoveHandler("/v+");
        try { _pluginInterface.UiBuilder.Draw -= UiBuilderOnDraw; } catch { }
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
        try { _nameplateVipService.Dispose(); } catch { }
        try { _window.Dispose(); } catch { }
        try { _app.DisconnectRemoteAsync().GetAwaiter().GetResult(); } catch { }
        _app.Dispose();
    }

    private void UiBuilderOnDraw()
    {
        _app.UpdateCurrentCharacterCache();
        _windowSystem.Draw();
        _vipTargetOverlay.Draw(_app);
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
        _window.IsOpen = true;
    }

    private void OnVPlusCommand(string command, string args)
    {
        _log.Debug("/v+ invoked");
        _window.IsOpen = true;
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

        _app.EnsureSelfRights();
        var name = target.TargetName;
        var world = target.TargetHomeWorld.Value.Name.ToString();
        var exists = _app.GetActive().Any(e => string.Equals(e.CharacterName, name, System.StringComparison.Ordinal)
                                            && string.Equals(e.HomeWorld, world, System.StringComparison.Ordinal));
        var canInitial = _app.IsOwnerCurrentClub || (_app.HasStaffSession && _app.StaffCanAddVip);
        if (exists)
        {
            var vipLabel = new SeStringBuilder().AddText("VIP").Build();
            args.AddMenuItem(new MenuItem
            {
                PrefixChar = '★',
                PrefixColor = 541,
                Name = vipLabel,
                OnClicked = _ => { }
            });
        }
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
            PrefixChar = 'C',
            PrefixColor = 541,
            Name = itemText4,
            OnClicked = _ => AddFromTarget(target, VipDuration.FourWeeks)
        });
        args.AddMenuItem(new MenuItem
        {
            PrefixChar = 'C',
            PrefixColor = 541,
            Name = itemText12,
            OnClicked = _ => AddFromTarget(target, VipDuration.TwelveWeeks)
        });
        args.AddMenuItem(new MenuItem
        {
            PrefixChar = 'C',
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
            _log.Debug($"Add VIP from context name={characterName} world={homeWorld} duration={duration}");
            _app.AddVip(characterName, homeWorld, duration);
        }
        catch
        {
            _window.OpenAddDialogWithPrefill(target.TargetName, string.Empty, duration);
        }
    }
}
