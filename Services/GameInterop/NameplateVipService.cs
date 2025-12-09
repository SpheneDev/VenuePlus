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
            var symbol = _app.VipStarChar ?? string.Empty;
            var textEnabled = _app.VipTextEnabled && !string.IsNullOrWhiteSpace(_app.VipLabelText);
            var symbolEnabled = !string.IsNullOrEmpty(symbol);
            if (!textEnabled && !symbolEnabled)
            {
                continue;
            }

            string ComposeGroup(bool sym, bool txt)
            {
                if (txt && sym)
                {
                    return _app.VipLabelOrder == VenuePlus.Configuration.VipLabelOrder.SymbolThenText
                        ? (symbol + " " + _app.VipLabelText)
                        : (_app.VipLabelText + " " + symbol);
                }
                if (sym) return symbol;
                if (txt) return _app.VipLabelText;
                return string.Empty;
            }

            var leftGroup = string.Empty;
            var rightGroup = string.Empty;
            if (_app.VipStarPosition == VenuePlus.Configuration.VipStarPosition.Left)
            {
                leftGroup = ComposeGroup(symbolEnabled, textEnabled);
            }
            else if (_app.VipStarPosition == VenuePlus.Configuration.VipStarPosition.Right)
            {
                rightGroup = ComposeGroup(symbolEnabled, textEnabled);
            }
            else
            {
                leftGroup = ComposeGroup(symbolEnabled, textEnabled);
                rightGroup = symbolEnabled ? symbol : string.Empty;
            }

            var left = new SeStringBuilder().Build();
            var right = new SeStringBuilder().Build();
            if (!string.IsNullOrEmpty(leftGroup))
            {
                left = new SeStringBuilder().AddUiForeground(leftGroup + " ", _app.VipStarColorKey).Build();
            }
            if (!string.IsNullOrEmpty(rightGroup))
            {
                right = new SeStringBuilder().AddUiForeground(" " + rightGroup, _app.VipStarColorKey).Build();
            }
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
