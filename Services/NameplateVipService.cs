using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;

namespace VenuePlus.Services;

public sealed class NameplateVipService : IDisposable
{
    private readonly INamePlateGui _namePlateGui;
    private readonly VenuePlus.Plugin.VenuePlusApp _app;
    private bool _disposed;

    public NameplateVipService(INamePlateGui namePlateGui, VenuePlus.Plugin.VenuePlusApp app)
    {
        _namePlateGui = namePlateGui;
        _app = app;
        _namePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext ctx, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!_app.ShowVipNameplateHook) return;
        for (int i = 0; i < handlers.Count; i++)
        {
            var h = handlers[i];
            var obj = h.GameObject;
            if (obj == null || obj.ObjectKind != ObjectKind.Player) continue;
            var pc = obj as IPlayerCharacter;
            if (pc == null) continue;
            var name = pc.Name.TextValue;
            var world = pc.HomeWorld.Value.Name.ToString();
            if (!_app.IsVip(name, world)) continue;
            var original = h.InfoView.Name;
            var left = new SeStringBuilder().AddUiForeground("★ ", _app.VipStarColorKey).Build();
            var right = new SeStringBuilder().Build();
            h.NameParts.Text = original;
            h.NameParts.TextWrap = (left, right);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _namePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate; } catch { }
    }
}
