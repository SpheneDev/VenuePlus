using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using VenuePlus.Plugin;
using Dalamud.Plugin.Services;

namespace VenuePlus.UI;

public sealed class MacroHotbarWindow : Window
{
    private readonly VenuePlusApp _app;
    private readonly WhisperWindow _macroHelper;
    private readonly VenuePlus.Services.MacroScheduler _scheduler;
    private readonly ITextureProvider _textureProvider;
    private int _renameIndex = -1;
    private string _renameText = string.Empty;
    private int _iconPickerSlot = -1;
    private string _iconFilter = string.Empty;
    private static string[] _iconKeysCache = System.Enum.GetNames(typeof(Dalamud.Interface.FontAwesomeIcon));
    private System.Collections.Generic.Dictionary<int, string> _slotPresetFilter = new System.Collections.Generic.Dictionary<int, string>();
    private System.Collections.Generic.Dictionary<int, string> _slotMacroName = new System.Collections.Generic.Dictionary<int, string>();
    private System.Collections.Generic.Dictionary<int, string> _slotMacroText = new System.Collections.Generic.Dictionary<int, string>();
    private int _gameTabIndex = 0;
    private int _gamePage = 0;
    private static readonly (string name, int start, int end)[] _gameTabs = new[]
    {
        ("Jobs", 62000, 62600),
        ("Actions", 100, 4000),
        ("Emotes", 64000, 64500),
        ("FC", 64200, 64325),
        ("Items", 20000, 30000),
        ("Equipment", 30000, 50000),
        ("Statuses", 10000, 20000),
        ("Mounts", 4000, 4400),
        ("Minions", 4400, 5100),
    };

    private int _barIndex = -1;
    private bool _pushedWindowBg;
    private System.Numerics.Vector2? _pendingWindowPos;
    private string? _pendingPopupId;
    private bool _clearPositionAfterApply;
    private Vector2 _lastWindowPos;

    public MacroHotbarWindow(VenuePlusApp app, WhisperWindow macroHelper, VenuePlus.Services.MacroScheduler scheduler, ITextureProvider textureProvider, int barIndex = -1, string? title = null) : base(title ?? "Macro Hotbar", ImGuiWindowFlags.AlwaysAutoResize)
    {
        _app = app;
        _macroHelper = macroHelper;
        _scheduler = scheduler;
        _textureProvider = textureProvider;
        _barIndex = barIndex;
        RespectCloseHotkey = true;
    }

    public override void PreDraw()
    {
        var curBar = (_barIndex >= 0) ? _barIndex : _app.GetCurrentMacroHotbarIndex();
        var noBg = (_barIndex >= 0) ? _app.IsMacroHotbarNoBackgroundAt(curBar) : _app.IsMacroHotbarNoBackground;
        if (!noBg)
        {
            var bgOptWin = (_barIndex >= 0) ? _app.GetMacroHotbarBackgroundColorAt(curBar) : _app.GetMacroHotbarBackgroundColor();
            var bgVecWin = bgOptWin.HasValue ? ImGui.ColorConvertU32ToFloat4(bgOptWin.Value) : new Vector4(0f, 0f, 0f, 0.25f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, bgVecWin);
            _pushedWindowBg = true;
        }
    }

    public override void PostDraw()
    {
        if (_pushedWindowBg)
        {
            ImGui.PopStyleColor();
            _pushedWindowBg = false;
        }
        if (_clearPositionAfterApply)
        {
            Position = null;
            PositionCondition = ImGuiCond.None;
            _clearPositionAfterApply = false;
        }
    }

