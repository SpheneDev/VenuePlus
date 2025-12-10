using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using VenuePlus.Plugin;
using VenuePlus.Helpers;

namespace VenuePlus.UI;

public sealed class WhisperWindow : Window
{
    private readonly VenuePlusApp _app;
    private readonly ITargetManager _targetManager;
    private readonly ICommandManager _commandManager;
    private readonly IPluginLog _log;
    private WhisperPresetEditorWindow? _editor;
    private string _message = string.Empty;
    private string _status = string.Empty;
    private System.DateTimeOffset _statusUntil;
    private bool _persistMessage;
    private int _presetIndex;
    private Vector2 _lastPos;
    private Vector2 _lastSize;

    public WhisperWindow(VenuePlusApp app, ITargetManager targetManager, ICommandManager commandManager, IPluginLog log) : base("Whisper Helper", ImGuiWindowFlags.AlwaysAutoResize)
    {
        _app = app;
        _targetManager = targetManager;
        _commandManager = commandManager;
        _log = log;
        _persistMessage = _app.KeepWhisperMessage;
        RespectCloseHotkey = true;
    }
    public void SetEditor(WhisperPresetEditorWindow editor)
    {
        _editor = editor;
    }
    public Vector2 WindowPos => _lastPos;
    public Vector2 WindowSize => _lastSize;

    public void ApplyPresetByName(string name)
    {
        var presets = _app.GetWhisperPresets();
        for (int i = 0; i < presets.Length; i++)
        {
            if (string.Equals(presets[i].Name, name, System.StringComparison.Ordinal))
            {
                _presetIndex = i;
                _message = presets[i].Text;
                break;
            }
        }
    }

    public override void Draw()
    {
        _lastPos = ImGui.GetWindowPos();
        _lastSize = ImGui.GetWindowSize();
        _editor?.UpdateDock(_lastPos, _lastSize);
        var target = _targetManager.Target as IPlayerCharacter;
        var targetName = target?.Name.TextValue ?? "—";
        var worldName = target?.HomeWorld.Value.Name.ToString() ?? "—";
        ImGui.TextUnformatted("Target:");
        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
        var targetStr = targetName + "@" + worldName;
        var c = new Vector4(0.58f, 0.72f, 0.98f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Text, c);
        ImGui.TextUnformatted(targetStr);
        ImGui.PopStyleColor();
        ImGui.InputTextMultiline("##whisper_msg_text", ref _message, 500, new Vector2(300f, 200f));
        var chk = _persistMessage;
        if (ImGui.Checkbox("Keep Message", ref chk))
        {
            _persistMessage = chk;
            _ = _app.SetKeepWhisperMessageAsync(chk);
        }
        ImGui.SameLine();
        IconDraw.IconText(Dalamud.Interface.FontAwesomeIcon.QuestionCircle, "Keep the text after sending");
        ImGui.Separator();
        
        ImGui.PushStyleColor(ImGuiCol.Header, 0x00ffffff);
        ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
        if (ImGui.CollapsingHeader("Presets"))
        {
            var presets = _app.GetWhisperPresets();
            var currentLabel = (_presetIndex >= 0 && _presetIndex < presets.Length) ? presets[_presetIndex].Name : "Select preset";
            var style = ImGui.GetStyle();
            var useBtnW = 40f;
            var comboW = System.MathF.Max(100f, ImGui.GetContentRegionAvail().X - useBtnW - style.ItemSpacing.X);
            ImGui.SetNextItemWidth(comboW);
            if (ImGui.BeginCombo("##whisper_preset_combo", currentLabel))
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    var sel = i == _presetIndex;
                    if (ImGui.Selectable(presets[i].Name, sel))
                    {
                        _presetIndex = i;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine(0f, style.ItemSpacing.X);
            if (ImGui.Button("Use", new Vector2(useBtnW, 0)))
            {
                if (_presetIndex >= 0 && _presetIndex < presets.Length)
                {
                    _message = presets[_presetIndex].Text;
                }
            }

            var avail = ImGui.GetContentRegionAvail().X;
            var spacingX = style.ItemSpacing.X;
            var perW = System.MathF.Max(60f, (avail - spacingX * 2f) / 3f);
            if (_editor != null)
            {
                if (ImGui.Button("Create", new Vector2(perW, 0)))
                {
                    var text = _message?.Trim() ?? string.Empty;
                    _editor.OpenCreate(text);
                }
                ImGui.SameLine();
            }
            var canDelete = _presetIndex >= 0 && _presetIndex < presets.Length;
            if (!canDelete) ImGui.BeginDisabled();
            if (ImGui.Button("Delete", new Vector2(perW, 0)))
            {
                if (_presetIndex >= 0)
                {
                    _ = _app.RemoveWhisperPresetAtAsync(_presetIndex);
                    _presetIndex = -1;
                }
            }
            if (!canDelete) ImGui.EndDisabled();
            ImGui.SameLine();
            var canEdit = _presetIndex >= 0 && _presetIndex < presets.Length && _editor != null;
            if (!canEdit) ImGui.BeginDisabled();
            if (ImGui.Button("Edit", new Vector2(perW, 0)))
            {
                _editor!.OpenForIndex(_presetIndex);
            }
            if (!canEdit) ImGui.EndDisabled();
        }
        ImGui.PopStyleColor();
        ImGui.Separator();
        var canSend = target != null && !string.IsNullOrWhiteSpace(_message);
        var styleBottom = ImGui.GetStyle();
        var sendW = ImGui.CalcTextSize("Send").X + styleBottom.FramePadding.X * 2f;
        var clearW = ImGui.CalcTextSize("Clear").X + styleBottom.FramePadding.X * 2f;
        if (!canSend) ImGui.BeginDisabled();
        var clicked = ImGui.Button("Send", new Vector2(sendW, 0));
        if (!canSend) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Clear", new Vector2(clearW, 0)))
        {
            _message = string.Empty;
        }

        if (clicked && canSend)
        {
            var t = _targetManager.Target as IPlayerCharacter;
            if (t == null) return;
            var name = t.Name.TextValue;
            var world = t.HomeWorld.Value.Name.ToString();
            var msg = _message ?? string.Empty;
            var final = "/tell " + name + "@" + world + " " + msg.Trim();
            VenuePlus.Helpers.Chat.SendMessage(final);
            var ok = true;
            _status = ok ? "Sent" : "Failed";
            _statusUntil = System.DateTimeOffset.UtcNow.AddSeconds(2);
            _log.Debug($"Whisper send status ok={ok} to={name}@{world}");
            if (ok && !_persistMessage) _message = string.Empty;
        }

        var showStatus = !string.IsNullOrEmpty(_status) && System.DateTimeOffset.UtcNow <= _statusUntil;
        if (showStatus)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted(_status);
        }
        else
        {
            _status = string.Empty;
        }
    }
}
