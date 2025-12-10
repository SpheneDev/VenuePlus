using System.Numerics;
using System.Collections.Generic;
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
    private List<SendStep>? _steps;
    private int _stepIndex;
    private System.DateTimeOffset _nextStepAt;
    private bool _sending;
    private int _sentCount;
    private int _failCount;
    private System.DateTimeOffset _whisperCooldownUntil;
    private System.DateTimeOffset _chatCooldownUntil;
    private ChatChannel _channel = ChatChannel.Whisper;
    private const int MaxMacroLines = 15;
    private const float MacroRestartPauseSeconds = 0.2f;

    private struct SendStep
    {
        public bool Wait;
        public float Seconds;
        public string Text;
    }

    private enum ChatChannel
    {
        Whisper,
        Say,
        Party,
        Shout,
        Yell,
        FC,
        Echo,
        Emote
    }

    private static string ChannelLabel(ChatChannel ch)
    {
        switch (ch)
        {
            case ChatChannel.Whisper: return "Whisper";
            case ChatChannel.Say: return "Say";
            case ChatChannel.Party: return "Party";
            case ChatChannel.Shout: return "Shout";
            case ChatChannel.Yell: return "Yell";
            case ChatChannel.FC: return "FC";
            case ChatChannel.Echo: return "Echo";
            case ChatChannel.Emote: return "Emote";
            default: return "Whisper";
        }
    }

    private static string BuildChannelCommand(ChatChannel ch, string text)
    {
        switch (ch)
        {
            case ChatChannel.Say: return "/say " + text;
            case ChatChannel.Party: return "/p " + text;
            case ChatChannel.Shout: return "/sh " + text;
            case ChatChannel.Yell: return "/y " + text;
            case ChatChannel.FC: return "/fc " + text;
            case ChatChannel.Echo: return "/echo " + text;
            case ChatChannel.Emote: return "/em " + text;
            default: return text;
        }
    }

    private static bool IsChatSendCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (!s.StartsWith("/")) return false;
        var split = s.IndexOf(' ');
        var cmd = split > 0 ? s.Substring(0, split) : s;
        cmd = cmd.ToLowerInvariant();
        if (cmd.StartsWith("/ls") || cmd.StartsWith("/cwls")) return true;
        return cmd == "/say" || cmd == "/s" || cmd == "/echo" || cmd == "/yell" || cmd == "/y" || cmd == "/shout" || cmd == "/sh" || cmd == "/party" || cmd == "/p" || cmd == "/fc" || cmd == "/tell" || cmd == "/t";
    }

    public WhisperWindow(VenuePlusApp app, ITargetManager targetManager, ICommandManager commandManager, IPluginLog log) : base("Macro Helper", ImGuiWindowFlags.AlwaysAutoResize)
    {
        _app = app;
        _targetManager = targetManager;
        _commandManager = commandManager;
        _log = log;
        _persistMessage = _app.KeepWhisperMessage;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(316f, 0f),
            MaximumSize = new Vector2(316f, float.MaxValue)
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
        ImGui.TextUnformatted("Channel:");
        ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X);
        var currentChannel = ChannelLabel(_channel);
        ImGui.SetNextItemWidth(120f);
        if (ImGui.BeginCombo("##whisper_channel_combo", currentChannel))
        {
            if (ImGui.Selectable("Whisper", _channel == ChatChannel.Whisper)) _channel = ChatChannel.Whisper;
            if (ImGui.Selectable("Say", _channel == ChatChannel.Say)) _channel = ChatChannel.Say;
            if (ImGui.Selectable("Party", _channel == ChatChannel.Party)) _channel = ChatChannel.Party;
            if (ImGui.Selectable("Shout", _channel == ChatChannel.Shout)) _channel = ChatChannel.Shout;
            if (ImGui.Selectable("Yell", _channel == ChatChannel.Yell)) _channel = ChatChannel.Yell;
            if (ImGui.Selectable("FC", _channel == ChatChannel.FC)) _channel = ChatChannel.FC;
            if (ImGui.Selectable("Echo", _channel == ChatChannel.Echo)) _channel = ChatChannel.Echo;
            if (ImGui.Selectable("Emote", _channel == ChatChannel.Emote)) _channel = ChatChannel.Emote;
            ImGui.EndCombo();
        }
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
        var msgTrim = _message?.Trim() ?? string.Empty;
        var normalized = msgTrim.Replace("\r\n", "\n");
        var split = normalized.Length == 0 ? System.Array.Empty<string>() : normalized.Split('\n');
        var hasAnyCmd = false;
        var hasAnyNonCmd = false;
        for (int i = 0; i < split.Length; i++)
        {
            var li = split[i].Trim();
            if (li.Length == 0) continue;
            if (li.StartsWith("/")) hasAnyCmd = true; else hasAnyNonCmd = true;
        }
        var needsTarget = _channel == ChatChannel.Whisper;
        var canSend = !_sending && split.Length > 0 && (hasAnyCmd || (hasAnyNonCmd && (!needsTarget || target != null)));
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
            var name = t?.Name.TextValue ?? string.Empty;
            var world = t?.HomeWorld.Value.Name.ToString() ?? string.Empty;

            var steps = new List<SendStep>();
            var macroLines = 0;
            for (int i = 0; i < split.Length; i++)
            {
                var li = split[i].Trim();
                if (li.Length == 0) continue;
                if (li.StartsWith("//")) continue;

                if (li.StartsWith("/wait"))
                {
                    var rest = li.Length >= 5 ? li.Substring(5).Trim() : string.Empty;
                    float secs = 0f;
                    if (!string.IsNullOrEmpty(rest))
                    {
                        if (float.TryParse(rest, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)) secs = s;
                    }
                    steps.Add(new SendStep { Wait = true, Seconds = secs, Text = string.Empty });
                    continue;
                }

                if (li.StartsWith("/"))
                {
                    steps.Add(new SendStep { Wait = false, Seconds = 0f, Text = li });
                    macroLines++;
                    if (macroLines >= MaxMacroLines)
                    {
                        steps.Add(new SendStep { Wait = true, Seconds = MacroRestartPauseSeconds, Text = string.Empty });
                        macroLines = 0;
                    }
                }
                else
                {
                    string final;
                    if (_channel == ChatChannel.Whisper)
                    {
                        if (t == null) continue;
                        final = "/tell " + name + "@" + world + " " + li;
                    }
                    else
                    {
                        final = BuildChannelCommand(_channel, li);
                    }
                    steps.Add(new SendStep { Wait = false, Seconds = 0f, Text = final });
                    macroLines++;
                    if (macroLines >= MaxMacroLines)
                    {
                        steps.Add(new SendStep { Wait = true, Seconds = MacroRestartPauseSeconds, Text = string.Empty });
                        macroLines = 0;
                    }
                }
            }

            if (steps.Count > 0)
            {
                _steps = steps;
                _stepIndex = 0;
                _nextStepAt = System.DateTimeOffset.UtcNow;
                _sending = true;
                _sentCount = 0;
                _failCount = 0;
                _status = "Sending...";
                _statusUntil = System.DateTimeOffset.UtcNow.AddSeconds(2);
            }
        }

        if (_sending)
        {
            var now = System.DateTimeOffset.UtcNow;
            if (now >= _nextStepAt)
            {
                if (_steps != null && _stepIndex < _steps.Count)
                {
                    var step = _steps[_stepIndex];
                    if (step.Wait)
                    {
                        _nextStepAt = now.AddSeconds(step.Seconds);
                        _stepIndex++;
                    }
                    else
                    {
                        var isWhisper = step.Text != null && step.Text.StartsWith("/tell ", System.StringComparison.Ordinal);
                        var isChat = IsChatSendCommand(step.Text ?? string.Empty);
                        if (isWhisper && now < _whisperCooldownUntil)
                        {
                            _nextStepAt = _whisperCooldownUntil;
                        }
                        else if (!isWhisper && isChat && now < _chatCooldownUntil)
                        {
                            _nextStepAt = _chatCooldownUntil;
                        }
                        else
                        {
                            try
                            {
                                VenuePlus.Helpers.Chat.SendMessage(step.Text!);
                                _sentCount++;
                            }
                            catch (System.Exception ex)
                            {
                                _failCount++;
                                _log.Debug($"Whisper queued send failed idx={_stepIndex}: {ex.Message}");
                            }
                            if (isWhisper)
                            {
                                _whisperCooldownUntil = System.DateTimeOffset.UtcNow.AddSeconds(1);
                            }
                            else if (isChat)
                            {
                                _chatCooldownUntil = System.DateTimeOffset.UtcNow.AddSeconds(1f / 6f);
                            }
                            _nextStepAt = System.DateTimeOffset.UtcNow;
                            _stepIndex++;
                        }
                    }
                }
                else
                {
                    _sending = false;
                    if (_failCount == 0 && _sentCount > 0) _status = "Sent"; else if (_sentCount > 0 && _failCount > 0) _status = "Sent " + _sentCount + ", Failed " + _failCount; else _status = "Failed";
                    _statusUntil = System.DateTimeOffset.UtcNow.AddSeconds(2);
                    if (_sentCount > 0 && !_persistMessage) _message = string.Empty;
                }
            }
            _statusUntil = System.DateTimeOffset.UtcNow.AddSeconds(2);
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
