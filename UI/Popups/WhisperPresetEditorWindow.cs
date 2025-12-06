using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using VenuePlus.Plugin;

namespace VenuePlus.UI;

public sealed class WhisperPresetEditorWindow : Window
{
    private readonly VenuePlusApp _app;
    private readonly WhisperWindow _whisper;
    private readonly IPluginLog _log;
    private int _index = -1;
    private string _name = string.Empty;
    private string _text = string.Empty;
    private bool _createMode;

    public WhisperPresetEditorWindow(VenuePlusApp app, WhisperWindow whisper, IPluginLog log) : base("Preset Editor")
    {
        _app = app;
        _whisper = whisper;
        _log = log;
        Size = new Vector2(250f, _whisper.WindowSize.Y);
        SizeCondition = ImGuiCond.Always;
        Flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;
        RespectCloseHotkey = true;
    }

    public void OpenForIndex(int index)
    {
        var presets = _app.GetWhisperPresets();
        if (index < 0 || index >= presets.Length)
        {
            _index = -1;
            _name = string.Empty;
            _text = string.Empty;
            _createMode = false;
        }
        else
        {
            _index = index;
            _name = presets[index].Name;
            _text = presets[index].Text;
            _createMode = false;
        }
        IsOpen = true;
    }

    public void OpenCreate(string initialText)
    {
        var t = initialText?.Trim() ?? string.Empty;
        _index = -1;
        _createMode = true;
        _text = t;
        _name = GenerateName(t);
        IsOpen = true;
    }

    public override void Draw()
    {
        if (!_whisper.IsOpen)
        {
            IsOpen = false;
            return;
        }
        var vp = ImGui.GetMainViewport();
        var leftEdge = vp.WorkPos.X;
        var rightEdge = vp.WorkPos.X + vp.WorkSize.X;
        var wPos = _whisper.WindowPos;
        var wSize = _whisper.WindowSize;
        var style = ImGui.GetStyle();
        var offsetX = style.ItemSpacing.X;
        var offsetY = 0f;
        var threshold = offsetX + 2f;
        var atLeftEdge = wPos.X <= leftEdge + threshold;
        var atRightEdge = (wPos.X + wSize.X) >= rightEdge - threshold;
        var attachToLeft = atRightEdge;
        var width = 250f;
        Size = new Vector2(width, wSize.Y);
        SizeCondition = ImGuiCond.Always;
        var anchorX = attachToLeft ? (wPos.X - width - offsetX) : (wPos.X + wSize.X + offsetX);
        var anchorY = wPos.Y + offsetY;
        var ownSizeX = ImGui.GetWindowSize().X;
        var ownSizeY = ImGui.GetWindowSize().Y;
        var clampedX = System.Math.Clamp(anchorX, leftEdge, rightEdge - ownSizeX);
        var clampedY = System.Math.Clamp(anchorY, vp.WorkPos.Y, vp.WorkPos.Y + vp.WorkSize.Y - ownSizeY);
        Position = new Vector2(clampedX, clampedY);
        PositionCondition = ImGuiCond.Always;

        ImGui.PushItemWidth(-1);
        ImGui.InputText("Name", ref _name, 128);
        ImGui.Separator();
        var style2 = ImGui.GetStyle();
        var spacingX = style2.ItemSpacing.X;
        var btnRowH = ImGui.GetFrameHeight() + style2.ItemSpacing.Y;
        var msgHeight = System.Math.Max(60f, ImGui.GetContentRegionAvail().Y - btnRowH);
        ImGui.InputTextMultiline("##preset_edit_text", ref _text, 2000, new Vector2(-1, msgHeight));
        ImGui.PopItemWidth();

        var canSave = _index >= 0 && !string.IsNullOrWhiteSpace(_text?.Trim());
        var canCreate = _createMode && !string.IsNullOrWhiteSpace(_text?.Trim());
        var availW = ImGui.GetContentRegionAvail().X;
        var halfW = System.Math.Max(1f, (availW - spacingX) / 2f);
        if (!canSave && !canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Save", new Vector2(halfW, 0f)))
        {
            if (_createMode)
            {
                _ = _app.CreateWhisperPresetAsync(_name, _text!);
                _whisper.ApplyPresetByName(_name);
                _createMode = false;
                _index = -1;
                IsOpen = false;
            }
            else
            {
                _ = _app.UpdateWhisperPresetAtAsync(_index, _name, _text!);
                _whisper.ApplyPresetByName(_name);
                IsOpen = false;
            }
        }
        if (!canSave && !canCreate) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(halfW, 0f)))
        {
            IsOpen = false;
        }
    }

    private static string GenerateName(string text)
    {
        var trimmed = (text ?? string.Empty).Trim().Replace("\n", " ").Replace("\r", " ");
        if (trimmed.Length == 0) return "Preset";
        return trimmed.Length <= 24 ? trimmed : trimmed.Substring(0, 24);
    }

    public void UpdateDock(System.Numerics.Vector2 whisperPos, System.Numerics.Vector2 whisperSize)
    {
        var vp = ImGui.GetMainViewport();
        var leftEdge = vp.WorkPos.X;
        var rightEdge = vp.WorkPos.X + vp.WorkSize.X;
        var style = ImGui.GetStyle();
        var offsetX = style.ItemSpacing.X;
        var offsetY = 0f;
        var threshold = offsetX + 8f;
        var atLeftEdge = whisperPos.X <= leftEdge + threshold;
        var atRightEdge = (whisperPos.X + whisperSize.X) >= rightEdge - threshold;
        var attachToLeft = atRightEdge;
        var width = 250f;
        var anchorX = attachToLeft ? (whisperPos.X - width - offsetX) : (whisperPos.X + whisperSize.X + offsetX);
        var anchorY = whisperPos.Y + offsetY;
        var clampedX = System.Math.Clamp(anchorX, leftEdge, rightEdge - width);
        var height = whisperSize.Y;
        var clampedY = System.Math.Clamp(anchorY, vp.WorkPos.Y, vp.WorkPos.Y + vp.WorkSize.Y - height);
        Position = new System.Numerics.Vector2(clampedX, clampedY);
        PositionCondition = ImGuiCond.Always;
    }
}

