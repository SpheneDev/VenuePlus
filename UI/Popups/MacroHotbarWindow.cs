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
    private string _renameText = string.Empty;
    private int _iconPickerSlot = -1;
    private string _iconFilter = string.Empty;
    private static string[] _iconKeysCache = System.Enum.GetNames(typeof(Dalamud.Interface.FontAwesomeIcon));
    private System.Collections.Generic.Dictionary<int, string> _slotPresetFilter = new System.Collections.Generic.Dictionary<int, string>();
    private System.Collections.Generic.Dictionary<int, string> _slotMacroName = new System.Collections.Generic.Dictionary<int, string>();
    private System.Collections.Generic.Dictionary<int, string> _slotMacroText = new System.Collections.Generic.Dictionary<int, string>();
    private int _gameTabIndex = 0;
    private int _gamePage = 0;
    private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>> _gameIconIdsByTab = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
    private readonly System.Collections.Generic.List<int> _gameFilteredIds = new System.Collections.Generic.List<int>();
    private int _gameFilteredTabIndex = -1;
    private string _gameFilteredText = string.Empty;
    private sealed class GameIconCategory
    {
        public string Name { get; }
        public (int start, int end)[] Ranges { get; }

        public GameIconCategory(string name, (int start, int end)[] ranges)
        {
            Name = name;
            Ranges = ranges;
        }
    }

    private static readonly GameIconCategory[] _gameCategories = new[]
    {
        new GameIconCategory("General", new[] {
            (0, 95), (101, 132), (651, 652), (654, 655), (695, 698), (66001, 66001), (66021, 66023),
            (66031, 66033), (66041, 66043), (66051, 66053), (66061, 66063), (66071, 66073), (66081, 66083),
            (66101, 66105), (66121, 66125), (66141, 66145), (66161, 66171), (66181, 66191), (66301, 66341),
            (66401, 66423), (66452, 66473), (60001, 6004), (60011, 60013), (60026, 60048), (60071, 60074),
            (61471, 61489), (61501, 61548), (61551, 61598), (61751, 61768), (61801, 61850), (61875, 61880),
        }),
        new GameIconCategory("Jobs", new[] {
            (0, 0), (62001, 62042), (62801, 62842), (62226, 62267), (62101, 62142), (62301, 62320), (62401, 62422),
            (82271, 82286),
        }),
        new GameIconCategory("Quests", new[] {
            (0, 0),
            (71001, 71006), (71021, 71025), (71041, 71045), (71061, 70165), (71081, 70185), (71101, 71102),
            (71121, 71125), (71141, 71145), (71201, 71205), (71221, 71225), (61721, 61723), (61731, 61733),
            (63875, 63892), (63900, 63977), (63979, 63987),
        }),
        new GameIconCategory("Avatars", new[] {
            (0, 0), (82009, 82010), (62145, 62146), (72556, 72608), (72001, 72059), (73001, 73279), (73281, 73283),
            (73285, 73287), (88001, 88457),
        }),
        new GameIconCategory("Emotes", new[] {
            (0, 0), (246001, 246004), (246101, 246133), (246201, 246280), (246282, 246299), (246301, 246324),
            (246327, 246453), (246456, 246457), (246459, 246459), (246463, 246470),
        }),
        new GameIconCategory("Rewards", new[] {
            (0, 0), (65001, 65127), (65130, 65134), (65137, 65137),
        }),
        new GameIconCategory("MapMarkers", new[] {
            (0, 0),
            (60401, 60408), (60412, 60482), (60501, 60508), (60511, 60515), (60550, 60565), (60567, 60583),
            (60585, 60611), (60640, 60649), (60651, 60662), (60751, 60792), (60901, 60999),
        }),
        new GameIconCategory("Shapes", new[] {
            (0, 0),
            (82091, 82093), (90001, 90004), (90200, 90263), (90401, 90463), (61901, 61918),
            (230131, 230143), (230201, 230215), (230301, 230317), (230401, 230433), (230701, 230715),
            (230626, 230629), (230631, 230641), (180021, 180028),
        }),
        new GameIconCategory("Minions", new[] {
            (0, 0), (4401, 4521), (4523, 4611), (4613, 4939), (4941, 4962), (4964, 4967), (4971, 4973),
            (4977, 4979), (59401, 59521), (59523, 59611), (59613, 59939), (59941, 59962),
            (59964, 59967), (59971, 59973), (59977, 59979),
        }),
        new GameIconCategory("Mounts", new[] {
            (0, 0), (4001, 4045), (4047, 4098), (4101, 4276), (4278, 4329), (4331, 4332), (4334, 4335),
            (4339, 4339), (4343, 4343),
        }),
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
                        var useGameSel = useGame;

                        if (ImGui.BeginTabBar($"##icon_tabs_{i}"))
                        {
                            if (ImGui.BeginTabItem("Source"))
                            {
                                ImGui.TextUnformatted("Icon Source");
                                ImGui.Separator();
                                if (ImGui.RadioButton($"Game##icon_src_game_{i}", useGameSel))
                                {
                                    useGameSel = true;
                                    if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, null, true, (i < slots.Length) ? slots[i].GameIconId : 0);
                                    else _ = _app.SetMacroHotbarSlotAsync(i, null, null, null, true, (i < slots.Length) ? slots[i].GameIconId : 0);
                                }
                                ImGui.SameLine();
                                if (ImGui.RadioButton($"FontAwesome##icon_src_font_{i}", !useGameSel))
                                {
                                    useGameSel = false;
                                    if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, (i < slots.Length) ? slots[i].IconKey : string.Empty, false, null);
                                    else _ = _app.SetMacroHotbarSlotAsync(i, null, null, (i < slots.Length) ? slots[i].IconKey : string.Empty, false, null);
                                }

                                if (useGameSel)
                                {
                                    var gid = (i < slots.Length) ? slots[i].GameIconId : 0;
                                    ImGui.SetNextItemWidth(100f);
                                    ImGui.InputInt($"ID##icon_gid_{i}", ref gid);
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Apply##icon_gid_apply_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, null, true, gid);
                                        else _ = _app.SetMacroHotbarSlotAsync(i, null, null, null, true, gid);
                                    }
                                    var wrapPrev = TryGetGameIconWrap(gid);
                                    if (wrapPrev != null)
                                    {
                                        ImGui.Image(wrapPrev.Handle, new Vector2(48f, 48f));
                                    }
                                    if (ImGui.Button($"Browse##icon_game_{i}"))
                                    {
                                        _iconPickerSlot = i;
                                        _iconFilter = string.Empty;
                                        _gameTabIndex = 0;
                                        _gamePage = 0;
                                        ImGui.OpenPopup($"macro_game_icon_browser_{i}");
                                    }

                                    var popGame = $"macro_game_icon_browser_{i}";
                                    if (ImGui.BeginPopup(popGame))
                                    {
                                        var filterGame = _iconFilter ?? string.Empty;
                                        ImGui.InputTextWithHint("##icon_filter_game", "Search by id", ref filterGame, 32);
                                        _iconFilter = filterGame ?? string.Empty;
                                        ImGui.Separator();
                                        if (ImGui.BeginTabBar($"##game_tabs_{i}"))
                                        {
                                            for (int t = 0; t < _gameCategories.Length; t++)
                                            {
                                                if (ImGui.BeginTabItem(_gameCategories[t].Name))
                                                {
                                                    _gameTabIndex = t;
                                                    var filter = _iconFilter?.Trim() ?? string.Empty;
                                                    var ids = GetFilteredGameIconIds(t, filter);
                                                    var pageSize = 120;
                                                    var total = ids.Count;
                                                    var maxPage = System.Math.Max(0, (total + pageSize - 1) / pageSize - 1);
                                                    if (_gamePage > maxPage) _gamePage = maxPage;
                                                    var pStart = _gamePage * pageSize;
                                                    var pEnd = System.Math.Min(total, pStart + pageSize);
                                                    var colsB = 10;
                                                    var cellB = System.MathF.Max(36f, ImGui.GetFrameHeight() * 1.6f);
                                                    ImGui.BeginDisabled(_gamePage <= 0);
                                                    if (ImGui.Button("Prev")) _gamePage = System.Math.Max(0, _gamePage - 1);
                                                    ImGui.EndDisabled();
                                                    ImGui.SameLine();
                                                    ImGui.TextUnformatted($"Page {_gamePage + 1} / {maxPage + 1}");
                                                    ImGui.SameLine();
                                                    ImGui.BeginDisabled(_gamePage >= maxPage);
                                                    if (ImGui.Button("Next")) _gamePage = System.Math.Min(maxPage, _gamePage + 1);
                                                    ImGui.EndDisabled();
                                                    ImGui.Separator();
                                                    int shownB = 0;
                                                    for (int idx = pStart; idx < pEnd; idx++)
                                                    {
                                                        var id = ids[idx];
                                                        var wrapB = TryGetGameIconWrap(id);
                                                        if (wrapB == null) continue;
                                                        var clickedPick = ImGui.ImageButton(wrapB.Handle, new Vector2(cellB, cellB));
                                                        if (ImGui.IsItemHovered())
                                                        {
                                                            ImGui.BeginTooltip();
                                                            ImGui.TextUnformatted(id.ToString());
                                                            ImGui.EndTooltip();
                                                        }
                                                        if (clickedPick)
                                                        {
                                                            if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, null, true, id);
                                                            else _ = _app.SetMacroHotbarSlotAsync(i, null, null, null, true, id);
                                                            ImGui.CloseCurrentPopup();
                                                        }
                                                        shownB++;
                                                        if ((shownB % colsB) != 0) ImGui.SameLine();
                                                    }
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
                                    var btnW = ImGui.CalcTextSize("Browse").X + ImGui.GetStyle().FramePadding.X * 2f;
                                    var inputW = System.MathF.Max(140f, System.MathF.Min(260f, ImGui.GetContentRegionAvail().X - btnW - 8f));
                                    var iconKey = (i < slots.Length) ? (slots[i].IconKey ?? string.Empty) : string.Empty;
                                    ImGui.SetNextItemWidth(inputW);
                                    ImGui.InputText($"##icon_font_{i}", ref iconKey, 64);
                                    if (ImGui.IsItemDeactivatedAfterEdit())
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, iconKey, false, null);
                                        else _ = _app.SetMacroHotbarSlotAsync(i, null, null, iconKey, false, null);
                                    }
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Browse##icon_font_{i}"))
                                    {
                                        _iconPickerSlot = i;
                                        _iconFilter = string.Empty;
                                        ImGui.OpenPopup($"macro_icon_browser_{i}");
                                    }

                                    var iconGlyph = VenuePlus.Helpers.IconDraw.ToIconStringFromKey(iconKey);
                                    ImGui.PushFont(UiBuilder.IconFont);
                                    ImGui.TextUnformatted(string.IsNullOrEmpty(iconGlyph) ? "-" : iconGlyph);
                                    ImGui.PopFont();

                                    var popupName = $"macro_icon_browser_{i}";
                                    var maxPopupW = System.MathF.Max(320f, ImGui.GetIO().DisplaySize.X - 80f);
                                    var maxPopupH = System.MathF.Max(240f, ImGui.GetIO().DisplaySize.Y - 120f);
                                    var minPopupW = System.MathF.Min(520f, maxPopupW);
                                    var minPopupH = System.MathF.Min(360f, maxPopupH);
                                    ImGui.SetNextWindowSizeConstraints(new Vector2(minPopupW, minPopupH), new Vector2(maxPopupW, maxPopupH));
                                    if (ImGui.BeginPopup(popupName))
                                    {
                                        var filterIcons = _iconFilter ?? string.Empty;
                                        ImGui.InputTextWithHint("##icon_filter", "Search icons", ref filterIcons, 64);
                                        _iconFilter = filterIcons ?? string.Empty;
                                        ImGui.Separator();
                                        var cols = 10;
                                        var cell = System.MathF.Max(36f, ImGui.GetFrameHeight() * 1.6f);
                                        var childW = System.MathF.Max(240f, System.MathF.Min(ImGui.GetContentRegionAvail().X, maxPopupW - 24f));
                                        var childH = System.MathF.Max(200f, System.MathF.Min(ImGui.GetContentRegionAvail().Y, maxPopupH - 120f));
                                        if (ImGui.BeginChild($"##icon_browser_list_{i}", new Vector2(childW, childH), false, ImGuiWindowFlags.HorizontalScrollbar))
                                        {
                                            int shown = 0;
                                            for (int k = 0; k < _iconKeysCache.Length; k++)
                                            {
                                                var key = _iconKeysCache[k];
                                                if (!string.IsNullOrWhiteSpace(_iconFilter) && key.IndexOf(_iconFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                                                var iconStrPick = VenuePlus.Helpers.IconDraw.ToIconStringFromKey(key);
                                                ImGui.PushFont(UiBuilder.IconFont);
                                                var clickedPick = ImGui.Button(iconStrPick + $"##pick_{i}_{k}", new Vector2(cell, cell));
                                                ImGui.PopFont();
                                                if (ImGui.IsItemHovered())
                                                {
                                                    ImGui.BeginTooltip();
                                                    ImGui.TextUnformatted(key);
                                                    ImGui.EndTooltip();
                                                }
                                                if (clickedPick)
                                                {
                                                    if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, null, key, false, null);
                                                    else _ = _app.SetMacroHotbarSlotAsync(i, null, null, key, false, null);
                                                    ImGui.CloseCurrentPopup();
                                                }
                                                shown++;
                                                if ((shown % cols) != 0) ImGui.SameLine();
                                            }
                                        }
                                        ImGui.EndChild();
                                        ImGui.EndPopup();
                                    }
                                }

                                ImGui.EndTabItem();
                            }

                            if (ImGui.BeginTabItem("Icon Options"))
                            {
                                if (ImGui.BeginTable($"##icon_settings_{i}", 3, ImGuiTableFlags.SizingFixedFit))
                                {
                                    ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthFixed, 260f);
                                    ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 160f);
                                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 140f);

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.SetNextItemWidth(200f);
                                    if (ImGui.Checkbox($"##slot_nobg_{i}", ref noBgSlotSel))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotNoBackgroundAtAsync(_barIndex, i, noBgSlotSel);
                                        else _ = _app.SetMacroHotbarSlotNoBackgroundAsync(i, noBgSlotSel);
                                    }
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted("Hide Button Background");
                                    ImGui.TableSetColumnIndex(2);
                                    if (ImGui.Button($"Reset##slot_nobg_reset_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotNoBackgroundAtAsync(_barIndex, i, false);
                                        else _ = _app.SetMacroHotbarSlotNoBackgroundAsync(i, false);
                                    }

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.SetNextItemWidth(220f);
                                    if (ImGui.SliderFloat($"##slot_iconscale_{i}", ref iconScale, 0.3f, 2.0f))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconScaleAtAsync(_barIndex, i, iconScale);
                                        else _ = _app.SetMacroHotbarSlotIconScaleAsync(i, iconScale);
                                    }
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted("Icon Scale");
                                    ImGui.TableSetColumnIndex(2);
                                    if (ImGui.Button($"Reset##slot_iconscale_reset_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconScaleAtAsync(_barIndex, i, defScale);
                                        else _ = _app.SetMacroHotbarSlotIconScaleAsync(i, defScale);
                                    }
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Set Global##slot_iconscale_global_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarIconScaleDefaultAtAsync(curBar, iconScale);
                                        else _ = _app.SetMacroHotbarIconScaleDefaultAsync(iconScale);
                                    }

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.SetNextItemWidth(100f);
                                    if (ImGui.SliderFloat($"##slot_iconoffx_{i}", ref offXUi, -maxOffX, maxOffX))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconOffsetAtAsync(_barIndex, i, offXUi, offYUi);
                                        else _ = _app.SetMacroHotbarSlotIconOffsetAsync(i, offXUi, offYUi);
                                    }
                                    ImGui.SameLine();
                                    ImGui.TextUnformatted("X");
                                    ImGui.SetNextItemWidth(100f);
                                    if (ImGui.SliderFloat($"##slot_iconoffy_{i}", ref offYUi, -maxOffY, maxOffY))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconOffsetAtAsync(_barIndex, i, offXUi, offYUi);
                                        else _ = _app.SetMacroHotbarSlotIconOffsetAsync(i, offXUi, offYUi);
                                    }
                                    ImGui.SameLine();
                                    ImGui.TextUnformatted("Y");
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted("Icon Offset");
                                    ImGui.TableSetColumnIndex(2);
                                    if (ImGui.Button($"Reset##slot_iconoffset_reset_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconOffsetAtAsync(_barIndex, i, 0f, 0f);
                                        else _ = _app.SetMacroHotbarSlotIconOffsetAsync(i, 0f, 0f);
                                    }

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.SetNextItemWidth(200f);
                                    if (ImGui.Checkbox($"##slot_showframe_{i}", ref showFrameSel))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotShowFrameAtAsync(_barIndex, i, showFrameSel);
                                        else _ = _app.SetMacroHotbarSlotShowFrameAsync(i, showFrameSel);
                                    }
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted("Show Hover Frame");
                                    ImGui.TableSetColumnIndex(2);
                                    if (ImGui.Button($"Reset##slot_showframe_reset_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotShowFrameAtAsync(_barIndex, i, defShowFrame);
                                        else _ = _app.SetMacroHotbarSlotShowFrameAsync(i, defShowFrame);
                                    }
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Set Global##slot_showframe_global_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarShowFrameDefaultAtAsync(curBar, showFrameSel);
                                        else _ = _app.SetMacroHotbarShowFrameDefaultAsync(showFrameSel);
                                    }

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.SetNextItemWidth(-1);
                                    if (ImGui.InputTextWithHint($"##slot_tooltip_{i}", "Hover tooltip text", ref tipSlot, 128))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotTooltipAtAsync(_barIndex, i, tipSlot);
                                        else _ = _app.SetMacroHotbarSlotTooltipAsync(i, tipSlot);
                                    }
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted("Tooltip");
                                    ImGui.TableSetColumnIndex(2);
                                    if (ImGui.Button($"Reset##slot_tooltip_reset_{i}"))
                                    {
                                        var empty = string.Empty;
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotTooltipAtAsync(_barIndex, i, empty);
                                        else _ = _app.SetMacroHotbarSlotTooltipAsync(i, empty);
                                    }

                                    ImGui.EndTable();
                                }

                                ImGui.Separator();
                                ImGui.TextUnformatted("Colors");

                                if (ImGui.BeginTable($"##icon_colors_{i}", 3, ImGuiTableFlags.SizingStretchProp))
                                {
                                    ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthFixed, 200f);
                                    ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthStretch);
                                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 140f);

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    var frameColVec = ImGui.ColorConvertU32ToFloat4(frameColU32);
                                    ImGui.SetNextItemWidth(200f);
                                    if (ImGui.ColorEdit4($"##slot_framecolor_{i}", ref frameColVec, ImGuiColorEditFlags.NoInputs))
                                    {
                                        var colU32 = ImGui.ColorConvertFloat4ToU32(frameColVec);
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotFrameColorAtAsync(_barIndex, i, colU32);
                                        else _ = _app.SetMacroHotbarSlotFrameColorAsync(i, colU32);
                                    }
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted("Hover Frame Color");
                                    ImGui.TableSetColumnIndex(2);
                                    if (ImGui.Button($"Reset##slot_framecolor_reset_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotFrameColorAtAsync(_barIndex, i, null);
                                        else _ = _app.SetMacroHotbarSlotFrameColorAsync(i, null);
                                    }
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Set Global##slot_framecolor_global_{i}"))
                                    {
                                        var colU32 = ImGui.ColorConvertFloat4ToU32(frameColVec);
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarFrameColorDefaultAtAsync(curBar, colU32);
                                        else _ = _app.SetMacroHotbarFrameColorDefaultAsync(colU32);
                                    }

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    var iconColVec = ImGui.ColorConvertU32ToFloat4(iconTintU32);
                                    ImGui.SetNextItemWidth(200f);
                                    if (ImGui.ColorEdit4($"##slot_iconcolor_{i}", ref iconColVec, ImGuiColorEditFlags.NoInputs))
                                    {
                                        var colU32I = ImGui.ColorConvertFloat4ToU32(iconColVec);
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconColorAtAsync(_barIndex, i, colU32I);
                                        else _ = _app.SetMacroHotbarSlotIconColorAsync(i, colU32I);
                                    }
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted("Icon Color");
                                    ImGui.TableSetColumnIndex(2);
                                    if (ImGui.Button($"Reset##slot_iconcolor_reset_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotIconColorAtAsync(_barIndex, i, null);
                                        else _ = _app.SetMacroHotbarSlotIconColorAsync(i, null);
                                    }
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Set Global##slot_iconcolor_global_{i}"))
                                    {
                                        var colU32I = ImGui.ColorConvertFloat4ToU32(iconColVec);
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarIconColorDefaultAtAsync(curBar, colU32I);
                                        else _ = _app.SetMacroHotbarIconColorDefaultAsync(colU32I);
                                    }

                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    var hoverBgVec = ImGui.ColorConvertU32ToFloat4(hoverBgColU32);
                                    ImGui.SetNextItemWidth(200f);
                                    if (ImGui.ColorEdit4($"##slot_hoverbgcolor_{i}", ref hoverBgVec, ImGuiColorEditFlags.NoInputs))
                                    {
                                        var colU32H = ImGui.ColorConvertFloat4ToU32(hoverBgVec);
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotHoverBackgroundColorAtAsync(_barIndex, i, colU32H);
                                        else _ = _app.SetMacroHotbarSlotHoverBackgroundColorAsync(i, colU32H);
                                    }
                                    ImGui.TableSetColumnIndex(1);
                                    ImGui.TextUnformatted("Hover Background Color");
                                    ImGui.TableSetColumnIndex(2);
                                    if (ImGui.Button($"Reset##slot_hoverbgcolor_reset_{i}"))
                                    {
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotHoverBackgroundColorAtAsync(_barIndex, i, null);
                                        else _ = _app.SetMacroHotbarSlotHoverBackgroundColorAsync(i, null);
                                    }
                                    ImGui.SameLine();
                                    if (ImGui.Button($"Set Global##slot_hoverbgcolor_global_{i}"))
                                    {
                                        var colU32H = ImGui.ColorConvertFloat4ToU32(hoverBgVec);
                                        if (_barIndex >= 0) _ = _app.SetMacroHotbarHoverBackgroundColorDefaultAtAsync(curBar, colU32H);
                                        else _ = _app.SetMacroHotbarHoverBackgroundColorDefaultAsync(colU32H);
                                    }

                                    ImGui.EndTable();
                                }

                                ImGui.EndTabItem();
                            }

                            ImGui.EndTabBar();
                        }

                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Options"))
                    {
                        var chSel = ch;
                        if (ImGui.BeginTable($"##options_table_{i}", 2, ImGuiTableFlags.SizingStretchProp))
                        {
                            var entries = new (string label, VenuePlus.Configuration.ChatChannel value)[]
                            {
                                ("Whisper", VenuePlus.Configuration.ChatChannel.Whisper),
                                ("Say", VenuePlus.Configuration.ChatChannel.Say),
                                ("Party", VenuePlus.Configuration.ChatChannel.Party),
                                ("Shout", VenuePlus.Configuration.ChatChannel.Shout),
                                ("Yell", VenuePlus.Configuration.ChatChannel.Yell),
                                ("FC", VenuePlus.Configuration.ChatChannel.FC),
                                ("Echo", VenuePlus.Configuration.ChatChannel.Echo),
                                ("Emote", VenuePlus.Configuration.ChatChannel.Emote),
                            };
                            for (int e = 0; e < entries.Length; e++)
                            {
                                if ((e % 2) == 0)
                                {
                                    ImGui.TableNextRow();
                                }
                                ImGui.TableSetColumnIndex(e % 2);
                                var entry = entries[e];
                                if (ImGui.Selectable(entry.label, chSel == entry.value))
                                {
                                    chSel = entry.value;
                                    if (_barIndex >= 0) _ = _app.SetMacroHotbarSlotAsyncAtBar(_barIndex, i, null, chSel);
                                    else _ = _app.SetMacroHotbarSlotAsync(i, null, chSel);
                                }
                            }
                            ImGui.EndTable();
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Hotbar Settings"))
                    {
                        var lockedLocal2 = locked;
                        var colsCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarColumnsAt(curBar) : _app.GetMacroHotbarColumns();
                        var rowsCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarRowsAt(curBar) : _app.GetMacroHotbarRows();
                        var sideCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarButtonSideAt(curBar) : _app.GetMacroHotbarButtonSide();
                        var spacingXCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarItemSpacingXAt(curBar) : _app.GetMacroHotbarItemSpacingX();
                        var spacingYCtx2 = (_barIndex >= 0) ? _app.GetMacroHotbarItemSpacingYAt(curBar) : _app.GetMacroHotbarItemSpacingY();
                        var noBgLocal2 = noBg;
                        var bgOpt2 = (_barIndex >= 0) ? _app.GetMacroHotbarBackgroundColorAt(curBar) : _app.GetMacroHotbarBackgroundColor();
                        var bgVec2 = bgOpt2.HasValue ? ImGui.ColorConvertU32ToFloat4(bgOpt2.Value) : new Vector4(0f, 0f, 0f, 0.25f);
                        var alpha2 = bgVec2.W;
                        var showPersist2 = System.Array.IndexOf(_app.GetOpenMacroHotbarIndices(), curBar) >= 0;

                        if (ImGui.BeginTable($"##hotbar_settings_{i}", 2, ImGuiTableFlags.SizingStretchProp))
                        {
                            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 180f);
                            ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthStretch);

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Locked");
                            ImGui.TableSetColumnIndex(1);
                            if (ImGui.Checkbox($"##macro_locked_{i}", ref lockedLocal2))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarLockedAtAsync(curBar, lockedLocal2);
                                else _ = _app.SetMacroHotbarLockedAsync(lockedLocal2);
                                locked = lockedLocal2;
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Grid");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.SetNextItemWidth(80f);
                            if (ImGui.SliderInt($"Cols##macro_cols_ctx_slot_{i}", ref colsCtx2, 1, 12))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarColumnsAtAsync(curBar, colsCtx2);
                                else _ = _app.SetMacroHotbarColumnsAsync(colsCtx2);
                            }
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(80f);
                            if (ImGui.SliderInt($"Rows##macro_rows_ctx_slot_{i}", ref rowsCtx2, 1, 12))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarRowsAtAsync(curBar, rowsCtx2);
                                else _ = _app.SetMacroHotbarRowsAsync(rowsCtx2);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Button Size");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.SliderFloat($"##macro_side_ctx_slot_{i}", ref sideCtx2, 16f, 128f))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarButtonSideAtAsync(curBar, sideCtx2);
                                else _ = _app.SetMacroHotbarButtonSideAsync(sideCtx2);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Spacing");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.SetNextItemWidth(80f);
                            if (ImGui.SliderFloat($"X##macro_spx_ctx_slot_{i}", ref spacingXCtx2, 0f, 24f))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarItemSpacingXAtAsync(curBar, spacingXCtx2);
                                else _ = _app.SetMacroHotbarItemSpacingXAsync(spacingXCtx2);
                            }
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(80f);
                            if (ImGui.SliderFloat($"Y##macro_spy_ctx_slot_{i}", ref spacingYCtx2, 0f, 24f))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarItemSpacingYAtAsync(curBar, spacingYCtx2);
                                else _ = _app.SetMacroHotbarItemSpacingYAsync(spacingYCtx2);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Hide Background");
                            ImGui.TableSetColumnIndex(1);
                            if (ImGui.Checkbox($"##macro_nobg_ctx_slot_{i}", ref noBgLocal2))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarNoBackgroundAtAsync(curBar, noBgLocal2);
                                else _ = _app.SetMacroHotbarNoBackgroundAsync(noBgLocal2);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Background Color");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.SetNextItemWidth(-60f);
                            if (ImGui.ColorEdit4($"##bar_bgcolor_{i}", ref bgVec2, ImGuiColorEditFlags.NoInputs))
                            {
                                var colU32 = ImGui.ColorConvertFloat4ToU32(bgVec2);
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarBackgroundColorAtAsync(curBar, colU32);
                                else _ = _app.SetMacroHotbarBackgroundColorAsync(colU32);
                            }
                            ImGui.SameLine();
                            if (ImGui.Button($"Reset##bar_bgcolor_reset_{i}"))
                            {
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarBackgroundColorAtAsync(curBar, null);
                                else _ = _app.SetMacroHotbarBackgroundColorAsync(null);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Background Alpha");
                            ImGui.TableSetColumnIndex(1);
                            ImGui.SetNextItemWidth(-1);
                            if (ImGui.SliderFloat($"##bar_bg_alpha_{i}", ref alpha2, 0f, 1f))
                            {
                                bgVec2.W = alpha2;
                                var colU32A = ImGui.ColorConvertFloat4ToU32(bgVec2);
                                if (_barIndex >= 0) _ = _app.SetMacroHotbarBackgroundColorAtAsync(curBar, colU32A);
                                else _ = _app.SetMacroHotbarBackgroundColorAsync(colU32A);
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted("Show on Startup");
                            ImGui.TableSetColumnIndex(1);
                            if (ImGui.Checkbox($"##macro_startup_ctx_slot_{i}", ref showPersist2))
                            {
                                _ = _app.SetMacroHotbarOpenStateAsync(curBar, showPersist2);
                                if (showPersist2) _app.OpenMacroHotbarWindowAt(curBar);
                                else _app.CloseMacroHotbarWindowAt(curBar);
                            }

                            ImGui.EndTable();
                        }

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

    private System.Collections.Generic.List<int> GetGameIconIdsForTab(int tabIndex)
    {
        EnsureGameIconCache();
        if (_gameIconIdsByTab.TryGetValue(tabIndex, out var cached)) return cached;
        return new System.Collections.Generic.List<int>();
    }
    private bool _gameIconCacheBuilt;

    private void EnsureGameIconCache()
    {
        if (_gameIconCacheBuilt) return;
        _gameIconIdsByTab.Clear();
        for (int t = 0; t < _gameCategories.Length; t++)
        {
            var list = new System.Collections.Generic.List<int>();
            var ranges = _gameCategories[t].Ranges;
            for (int r = 0; r < ranges.Length; r++)
            {
                var start = ranges[r].start;
                var end = ranges[r].end;
                for (int id = start; id <= end; id++)
                {
                    var wrap = TryGetGameIconWrap(id);
                    if (wrap == null) continue;
                    list.Add(id);
                }
            }
            _gameIconIdsByTab[t] = list;
        }
        _gameIconCacheBuilt = true;
    }

    private System.Collections.Generic.List<int> GetFilteredGameIconIds(int tabIndex, string filter)
    {
        var list = GetGameIconIdsForTab(tabIndex);
        if (string.IsNullOrWhiteSpace(filter)) return list;
        if (tabIndex == _gameFilteredTabIndex && string.Equals(filter, _gameFilteredText, System.StringComparison.Ordinal)) return _gameFilteredIds;
        _gameFilteredTabIndex = tabIndex;
        _gameFilteredText = filter;
        _gameFilteredIds.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            var id = list[i];
            if (id.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            _gameFilteredIds.Add(id);
        }
        return _gameFilteredIds;
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
