using Dalamud.Plugin.Services;

namespace VenuePlus.Helpers;

internal static class Logger
{
    private static IPluginLog? _log;

    public static void Initialize(IPluginLog? log)
    {
        _log = log;
    }

    public static void LogDebug(string message)
    {
        try { _log?.Debug(message); } catch { }
    }

    public static void LogInfo(string message)
    {
        try { _log?.Info(message); } catch { }
    }

    public static void LogWarning(string message)
    {
        try { _log?.Warning(message); } catch { }
    }

    public static void LogError(string message)
    {
        try { _log?.Error(message); } catch { }
    }
}