    public override void Draw()
    {
        _lastWindowPos = ImGui.GetWindowPos();
        var barCount = _app.GetMacroHotbarCountUnsafe();
        if (barCount <= 0)
        {
            if (ImGui.SmallButton("+ Bar")) { _ = _app.AddMacroHotbarAsync(); }
            return;
        }
        var presets = _app.GetWhisperPresets();
        var slots = (_barIndex >= 0) ? _app.GetMacroHotbarSlotsFor(_barIndex) : _app.GetMacroHotbarSlots();
        var locked = (_barIndex >= 0) ? _app.IsMacroHotbarLockedAt(_barIndex) : _app.IsMacroHotbarLocked;
        var curBar = (_barIndex >= 0) ? _barIndex : _app.GetCurrentMacroHotbarIndex();

        var noBg = (_barIndex >= 0) ? _app.IsMacroHotbarNoBackgroundAt(curBar) : _app.IsMacroHotbarNoBackground;
        var desiredFlags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | (noBg ? ImGuiWindowFlags.NoBackground : ImGuiWindowFlags.None) | (locked ? ImGuiWindowFlags.NoMove : ImGuiWindowFlags.None);
        if (Flags != desiredFlags) Flags = desiredFlags;


        

        var colsUse = System.Math.Max(1, (_barIndex >= 0) ? _app.GetMacroHotbarColumnsAt(curBar) : _app.GetMacroHotbarColumns());
        var sideUse = (_barIndex >= 0) ? _app.GetMacroHotbarButtonSideAt(curBar) : _app.GetMacroHotbarButtonSide();
        var perW = sideUse;
        var perH = sideUse;

        var slotCount = System.Math.Max(1, slots.Length);
        var spacingX = (_barIndex >= 0) ? _app.GetMacroHotbarItemSpacingXAt(curBar) : _app.GetMacroHotbarItemSpacingX();
        var spacingY = (_barIndex >= 0) ? _app.GetMacroHotbarItemSpacingYAt(curBar) : _app.GetMacroHotbarItemSpacingY();
        
        var reloadSlotsNow = false;
        for (int i = 0; i < slotCount; i++)
        {
            var cur = (i < slots.Length) ? slots[i].PresetName : string.Empty;
            var ch = (i < slots.Length) ? slots[i].Channel : VenuePlus.Configuration.ChatChannel.Whisper;
            var chAbbr = ChannelAbbr(ch);
            var useGame = (i < slots.Length) && slots[i].UseGameIcon;
            var noBgSlot = (i < slots.Length) && slots[i].NoBackground;
            var iconStr = VenuePlus.Helpers.IconDraw.ToIconStringFromKey((i < slots.Length) ? slots[i].IconKey : string.Empty);
            var nameShort = Shorten(cur, 11);
            var clicked = false;
            var scale = (i < slots.Length) ? System.Math.Clamp(slots[i].IconScale, 0.3f, 2.0f) : 1.0f;
            var slotRef = (i < slots.Length) ? slots[i] : null;
            var frameColDefault = (_barIndex >= 0) ? _app.GetMacroHotbarFrameColorDefaultAt(curBar) : _app.GetMacroHotbarFrameColorDefault();
            var iconColDefault = (_barIndex >= 0) ? _app.GetMacroHotbarIconColorDefaultAt(curBar) : _app.GetMacroHotbarIconColorDefault();
            var hoverBgDefault = (_barIndex >= 0) ? _app.GetMacroHotbarHoverBackgroundColorDefaultAt(curBar) : _app.GetMacroHotbarHoverBackgroundColorDefault();
            var showFrameDefault = (_barIndex >= 0) ? _app.GetMacroHotbarShowFrameDefaultAt(curBar) : _app.GetMacroHotbarShowFrameDefault();
            var showFrame = ((slotRef != null) && slotRef.ShowFrame) || showFrameDefault;
            var frameColU32 = (slotRef != null && slotRef.FrameColor.HasValue) ? slotRef.FrameColor.Value : (frameColDefault.HasValue ? frameColDefault.Value : 0xFFE20080u);
            var iconTintU32 = (slotRef != null && slotRef.IconColor.HasValue) ? slotRef.IconColor.Value : (iconColDefault.HasValue ? iconColDefault.Value : 0xFFFFFFFFu);
            var hoverBgColU32 = (slotRef != null && slotRef.HoverBackgroundColor.HasValue) ? slotRef.HoverBackgroundColor.Value : (hoverBgDefault.HasValue ? hoverBgDefault.Value : ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 0.25f)));
            if (noBgSlot)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
            }
            if (useGame)
            {
                var wrap = TryGetGameIconWrap((i < slots.Length) ? slots[i].GameIconId : 0);
                if (wrap != null)
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0f, 0f, 0f, 0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0f, 0f, 0f, 0f));
                    clicked = ImGui.Button($"##slot_{i}", new Vector2(perW, perH));
                    ImGui.PopStyleColor(2);
                    ImGui.PopStyleVar();
                    var draw = ImGui.GetWindowDrawList();
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    var round = ImGui.GetStyle().FrameRounding;
                    {
                        var rectW = System.MathF.Max(1f, max.X - min.X);
                        var rectH = System.MathF.Max(1f, max.Y - min.Y);
                        var scaleFill = System.MathF.Max(1.0f, scale);
                        var factor = 1.0f / scaleFill;
                        var half = 0.5f;
                        var wHalf = factor * 0.5f;
                        var allowed = half - wHalf;
                        var offX = (i < slots.Length) ? slots[i].IconOffsetX : 0f;
                        var offY = (i < slots.Length) ? slots[i].IconOffsetY : 0f;
                        var shiftU = offX * (factor / rectW);
                        var shiftV = offY * (factor / rectH);
                        if (shiftU < -allowed) shiftU = -allowed; else if (shiftU > allowed) shiftU = allowed;
                        if (shiftV < -allowed) shiftV = -allowed; else if (shiftV > allowed) shiftV = allowed;
                        var centerU = half + shiftU;
                        var centerV = half + shiftV;
                        var uvMin = new Vector2(centerU - wHalf, centerV - wHalf);
                        var uvMax = new Vector2(centerU + wHalf, centerV + wHalf);
                        draw.AddImageRounded(wrap.Handle, min, max, uvMin, uvMax, iconTintU32, round);
                    }
                    if (ImGui.IsItemHovered() || ImGui.IsItemActive())
                    {
                        draw.AddRectFilled(min, max, hoverBgColU32, round);
                    }
                    if (showFrame && (ImGui.IsItemHovered() || ImGui.IsItemActive()))
                    {
                        draw.AddRect(min, max, frameColU32, round, 0, 1.5f);
                    }
                }
                else
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
                    var useTintVec = ImGui.ColorConvertU32ToFloat4(iconTintU32);
                    var prevScale = 1.0f;
                    ImGui.SetWindowFontScale(scale);
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0f, 0f, 0f, 0f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0f, 0f, 0f, 0f));
                    if (!string.IsNullOrEmpty(iconStr)) ImGui.PushStyleColor(ImGuiCol.Text, useTintVec);
                    clicked = ImGui.Button(string.IsNullOrEmpty(iconStr) ? nameShort : iconStr, new Vector2(perW, perH));
                    if (!string.IsNullOrEmpty(iconStr)) ImGui.PopStyleColor();
                    ImGui.PopStyleColor(2);
                    ImGui.PopFont();
                    ImGui.SetWindowFontScale(prevScale);
                    ImGui.PopStyleVar();
                    if (ImGui.IsItemHovered() || ImGui.IsItemActive())
                    {
                        var draw2 = ImGui.GetWindowDrawList();
                        var min2 = ImGui.GetItemRectMin();
                        var max2 = ImGui.GetItemRectMax();
                        var round2 = ImGui.GetStyle().FrameRounding;
                        draw2.AddRectFilled(min2, max2, hoverBgColU32, round2);
                    }
                }
            }
            else
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
                var useTintVec = ImGui.ColorConvertU32ToFloat4(iconTintU32);
                var prevScale = 1.0f;
                ImGui.SetWindowFontScale(scale);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0f, 0f, 0f, 0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0f, 0f, 0f, 0f));
                var label = string.IsNullOrEmpty(iconStr) ? nameShort : string.Empty;
                if (!string.IsNullOrEmpty(iconStr)) ImGui.PushStyleColor(ImGuiCol.Text, useTintVec);
                clicked = ImGui.Button(label + $"##slot_{i}", new Vector2(perW, perH));
                if (!string.IsNullOrEmpty(iconStr)) ImGui.PopStyleColor();
                ImGui.PopStyleColor(2);
                ImGui.PopFont();
                ImGui.SetWindowFontScale(prevScale);
                ImGui.PopStyleVar();
                if (!string.IsNullOrEmpty(iconStr))
                {
                    var draw = ImGui.GetWindowDrawList();
                    var minText = ImGui.GetItemRectMin();
                    var maxText = ImGui.GetItemRectMax();
                    var offX = (i < slots.Length) ? slots[i].IconOffsetX : 0f;
                    var offY = (i < slots.Length) ? slots[i].IconOffsetY : 0f;
                    ImGui.PushFont(UiBuilder.IconFont);
                    var sz = ImGui.CalcTextSize(iconStr);
                    ImGui.PopFont();
                    var rectW = System.MathF.Max(1f, maxText.X - minText.X);
                    var rectH = System.MathF.Max(1f, maxText.Y - minText.Y);
                    var rUse = System.MathF.Min(ImGui.GetStyle().FrameRounding, System.MathF.Min(rectW, rectH) * 0.5f);
                    var safeMin = new Vector2(minText.X + rUse, minText.Y + rUse);
                    var safeMax = new Vector2(maxText.X - rUse, maxText.Y - rUse);
                    var scaledSz = new Vector2(sz.X * scale, sz.Y * scale);
                    var center = new Vector2((minText.X + maxText.X) * 0.5f + offX, (minText.Y + maxText.Y) * 0.5f + offY);
                    var halfW = scaledSz.X * 0.5f;
                    var halfH = scaledSz.Y * 0.5f;
                    var allowMin = new Vector2(safeMin.X + halfW, safeMin.Y + halfH);
                    var allowMax = new Vector2(safeMax.X - halfW, safeMax.Y - halfH);
                    if (allowMin.X > allowMax.X) { allowMin.X = allowMax.X = (safeMin.X + safeMax.X) * 0.5f; }
                    if (allowMin.Y > allowMax.Y) { allowMin.Y = allowMax.Y = (safeMin.Y + safeMax.Y) * 0.5f; }
                    center.X = System.MathF.Max(allowMin.X, System.MathF.Min(center.X, allowMax.X));
                    center.Y = System.MathF.Max(allowMin.Y, System.MathF.Min(center.Y, allowMax.Y));
                    var pos = new Vector2(center.X - halfW, center.Y - halfH);
                    draw.PushClipRect(safeMin, safeMax, true);
                    draw.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize * scale, pos, iconTintU32, iconStr);
                    draw.PopClipRect();
                }
                if (showFrame && (ImGui.IsItemHovered() || ImGui.IsItemActive()))
                {
                    var draw = ImGui.GetWindowDrawList();
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    var round = ImGui.GetStyle().FrameRounding;
                    draw.AddRect(min, max, frameColU32, round, 0, 1.5f);
                }
                if (ImGui.IsItemHovered() || ImGui.IsItemActive())
                {
                    var draw = ImGui.GetWindowDrawList();
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    var round = ImGui.GetStyle().FrameRounding;
                    draw.AddRectFilled(min, max, hoverBgColU32, round);
                }
            }
            if (noBgSlot)
            {
                ImGui.PopStyleColor(1);
            }
            if (ImGui.IsItemClicked(Dalamud.Bindings.ImGui.ImGuiMouseButton.Right)) ImGui.OpenPopup($"macro_assign_{i}");
            if (ImGui.IsItemHovered())
            {
                var tipSlot = (i < slots.Length) ? slots[i].ToolTipText : null;
                var tipName = string.IsNullOrWhiteSpace(cur) ? string.Empty : cur + " [" + ChannelAbbr(ch) + "]";
                var tip = string.IsNullOrWhiteSpace(tipSlot) ? tipName : tipSlot!;
                if (!string.IsNullOrWhiteSpace(tip))
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(tip);
                    ImGui.EndTooltip();
                }
            }

            if (clicked)
            {
                if (!string.IsNullOrWhiteSpace(cur))
                {
                    for (int p = 0; p < presets.Length; p++)
                    {
                        if (string.Equals(presets[p].Name, cur, System.StringComparison.Ordinal))
                        {
                            _macroHelper.ExecuteSequenceFromText(presets[p].Text, ch);
                            break;
                        }
                    }
                }
                else
                {
                    ImGui.OpenPopup($"macro_assign_{i}");
                }
            }

        var breakAfterEndPopup = false;
        if (ImGui.BeginPopup($"macro_assign_{i}"))
            {
                if (ImGui.BeginTabBar($"##slot_tabs_{i}"))
                {
                    if (ImGui.BeginTabItem("Assign"))
                    {
                        if (ImGui.BeginTable($"##assign_table_{i}", 2, ImGuiTableFlags.SizingStretchProp))
                        {
                            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 160f);
                            ImGui.TableSetupColumn("Presets", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            if (ImGui.Button($"Empty##slot_empty_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, string.Empty, null);
                                else _ = _app.SetMacroHotbarSlotAsync(i, string.Empty, null);
                                ImGui.CloseCurrentPopup();
                                reloadSlotsNow = true;
                                breakAfterEndPopup = true;
                            }
                            if (ImGui.Button($"Remove Slot##slot_remove_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.RemoveMacroHotbarSlotAtAsync(_barIndex, i);
                                else _ = _app.RemoveMacroHotbarSlotAtAsync(i);
                                ImGui.CloseCurrentPopup();
                                reloadSlotsNow = true;
                                breakAfterEndPopup = true;
                            }
                            ImGui.Separator();
                            ImGui.SetNextItemWidth(150f);
                            var filter = _slotPresetFilter.TryGetValue(i, out var f) ? f : string.Empty;
                            ImGui.InputTextWithHint($"##preset_filter_{i}", "Search presets", ref filter, 64);
                            _slotPresetFilter[i] = filter;
                            ImGui.TableSetColumnIndex(1);
                            ImGui.BeginChild($"##preset_list_{i}", new Vector2(320f, 200f));
                            for (int p = 0; p < presets.Length; p++)
                            {
                                if (!string.IsNullOrWhiteSpace(filter) && presets[p].Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                                if (ImGui.Button(presets[p].Name + $"##assign_{i}_{p}"))
                                {
                                    if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, presets[p].Name, null);
                                    else _ = _app.SetMacroHotbarSlotAsync(i, presets[p].Name, null);
                                    ImGui.CloseCurrentPopup();
                                    reloadSlotsNow = true;
                                    breakAfterEndPopup = true;
                                }
                            }
                            ImGui.EndChild();
                        ImGui.EndTable();
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Macro"))
                    {
                        var name = _slotMacroName.TryGetValue(i, out var n) ? n : string.Empty;
                        var text = _slotMacroText.TryGetValue(i, out var t) ? t : string.Empty;
                        ImGui.InputTextWithHint($"##macro_name_{i}", "Name (optional)", ref name, 64);
                        _slotMacroName[i] = name;
                        ImGui.InputTextMultiline($"##macro_text_{i}", ref text, 1024, new Vector2(300f, 120f));
                        _slotMacroText[i] = text;
                        var canCreate = !string.IsNullOrWhiteSpace(text);
                        ImGui.BeginDisabled(!canCreate);
                        if (ImGui.Button($"Create##macro_{i}"))
                        {
                            if (!string.IsNullOrWhiteSpace(name)) _ = _app.CreateWhisperPresetAsync(name, text);
                            else _ = _app.AddWhisperPresetAsync(text);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Create & Assign##macro_{i}"))
                        {
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                _ = _app.CreateWhisperPresetAsync(name, text);
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, name, null);
                                else _ = _app.SetMacroHotbarSlotAsync(i, name, null);
                            }
                            else
                            {
                                _ = _app.AddWhisperPresetAsync(text);
                                var created = _app.GetWhisperPresets();
                                var trimmed = text.Trim();
                                var assigned = string.Empty;
                                for (int p = 0; p < created.Length; p++)
                                {
                                    if (string.Equals(created[p].Text, trimmed, System.StringComparison.Ordinal)) { assigned = created[p].Name; break; }
                                }
                                if (!string.IsNullOrWhiteSpace(assigned))
                                {
                                    if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, assigned, null);
                                    else _ = _app.SetMacroHotbarSlotAsync(i, assigned, null);
                                }
                            }
                        }
                        ImGui.EndDisabled();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Icon"))
                    {
                        if (ImGui.BeginTable($"##icon_table_{i}", 2, ImGuiTableFlags.SizingStretchProp))
                        {
                            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("Browser", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            var useGameSel = useGame;
                            if (ImGui.RadioButton($"Game##icon_{i}", useGameSel)) { useGameSel = true; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, null, true, (i < slots.Length) ? slots[i].GameIconId : 0); else _ = _app.SetMacroHotbarSlotAsync(i, null, null, null, true, (i < slots.Length) ? slots[i].GameIconId : 0); }
                            ImGui.SameLine();
                            if (ImGui.RadioButton($"FontAwesome##icon_{i}", !useGameSel)) { useGameSel = false; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, (i < slots.Length) ? slots[i].IconKey : string.Empty, false, null); else _ = _app.SetMacroHotbarSlotAsync(i, null, null, (i < slots.Length) ? slots[i].IconKey : string.Empty, false, null); }
                            if (useGameSel)
                            {
                                var gid = (i < slots.Length) ? slots[i].GameIconId : 0;
                                ImGui.InputInt($"Icon ID##game_{i}", ref gid);
                                var wrapPrev = TryGetGameIconWrap(gid);
                                if (wrapPrev != null) ImGui.Image(wrapPrev.Handle, new Vector2(64f, 64f));
                                if (ImGui.Button($"Set##game_{i}")) { if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, null, true, gid); else _ = _app.SetMacroHotbarSlotAsync(i, null, null, null, true, gid); }
                                ImGui.SameLine();
                                if (ImGui.Button($"Browse##game_{i}"))
                                {
                                    _iconPickerSlot = i;
                                    _iconFilter = string.Empty;
                                    _gameTabIndex = 0; _gamePage = 0;
                                    ImGui.OpenPopup($"macro_game_icon_browser_{i}");
                                }
                            }
                            else
                            {
                                ImGui.InputText($"##icon_{i}", ref iconStr, 64);
                                if (ImGui.IsItemDeactivatedAfterEdit()) { if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, iconStr, false, null); else _ = _app.SetMacroHotbarSlotAsync(i, null, null, iconStr, false, null); }
                                ImGui.SameLine();
                                if (ImGui.Button($"Browse##icon_{i}"))
                                {
                                    _iconPickerSlot = i;
                                    _iconFilter = string.Empty;
                                    ImGui.OpenPopup($"macro_icon_browser_{i}");
                                }
                            }
                            ImGui.TableSetColumnIndex(1);
                            if (useGameSel)
                            {
                                var popGame = $"macro_game_icon_browser_{i}";
                                if (ImGui.BeginPopup(popGame))
                                {
                                    ImGui.InputTextWithHint("##icon_filter_game", "Search by id", ref _iconFilter, 32);
                                    ImGui.Separator();
                                    if (ImGui.BeginTabBar($"##game_tabs_{i}"))
                                    {
                                        for (int t = 0; t < _gameTabs.Length; t++)
                                        {
                                            if (ImGui.BeginTabItem(_gameTabs[t].name))
                                            {
                                                _gameTabIndex = t;
                                                var start = _gameTabs[t].start;
                                                var end = _gameTabs[t].end;
                                                var pageSize = 120;
                                                var total = end - start;
                                                var maxPage = System.Math.Max(0, (total + pageSize - 1) / pageSize - 1);
                                                if (_gamePage > maxPage) _gamePage = maxPage;
                                                var pStart = start + _gamePage * pageSize;
                                                var pEnd = System.Math.Min(end, pStart + pageSize);
                                                var colsB = 10;
                                                var cellB = System.MathF.Max(36f, ImGui.GetFrameHeight() * 1.6f);
                                                int shownB = 0;
                                                for (int id = pStart; id < pEnd; id++)
                                                {
                                                    if (!string.IsNullOrWhiteSpace(_iconFilter) && id.ToString().IndexOf(_iconFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                                                    var wrapB = TryGetGameIconWrap(id);
                                                    if (wrapB == null) continue;
                                                    var clickedPick = ImGui.ImageButton(wrapB.Handle, new Vector2(cellB, cellB));
                                                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted(id.ToString()); ImGui.EndTooltip(); }
                                                    if (clickedPick) { if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, null, true, id); else _ = _app.SetMacroHotbarSlotAsync(i, null, null, null, true, id); ImGui.CloseCurrentPopup(); }
                                                    shownB++;
                                                    if ((shownB % colsB) != 0) ImGui.SameLine();
                                                }
                                                ImGui.Separator();
                                                ImGui.BeginDisabled(_gamePage <= 0);
                                                if (ImGui.Button("Prev")) { _gamePage = System.Math.Max(0, _gamePage - 1); }
                                                ImGui.EndDisabled();
                                                ImGui.SameLine();
                                                ImGui.TextUnformatted($"Page {_gamePage + 1} / {maxPage + 1}");
                                                ImGui.SameLine();
                                                ImGui.BeginDisabled(_gamePage >= maxPage);
                                                if (ImGui.Button("Next")) { _gamePage = System.Math.Min(maxPage, _gamePage + 1); }
                                                ImGui.EndDisabled();
                                                ImGui.EndTabItem();
                                            }
                                        }
                                        ImGui.EndTabBar();
                                    }
                                    ImGui.EndPopup();
                                }
                            }
                            else
                            {
                                var popupName = $"macro_icon_browser_{i}";
                                if (ImGui.BeginPopup(popupName))
                                {
                                    ImGui.InputTextWithHint("##icon_filter", "Search icons", ref _iconFilter, 64);
                                    ImGui.Separator();
                                    var cols = 10;
                                    var cell = System.MathF.Max(36f, ImGui.GetFrameHeight() * 1.6f);
                                    int shown = 0;
                                    for (int k = 0; k < _iconKeysCache.Length; k++)
                                    {
                                        var key = _iconKeysCache[k];
                                        if (!string.IsNullOrWhiteSpace(_iconFilter) && key.IndexOf(_iconFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                                        var iconStrPick = VenuePlus.Helpers.IconDraw.ToIconStringFromKey(key);
                                        ImGui.PushFont(UiBuilder.IconFont);
                                        var clickedPick = ImGui.Button(iconStrPick + $"##pick_{i}_{k}", new Vector2(cell, cell));
                                        ImGui.PopFont();
                                        if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted(key); ImGui.EndTooltip(); }
                                        if (clickedPick)
                                        {
                                            if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, key, false, null);
                                            else _ = _app.SetMacroHotbarSlotAsync(i, null, null, key, false, null);
                                            ImGui.CloseCurrentPopup();
                                        }
                                        shown++;
                                        if ((shown % cols) != 0) ImGui.SameLine();
                                    }
                                    ImGui.EndPopup();
                                }
                            }
                            ImGui.EndTable();
                        }
                        var defScale = (_barIndex >= 0) ? _app.GetMacroHotbarIconScaleDefaultAt(curBar) : _app.GetMacroHotbarIconScaleDefault();
                        var defShowFrame = (_barIndex >= 0) ? _app.GetMacroHotbarShowFrameDefaultAt(curBar) : _app.GetMacroHotbarShowFrameDefault();
                        var noBgSlotSel = noBgSlot;
                        var iconScale = (i < slots.Length) ? slots[i].IconScale : 1.0f;
                        var maxOffX = perW * 0.5f;
                        var maxOffY = perH * 0.5f;
                        var offXUi = (i < slots.Length) ? slots[i].IconOffsetX : 0f;
                        var offYUi = (i < slots.Length) ? slots[i].IconOffsetY : 0f;
                        var showFrameSel = (slotRef != null) && slotRef.ShowFrame;
                        var tipSlot = (i < slots.Length) ? (slots[i].ToolTipText ?? string.Empty) : string.Empty;

                        if (ImGui.BeginTable($"##icon_settings_{i}", 3, ImGuiTableFlags.SizingStretchProp))
                        {
                            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 160f);
                            ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 120f);

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Hide Button Background");
                            ImGui.TableSetColumnIndex(1);
                            if (ImGui.Checkbox($"##slot_nobg_{i}", ref noBgSlotSel))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotNoBackgroundAtAsync(_barIndex, i, noBgSlotSel);
                                else _ = _app.SetMacroHotbarSlotNoBackgroundAsync(i, noBgSlotSel);
                            }
                            ImGui.TableSetColumnIndex(2);
                            if (ImGui.Button($"Reset##slot_nobg_reset_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotNoBackgroundAtAsync(_barIndex, i, false);
                                else _ = _app.SetMacroHotbarSlotNoBackgroundAsync(i, false);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Icon Scale");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.SliderFloat($"##slot_iconscale_{i}", ref iconScale, 0.3f, 2.0f))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconScaleAtAsync(_barIndex, i, iconScale);
                                else _ = _app.SetMacroHotbarSlotIconScaleAsync(i, iconScale);
                            }
                            ImGui.TableSetColumnIndex(2);
                            if (ImGui.Button($"Reset##slot_iconscale_reset_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconScaleAtAsync(_barIndex, i, defScale);
                                else _ = _app.SetMacroHotbarSlotIconScaleAsync(i, defScale);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Icon Offset");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.TextUnformatted("X:");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(160f);
                            if (ImGui.SliderFloat($"##slot_iconoffx_{i}", ref offXUi, -maxOffX, maxOffX))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconOffsetAtAsync(_barIndex, i, offXUi, offYUi);
                                else _ = _app.SetMacroHotbarSlotIconOffsetAsync(i, offXUi, offYUi);
                            }
                            ImGui.SameLine();
                            ImGui.TextUnformatted("Y:");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(160f);
                            if (ImGui.SliderFloat($"##slot_iconoffy_{i}", ref offYUi, -maxOffY, maxOffY))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconOffsetAtAsync(_barIndex, i, offXUi, offYUi);
                                else _ = _app.SetMacroHotbarSlotIconOffsetAsync(i, offXUi, offYUi);
                            }
                            ImGui.TableSetColumnIndex(2);
                            if (ImGui.Button($"Reset##slot_iconoffset_reset_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconOffsetAtAsync(_barIndex, i, 0f, 0f);
                                else _ = _app.SetMacroHotbarSlotIconOffsetAsync(i, 0f, 0f);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Show Hover Frame");
                            ImGui.TableSetColumnIndex(1);
                            if (ImGui.Checkbox($"##slot_showframe_{i}", ref showFrameSel))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotShowFrameAtAsync(_barIndex, i, showFrameSel);
                                else _ = _app.SetMacroHotbarSlotShowFrameAsync(i, showFrameSel);
                            }
                            ImGui.TableSetColumnIndex(2);
                            if (ImGui.Button($"Reset##slot_showframe_reset_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotShowFrameAtAsync(_barIndex, i, defShowFrame);
                                else _ = _app.SetMacroHotbarSlotShowFrameAsync(i, defShowFrame);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Hover Frame Color");
                            ImGui.TableSetColumnIndex(1);
                            var frameColVec = ImGui.ColorConvertU32ToFloat4(frameColU32);
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.ColorEdit4($"##slot_framecolor_{i}", ref frameColVec, ImGuiColorEditFlags.NoInputs))
                            {
                                var colU32 = ImGui.ColorConvertFloat4ToU32(frameColVec);
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotFrameColorAtAsync(_barIndex, i, colU32);
                                else _ = _app.SetMacroHotbarSlotFrameColorAsync(i, colU32);
                            }
                            ImGui.TableSetColumnIndex(2);
                            if (ImGui.Button($"Reset##slot_framecolor_reset_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotFrameColorAtAsync(_barIndex, i, null);
                                else _ = _app.SetMacroHotbarSlotFrameColorAsync(i, null);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Icon Color");
                            ImGui.TableSetColumnIndex(1);
                            var iconColVec = ImGui.ColorConvertU32ToFloat4(iconTintU32);
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.ColorEdit4($"##slot_iconcolor_{i}", ref iconColVec, ImGuiColorEditFlags.NoInputs))
                            {
                                var colU32I = ImGui.ColorConvertFloat4ToU32(iconColVec);
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconColorAtAsync(_barIndex, i, colU32I);
                                else _ = _app.SetMacroHotbarSlotIconColorAsync(i, colU32I);
                            }
                            ImGui.TableSetColumnIndex(2);
                            if (ImGui.Button($"Reset##slot_iconcolor_reset_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconColorAtAsync(_barIndex, i, null);
                                else _ = _app.SetMacroHotbarSlotIconColorAsync(i, null);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Tooltip");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.InputTextWithHint($"##slot_tooltip_{i}", "Hover tooltip text", ref tipSlot, 128))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotTooltipAtAsync(_barIndex, i, tipSlot);
                                else _ = _app.SetMacroHotbarSlotTooltipAsync(i, tipSlot);
                            }
                            ImGui.TableSetColumnIndex(2);
                            if (ImGui.Button($"Reset##slot_tooltip_reset_{i}"))
                            {
                                var empty = string.Empty;
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotTooltipAtAsync(_barIndex, i, empty);
                                else _ = _app.SetMacroHotbarSlotTooltipAsync(i, empty);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Hover Background Color");
                            ImGui.TableSetColumnIndex(1);
                            var hoverBgVec = ImGui.ColorConvertU32ToFloat4(hoverBgColU32);
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.ColorEdit4($"##slot_hoverbgcolor_{i}", ref hoverBgVec, ImGuiColorEditFlags.NoInputs))
                            {
                                var colU32H = ImGui.ColorConvertFloat4ToU32(hoverBgVec);
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotHoverBackgroundColorAtAsync(_barIndex, i, colU32H);
                                else _ = _app.SetMacroHotbarSlotHoverBackgroundColorAsync(i, colU32H);
                            }
                            ImGui.TableSetColumnIndex(2);
                            if (ImGui.Button($"Reset##slot_hoverbgcolor_reset_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotHoverBackgroundColorAtAsync(_barIndex, i, null);
                                else _ = _app.SetMacroHotbarSlotHoverBackgroundColorAsync(i, null);
                            }

                            ImGui.EndTable();
                        }
                        ImGui.Separator();
                        ImGui.TextUnformatted("Global (This Bar)");
                        var globScale = (_barIndex >= 0) ? _app.GetMacroHotbarIconScaleDefaultAt(curBar) : _app.GetMacroHotbarIconScaleDefault();
                        ImGui.TextUnformatted("Default Icon Scale:"); ImGui.SameLine(); ImGui.SetNextItemWidth(180f);
                        if (ImGui.SliderFloat($"##bar_iconscale_{i}", ref globScale, 0.3f, 2.0f))
                        {
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarIconScaleDefaultAtAsync(curBar, globScale);
                            else _ = _app.SetMacroHotbarIconScaleDefaultAsync(globScale);
                        }
                        var globShow = (_barIndex >= 0) ? _app.GetMacroHotbarShowFrameDefaultAt(curBar) : _app.GetMacroHotbarShowFrameDefault();
                        if (ImGui.Checkbox($"Default Show Hover Frame##bar_showframe_{i}", ref globShow))
                        {
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarShowFrameDefaultAtAsync(curBar, globShow);
                            else _ = _app.SetMacroHotbarShowFrameDefaultAsync(globShow);
                        }
                        var globFrameCol = (_barIndex >= 0) ? _app.GetMacroHotbarFrameColorDefaultAt(curBar) : _app.GetMacroHotbarFrameColorDefault();
                        var globFrameVec = ImGui.ColorConvertU32ToFloat4(globFrameCol.HasValue ? globFrameCol.Value : 0xFFE20080u);
                        ImGui.TextUnformatted("Default Hover Frame Color:"); ImGui.SameLine(); ImGui.SetNextItemWidth(180f);
                        if (ImGui.ColorEdit4($"##bar_framecolor_{i}", ref globFrameVec, ImGuiColorEditFlags.NoInputs))
                        {
                            var colU32 = ImGui.ColorConvertFloat4ToU32(globFrameVec);
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarFrameColorDefaultAtAsync(curBar, colU32);
                            else _ = _app.SetMacroHotbarFrameColorDefaultAsync(colU32);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Reset##bar_framecolor_reset_{i}"))
                        {
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarFrameColorDefaultAtAsync(curBar, null);
                            else _ = _app.SetMacroHotbarFrameColorDefaultAsync(null);
                        }
                        var globIconCol = (_barIndex >= 0) ? _app.GetMacroHotbarIconColorDefaultAt(curBar) : _app.GetMacroHotbarIconColorDefault();
                        var globIconVec = ImGui.ColorConvertU32ToFloat4(globIconCol.HasValue ? globIconCol.Value : 0xFFFFFFFFu);
                        ImGui.TextUnformatted("Default Icon Color:"); ImGui.SameLine(); ImGui.SetNextItemWidth(180f);
                        if (ImGui.ColorEdit4($"##bar_iconcolor_{i}", ref globIconVec, ImGuiColorEditFlags.NoInputs))
                        {
                            var colU32I = ImGui.ColorConvertFloat4ToU32(globIconVec);
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarIconColorDefaultAtAsync(curBar, colU32I);
                            else _ = _app.SetMacroHotbarIconColorDefaultAsync(colU32I);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Reset##bar_iconcolor_reset_{i}"))
                        {
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarIconColorDefaultAtAsync(curBar, null);
                            else _ = _app.SetMacroHotbarIconColorDefaultAsync(null);
                        }
                        var globHoverCol = (_barIndex >= 0) ? _app.GetMacroHotbarHoverBackgroundColorDefaultAt(curBar) : _app.GetMacroHotbarHoverBackgroundColorDefault();
                        var fallbackHover = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 0.25f));
                        var globHoverVec = ImGui.ColorConvertU32ToFloat4(globHoverCol.HasValue ? globHoverCol.Value : fallbackHover);
                        ImGui.TextUnformatted("Default Hover Background Color:"); ImGui.SameLine(); ImGui.SetNextItemWidth(180f);
                        if (ImGui.ColorEdit4($"##bar_hoverbgcolor_{i}", ref globHoverVec, ImGuiColorEditFlags.NoInputs))
                        {
                            var colU32H = ImGui.ColorConvertFloat4ToU32(globHoverVec);
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarHoverBackgroundColorDefaultAtAsync(curBar, colU32H);
                            else _ = _app.SetMacroHotbarHoverBackgroundColorDefaultAsync(colU32H);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Reset##bar_hoverbgcolor_reset_{i}"))
                        {
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarHoverBackgroundColorDefaultAtAsync(curBar, null);
                            else _ = _app.SetMacroHotbarHoverBackgroundColorDefaultAsync(null);
                        }
                        if (ImGui.Button($"Apply defaults to all slots##bar_apply_{i}"))
                        {
                            var idxApply = (_barIndex >= 0) ? _barIndex : _app.GetCurrentMacroHotbarIndex();
                            _ = _app.ApplyMacroHotbarDefaultsToAllSlotsAtAsync(idxApply);
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Options"))
                    {
                        ImGui.TextUnformatted("Channel");
                        var chSel = ch;
                        if (ImGui.Selectable("Whisper", chSel == VenuePlus.Configuration.ChatChannel.Whisper)) { chSel = VenuePlus.Configuration.ChatChannel.Whisper; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel); else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel); }
                        if (ImGui.Selectable("Say", chSel == VenuePlus.Configuration.ChatChannel.Say)) { chSel = VenuePlus.Configuration.ChatChannel.Say; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel); else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel); }
                        if (ImGui.Selectable("Party", chSel == VenuePlus.Configuration.ChatChannel.Party)) { chSel = VenuePlus.Configuration.ChatChannel.Party; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel); else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel); }
                        if (ImGui.Selectable("Shout", chSel == VenuePlus.Configuration.ChatChannel.Shout)) { chSel = VenuePlus.Configuration.ChatChannel.Shout; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel); else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel); }
                        if (ImGui.Selectable("Yell", chSel == VenuePlus.Configuration.ChatChannel.Yell)) { chSel = VenuePlus.Configuration.ChatChannel.Yell; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel); else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel); }
                        if (ImGui.Selectable("FC", chSel == VenuePlus.Configuration.ChatChannel.FC)) { chSel = VenuePlus.Configuration.ChatChannel.FC; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel); else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel); }
                        if (ImGui.Selectable("Echo", chSel == VenuePlus.Configuration.ChatChannel.Echo)) { chSel = VenuePlus.Configuration.ChatChannel.Echo; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel); else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel); }
                        if (ImGui.Selectable("Emote", chSel == VenuePlus.Configuration.ChatChannel.Emote)) { chSel = VenuePlus.Configuration.ChatChannel.Emote; if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel); else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel); }
                        
                        
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Hotbar Settings"))
                    {
                        var lockedLocal2 = locked;
                        if (ImGui.Checkbox("Locked", ref lockedLocal2)) { if (_barIndex >= 0) { _ = _app.SetMacroHotbarLockedAtAsync(curBar, lockedLocal2); } else { _ = _app.SetMacroHotbarLockedAsync(lockedLocal2); } locked = lockedLocal2; }
                        ImGui.Separator();
                        var colsCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarColumnsAt(curBar) : _app.GetMacroHotbarColumns();
                        ImGui.TextUnformatted("Cols:"); ImGui.SameLine(); ImGui.SetNextItemWidth(100f);
                        if (ImGui.SliderInt("##macro_cols_ctx_slot", ref colsCtx2, 1, 12)) { if (_barIndex >= 0) { _ = _app.SetMacroHotbarColumnsAtAsync(curBar, colsCtx2); } else { _ = _app.SetMacroHotbarColumnsAsync(colsCtx2); } }
                        ImGui.SameLine();
                        var rowsCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarRowsAt(curBar) : _app.GetMacroHotbarRows();
                        ImGui.TextUnformatted("Rows:"); ImGui.SameLine(); ImGui.SetNextItemWidth(100f);
                        if (ImGui.SliderInt("##macro_rows_ctx_slot", ref rowsCtx2, 1, 12)) { if (_barIndex >= 0) { _ = _app.SetMacroHotbarRowsAtAsync(curBar, rowsCtx2); } else { _ = _app.SetMacroHotbarRowsAsync(rowsCtx2); } }
                        ImGui.SameLine();
                        var sideCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarButtonSideAt(curBar) : _app.GetMacroHotbarButtonSide();
                        ImGui.TextUnformatted("Size:"); ImGui.SameLine(); ImGui.SetNextItemWidth(120f);
                        if (ImGui.SliderFloat("##macro_side_ctx_slot", ref sideCtx2, 16f, 128f)) { if (_barIndex >= 0) { _ = _app.SetMacroHotbarButtonSideAtAsync(curBar, sideCtx2); } else { _ = _app.SetMacroHotbarButtonSideAsync(sideCtx2); } }
                        ImGui.SameLine();
                        var spacingXCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarItemSpacingXAt(curBar) : _app.GetMacroHotbarItemSpacingX();
                        ImGui.TextUnformatted("Spacing X:"); ImGui.SameLine(); ImGui.SetNextItemWidth(100f);
                        if (ImGui.SliderFloat("##macro_spx_ctx_slot", ref spacingXCtx2, 0f, 24f)) { if (_barIndex >= 0) { _ = _app.SetMacroHotbarItemSpacingXAtAsync(curBar, spacingXCtx2); } else { _ = _app.SetMacroHotbarItemSpacingXAsync(spacingXCtx2); } }
                        ImGui.SameLine();
                        var spacingYCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarItemSpacingYAt(curBar) : _app.GetMacroHotbarItemSpacingY();
                        ImGui.TextUnformatted("Spacing Y:"); ImGui.SameLine(); ImGui.SetNextItemWidth(100f);
                        if (ImGui.SliderFloat("##macro_spy_ctx_slot", ref spacingYCtx2, 0f, 24f)) { if (_barIndex >= 0) { _ = _app.SetMacroHotbarItemSpacingYAtAsync(curBar, spacingYCtx2); } else { _ = _app.SetMacroHotbarItemSpacingYAsync(spacingYCtx2); } }
                        var noBgLocal2 = noBg;
                        if (ImGui.Checkbox("Hide Background", ref noBgLocal2)) { if (_barIndex >= 0) { _ = _app.SetMacroHotbarNoBackgroundAtAsync(curBar, noBgLocal2); } else { _ = _app.SetMacroHotbarNoBackgroundAsync(noBgLocal2); } }
                        var bgOpt2 = (_barIndex >= 0) ? _app.GetMacroHotbarBackgroundColorAt(curBar) : _app.GetMacroHotbarBackgroundColor();
                        var bgVec2 = bgOpt2.HasValue ? ImGui.ColorConvertU32ToFloat4(bgOpt2.Value) : new Vector4(0f, 0f, 0f, 0.25f);
                        ImGui.TextUnformatted("Background Color:"); ImGui.SameLine(); ImGui.SetNextItemWidth(180f);
                        if (ImGui.ColorEdit4("##bar_bgcolor", ref bgVec2, ImGuiColorEditFlags.NoInputs))
                        {
                            var colU32 = ImGui.ColorConvertFloat4ToU32(bgVec2);
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarBackgroundColorAtAsync(curBar, colU32);
                            else _ = _app.SetMacroHotbarBackgroundColorAsync(colU32);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Reset##bar_bgcolor_reset"))
                        {
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarBackgroundColorAtAsync(curBar, null);
                            else _ = _app.SetMacroHotbarBackgroundColorAsync(null);
                        }
                        var alpha2 = bgVec2.W;
                        ImGui.TextUnformatted("Alpha:"); ImGui.SameLine(); ImGui.SetNextItemWidth(120f);
                        if (ImGui.SliderFloat("##bar_bg_alpha", ref alpha2, 0f, 1f))
                        {
                            bgVec2.W = alpha2;
                            var colU32A = ImGui.ColorConvertFloat4ToU32(bgVec2);
                            if (_barIndex >= 0) _ = _app.SetMacroHotbarBackgroundColorAtAsync(curBar, colU32A);
                            else _ = _app.SetMacroHotbarBackgroundColorAsync(colU32A);
                        }
                        var showPersist2 = System.Array.IndexOf(_app.GetOpenMacroHotbarIndices(), curBar) >= 0;
                        if (ImGui.Checkbox("Show this bar at startup", ref showPersist2)) { _ = _app.SetMacroHotbarOpenStateAsync(curBar, showPersist2); if (showPersist2) _app.OpenMacroHotbarWindowAt(curBar); else _app.CloseMacroHotbarWindowAt(curBar); }
                        
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
                ImGui.EndPopup();
                if (breakAfterEndPopup) { break; }
            }
            if (((i + 1) % colsUse) != 0) ImGui.SameLine(0f, spacingX);
            else if ((i + 1) < slotCount) { ImGui.Dummy(new Vector2(0f, spacingY)); }
            if (reloadSlotsNow) break;

            if (ImGui.BeginDragDropSource())
            {
                var bytes = System.BitConverter.GetBytes(i);
                ImGui.SetDragDropPayload("MACRO_SLOT", bytes, ImGuiCond.None);
                ImGui.TextUnformatted("Move slot " + (i + 1));
                ImGui.EndDragDropSource();
            }
            if (ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var payload = ImGui.AcceptDragDropPayload("MACRO_SLOT", ImGuiDragDropFlags.AcceptBeforeDelivery);
                    if (payload.Handle != null)
                    {
                        var dataPtr = payload.Handle->Data;
                        var size = payload.Handle->DataSize;
                        if (dataPtr != null && size >= sizeof(int))
                        {
                            var src = *(int*)dataPtr;
                            if (src != i)
                            {
                                if (_barIndex >= 0) _ = _app.SwapMacroHotbarSlotsAtAsync(_barIndex, src, i);
                                else _ = _app.SwapMacroHotbarSlotsAsync(src, i);
                            }
                        }
                    }
                }
                ImGui.EndDragDropTarget();
            }
        }
        if ((slotCount % colsUse) != 0) ImGui.SameLine(0f, spacingX);
        if (!locked)
        {
            if (ImGui.Button("+", new Vector2(perW, perH)))
            {
                if (_barIndex >= 0) _ = _app.AddMacroHotbarSlotAtAsync(_barIndex);
                else _ = _app.AddMacroHotbarSlotAsync();
            }
        }

        if (_pendingWindowPos.HasValue && _pendingPopupId != null && !ImGui.IsPopupOpen(_pendingPopupId))
        {
            ImGui.SetWindowPos(_pendingWindowPos.Value, ImGuiCond.Always);
            _pendingWindowPos = null;
            _pendingPopupId = null;
        }

    }

    public void SetExternalPosition(System.Numerics.Vector2 pos)
    {
        Position = pos;
        PositionCondition = ImGuiCond.Always;
        _clearPositionAfterApply = true;
    }

    public void ResetExternalPosition()
    {
        Position = null;
        PositionCondition = ImGuiCond.None;
    }

    public Vector2 GetCurrentWindowPosition()
    {
        return _lastWindowPos;
    }

    private static string Shorten(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max);
    }

    private static string ChannelAbbr(VenuePlus.Configuration.ChatChannel ch)
    {
        switch (ch)
        {
            case VenuePlus.Configuration.ChatChannel.Whisper: return "W";
            case VenuePlus.Configuration.ChatChannel.Say: return "S";
            case VenuePlus.Configuration.ChatChannel.Party: return "P";
            case VenuePlus.Configuration.ChatChannel.Shout: return "Sh";
            case VenuePlus.Configuration.ChatChannel.Yell: return "Y";
            case VenuePlus.Configuration.ChatChannel.FC: return "FC";
            case VenuePlus.Configuration.ChatChannel.Echo: return "E";
            case VenuePlus.Configuration.ChatChannel.Emote: return "Em";
            default: return "W";
        }
    }

    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? TryGetGameIconWrap(int id)
    {
        if (id <= 0) return null;
        var lookupHi = new Dalamud.Interface.Textures.GameIconLookup((uint)id) { HiRes = true };
        if (_textureProvider.TryGetFromGameIcon(lookupHi, out var sharedHi)) return sharedHi.GetWrapOrEmpty();
        var lookupLo = new Dalamud.Interface.Textures.GameIconLookup((uint)id) { HiRes = false };
        if (_textureProvider.TryGetFromGameIcon(lookupLo, out var sharedLo)) return sharedLo.GetWrapOrEmpty();
        return null;
    }
}
