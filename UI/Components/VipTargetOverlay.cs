using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using VenuePlus.Helpers;

namespace VenuePlus.UI.Components;

public sealed class VipTargetOverlay
{
    private readonly ITargetManager _targetManager;
    private System.DateTimeOffset _lastCheck;
    private bool _isVip;
    private string _targetName = string.Empty;
    private readonly Vector2 _offset = new(0f, 40f);

    public VipTargetOverlay(ITargetManager targetManager)
    {
        _targetManager = targetManager;
    }

    public void Draw(VenuePlus.Plugin.VenuePlusApp app)
    {
        if (!app.ShowVipOverlay) return;
        var now = System.DateTimeOffset.UtcNow;
        var tgt = _targetManager.Target;
        if (tgt == null || tgt.ObjectKind != ObjectKind.Player)
        {
            _isVip = false;
            return;
        }

        if (_lastCheck == System.DateTimeOffset.MinValue || _lastCheck < now.AddMilliseconds(-500))
        {
            _lastCheck = now;
            var pc = tgt as IPlayerCharacter;
            if (pc == null)
            {
                _isVip = false;
            }
            else
            {
                var name = pc.Name.TextValue;
                var world = pc.HomeWorld.Value.Name.ToString();
                _targetName = name;
                try
                {
                    var items = app.GetActive();
                    _isVip = items != null && System.Linq.Enumerable.Any(items, e => string.Equals(e.CharacterName, name, StringComparison.Ordinal) && string.Equals(e.HomeWorld, world, StringComparison.Ordinal));
                }
                catch { _isVip = false; }
            }
        }

        if (!_isVip) return;

        var vp = ImGui.GetMainViewport();
        var pos = new Vector2(vp.WorkPos.X + vp.WorkSize.X * 0.5f, vp.WorkPos.Y + _offset.Y);
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, new Vector2(0.5f, 0f));
        ImGui.SetNextWindowBgAlpha(0f);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing;
        if (ImGui.Begin("VenuePlus_VipTargetOverlay", flags))
        {
            var col = ColorUtil.HexToU32("#FFD700");
            IconDraw.IconText(Dalamud.Interface.FontAwesomeIcon.Star, 1.0f, col);
            ImGui.SameLine();
            ImGui.TextUnformatted("[VIP] " + _targetName);
        }
        ImGui.End();
    }
}
