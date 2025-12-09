using System;
using Dalamud.Plugin.Services;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Game.Gui;

namespace VenuePlus.Services;

public sealed class NotificationService : IDisposable
{
    private const string ChatPrefix = "VenuePlus";
    private readonly VenuePlus.Plugin.VenuePlusApp _app;
    private readonly IPluginLog _log;
    private readonly IChatGui _chat;
    private readonly INotificationManager _notificationManager;
    private bool _disposed;

    public NotificationService(VenuePlus.Plugin.VenuePlusApp app, IPluginLog log, IChatGui chatGui, INotificationManager notificationManager)
    {
        _app = app;
        _log = log;
        _chat = chatGui;
        _notificationManager = notificationManager;
        try { _log.Debug("NotificationService initialized"); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    public void ShowInfo(string message)
    {
        var prefs = _app.GetNotificationPreferences();
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.None) return;
        var notif = new Notification { Content = message, Type = NotificationType.Info, Minimized = false };
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Toast || prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Both)
        {
            try { _notificationManager.AddNotification(notif); } catch { }
        }
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Chat || prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Both)
        {
            try { _chat.Print(message, ChatPrefix); } catch { }
        }
        try { _log.Debug("Toast Info: " + message); } catch { }
    }

    public void ShowSuccess(string message)
    {
        var prefs = _app.GetNotificationPreferences();
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.None) return;
        var notif = new Notification { Content = message, Type = NotificationType.Success, Minimized = false };
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Toast || prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Both)
        {
            try { _notificationManager.AddNotification(notif); } catch { }
        }
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Chat || prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Both)
        {
            try { _chat.Print(message, ChatPrefix); } catch { }
        }
        try { _log.Debug("Toast Success: " + message); } catch { }
    }

    public void ShowError(string message)
    {
        var prefs = _app.GetNotificationPreferences();
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.None) return;
        var notif = new Notification { Content = message, Type = NotificationType.Error, Minimized = false };
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Toast || prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Both)
        {
            try { _notificationManager.AddNotification(notif); } catch { }
        }
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Chat || prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Both)
        {
            try { _chat.PrintError(message, ChatPrefix); } catch { }
        }
        try { _log.Debug("Toast Error: " + message); } catch { }
    }

    public void ShowWarning(string message)
    {
        var prefs = _app.GetNotificationPreferences();
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.None) return;
        var notif = new Notification { Content = message, Type = NotificationType.Warning, Minimized = false };
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Toast || prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Both)
        {
            try { _notificationManager.AddNotification(notif); } catch { }
        }
        if (prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Chat || prefs.DisplayMode == VenuePlus.Configuration.NotificationDisplayMode.Both)
        {
            try { _chat.Print(message, ChatPrefix); } catch { }
        }
        try { _log.Debug("Toast Warning: " + message); } catch { }
    }
}
