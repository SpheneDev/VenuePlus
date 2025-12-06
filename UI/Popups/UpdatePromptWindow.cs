using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VenuePlus.Plugin;

namespace VenuePlus.UI;

public sealed class UpdatePromptWindow : Window, System.IDisposable
{
    private readonly VenuePlusApp _app;
    private string _fromVersion = string.Empty;
    private string _toVersion = string.Empty;

    public UpdatePromptWindow(VenuePlusApp app) : base("VenuePlus Updated")
    {
        _app = app;
        Size = new Vector2(420f, 180f);
        SizeCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = true;
    }

    public void SetVersions(string? from, string to)
    {
        _fromVersion = string.IsNullOrWhiteSpace(from) ? "--" : from!;
        _toVersion = string.IsNullOrWhiteSpace(to) ? "--" : to;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted($"Updated from {_fromVersion} to {_toVersion}");
        ImGui.Spacing();
        if (ImGui.Button("Show Changelog"))
        {
            _app.SetLastInstalledVersion(_toVersion);
            _app.OpenChangelogWindow();
            IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Close without showing changelog"))
        {
            _app.SetLastInstalledVersion(_toVersion);
            IsOpen = false;
        }
    }

    public void Dispose()
    {
    }
}
