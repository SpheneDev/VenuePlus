using System;
using System.Collections.Generic;
using VenuePlus.Helpers;
using Dalamud.Plugin.Services;

namespace VenuePlus.Services;

public sealed class MacroScheduler
{
    private readonly IPluginLog _log;
    private readonly Queue<MacroStep> _queue = new();
    private DateTimeOffset _nextStepAt;
    private DateTimeOffset _whisperCooldownUntil;
    private DateTimeOffset _chatCooldownUntil;
    private int _sent;
    private int _failed;
    private string _status = string.Empty;
    private DateTimeOffset _statusUntil;

    public MacroScheduler(IPluginLog log)
    {
        _log = log;
    }

    public void AddSteps(IEnumerable<MacroStep> steps)
    {
        foreach (var s in steps)
            _queue.Enqueue(s);
        _nextStepAt = DateTimeOffset.UtcNow;
        _sent = 0;
        _failed = 0;
        _status = "Sending...";
        _statusUntil = DateTimeOffset.UtcNow.AddSeconds(2);
    }

    public void Update()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextStepAt) return;

        while (_queue.Count > 0)
        {
            var step = _queue.Peek();
            if (step.Wait)
            {
                _nextStepAt = now.AddSeconds(step.Seconds);
                _queue.Dequeue();
                break;
            }

            var text = step.Text ?? string.Empty;
            var isWhisper = text.StartsWith("/tell ", StringComparison.Ordinal);
            var isChat = IsChatSendCommand(text);

            if (isWhisper && now < _whisperCooldownUntil)
            {
                _nextStepAt = _whisperCooldownUntil;
                break;
            }
            if (!isWhisper && isChat && now < _chatCooldownUntil)
            {
                _nextStepAt = _chatCooldownUntil;
                break;
            }

            try
            {
                Chat.SendMessage(text);
                _sent++;
            }
            catch (Exception ex)
            {
                _failed++;
                try { _log.Debug("MacroScheduler send failed: " + ex.Message); } catch { }
            }

            if (isWhisper)
                _whisperCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(1);
            else if (isChat)
                _chatCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(1f / 6f);

            _nextStepAt = DateTimeOffset.UtcNow;
            _queue.Dequeue();
        }

        if (_queue.Count == 0)
        {
            if (_failed == 0 && _sent > 0) _status = "Sent";
            else if (_sent > 0 && _failed > 0) _status = "Sent " + _sent + ", Failed " + _failed;
            else _status = "Failed";
            _statusUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        }
        else
        {
            _statusUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        }
    }

    public string GetStatus()
    {
        if (string.IsNullOrEmpty(_status)) return string.Empty;
        return DateTimeOffset.UtcNow <= _statusUntil ? _status : string.Empty;
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
}

public struct MacroStep
{
    public bool Wait;
    public float Seconds;
    public string Text;
}
