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
    private bool _persistMessage;
    private int _presetIndex;
    private Vector2 _lastPos;
    private Vector2 _lastSize;

    public WhisperWindow(VenuePlusApp app, ITargetManager targetManager, ICommandManager commandManager, IPluginLog log) : base("Whisper Helper")
    {
        _app = app;
        _targetManager = targetManager;
        _commandManager = commandManager;
        _log = log;
        _persistMessage = _app.KeepWhisperMessage;
        Size = new Vector2(360f, 280f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(240f, 280f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
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
        ImGui.TextUnformatted("Target");
        ImGui.SameLine();
        var targetStr = targetName + "@" + worldName;
        var c = new Vector4(0.58f, 0.72f, 0.98f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Text, c);
        ImGui.TextUnformatted(targetStr);
        ImGui.PopStyleColor();
        ImGui.Separator();

        ImGui.PushItemWidth(-1);
        var style = ImGui.GetStyle();
        var frameH = ImGui.GetFrameHeight();
        var reserved = frameH * 3.8f + style.ItemSpacing.Y * 9f + 10f;
        var msgH = System.Math.Max(80f, ImGui.GetContentRegionAvail().Y - reserved);
        ImGui.InputTextMultiline("##whisper_msg_text", ref _message, 500, new Vector2(-1, msgH));
        ImGui.PopItemWidth();

        var presets = _app.GetWhisperPresets();
        var currentLabel = (_presetIndex >= 0 && _presetIndex < presets.Length) ? presets[_presetIndex].Name : "Select preset";
        ImGui.SetNextItemWidth(220f);
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
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var btnSize = new Vector2(100f, 0f);
        var totalBtnW = btnSize.X * 3f + spacing * 2f;
        ImGui.SameLine();
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail < totalBtnW)
        {
            ImGui.NewLine();
        }
        if (ImGui.Button("Apply", btnSize))
        {
            if (_presetIndex >= 0 && _presetIndex < presets.Length)
            {
                _message = presets[_presetIndex].Text;
            }
        }
        ImGui.SameLine();
        if (_editor != null && ImGui.Button("Create preset", btnSize))
        {
            var text = _message?.Trim() ?? string.Empty;
            _editor.OpenCreate(text);
        }
        var canDelete = _presetIndex >= 0 && _presetIndex < presets.Length;
        if (!canDelete) ImGui.BeginDisabled();
        if (ImGui.Button("Delete preset", btnSize))
        {
            if (_presetIndex >= 0)
            {
                _ = _app.RemoveWhisperPresetAtAsync(_presetIndex);
                _presetIndex = -1;
            }
        }
        if (!canDelete) ImGui.EndDisabled();
        ImGui.SameLine();
        var canEdit = _presetIndex >= 0 && _presetIndex < presets.Length;
        if (!canEdit || _editor == null) ImGui.BeginDisabled();
        if (ImGui.Button("Edit", btnSize))
        {
            _editor!.OpenForIndex(_presetIndex);
        }
        if (!canEdit || _editor == null) ImGui.EndDisabled();

        var chk = _persistMessage;
        if (ImGui.Checkbox("Keep message after send", ref chk))
        {
            _persistMessage = chk;
            _ = _app.SetKeepWhisperMessageAsync(chk);
        }
        ImGui.SameLine();
        IconDraw.IconText(Dalamud.Interface.FontAwesomeIcon.QuestionCircle, "Keep the text after sending");

        var canSend = target != null && !string.IsNullOrWhiteSpace(_message);
        if (!canSend)
        {
            ImGui.BeginDisabled();
        }
        var clicked = ImGui.Button("Send");
        if (!canSend)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
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
            _log.Debug($"Whisper send status ok={ok} to={name}@{world}");
            if (ok && !_persistMessage) _message = string.Empty;
        }

        if (!string.IsNullOrEmpty(_status))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(_status);
        }
    }
}
