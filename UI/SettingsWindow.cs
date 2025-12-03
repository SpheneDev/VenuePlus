using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VenuePlus.Plugin;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using VenuePlus.Helpers;
using System.Net.Http;

namespace VenuePlus.UI;

public sealed class SettingsWindow : Window, System.IDisposable
{
    private readonly VenuePlusApp _app;
    private string _rememberStatus = string.Empty;
    private string _autoStatus = string.Empty;
    private bool _selectAccountOnOpen;
    private string _staffNewPassword = string.Empty;
    private string _staffPassStatus = string.Empty;
    private readonly Dalamud.Plugin.Services.ITextureProvider _textureProvider;
    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? _aboutTex;
    private bool _aboutRequested;

    public SettingsWindow(VenuePlusApp app, Dalamud.Plugin.Services.ITextureProvider textureProvider) : base("Venue Plus Settings")
    {
        _app = app;
        _textureProvider = textureProvider;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(350f, 500f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public override void Draw()
    {
        var req = _app.ConsumeRequestedSettingsTab();
        _selectAccountOnOpen = string.Equals(req, "Account", System.StringComparison.Ordinal);

        if (ImGui.BeginTabBar("SettingsTabs"))
        {
            if (ImGui.BeginTabItem("Settings", ImGuiTabItemFlags.None))
            {
                DrawPluginOptions();
                ImGui.EndTabItem();
            }

            var accFlags = _selectAccountOnOpen ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Account Settings", accFlags))
            {
                DrawAccountSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("About"))
            {
                var ver = typeof(VenuePlus.Plugin.Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";
                var availX = ImGui.GetContentRegionAvail().X;
                if (_aboutTex == null && !_aboutRequested)
                {
                    _aboutRequested = true;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            using var http = new HttpClient();
                            var bytes = await http.GetByteArrayAsync("https://raw.githubusercontent.com/SpheneDev/repo/refs/heads/main/images/venueplus.png");
                            var tex = await _textureProvider.CreateFromImageAsync(bytes);
                            _aboutTex = tex;
                        }
                        catch { }
                    });
                }
                if (_aboutTex != null)
                {
                    var desiredW = 220f;
                    var desiredH = 220f;
                    var xImg = (availX - desiredW) / 2f;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + xImg);
                    ImGui.Image(_aboutTex.Handle, new Vector2(desiredW, desiredH));
                    if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Venue Plus"); ImGui.EndTooltip(); }
                    ImGui.Spacing();
                }
                var tTitle = "About Venue Plus";
                var wTitle = ImGui.CalcTextSize(tTitle).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wTitle) / 2f);
                ImGui.TextUnformatted(tTitle);
                ImGui.Separator();
                var tVer = $"Version: {ver}";
                var wVer = ImGui.CalcTextSize(tVer).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wVer) / 2f);
                ImGui.TextUnformatted(tVer);
                var tAuth = "Authors: Keqing Yu & SpheneDev";
                var wAuth = ImGui.CalcTextSize(tAuth).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wAuth) / 2f);
                ImGui.TextUnformatted(tAuth);
                ImGui.Spacing();
                var tDesc = "Manage your venue: VIPs, staff and DJs with fast actions.";
                var wDesc = ImGui.CalcTextSize(tDesc).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wDesc) / 2f);
                ImGui.TextDisabled(tDesc);
                ImGui.Spacing();

                var baseCol = VenuePlus.Helpers.ColorUtil.HexToVec4("#FF5E5B");
                var hoverCol = new System.Numerics.Vector4(baseCol.X + 0.08f, baseCol.Y + 0.08f, baseCol.Z + 0.08f, 1f);
                var activeCol = new System.Numerics.Vector4(baseCol.X - 0.06f, baseCol.Y - 0.06f, baseCol.Z - 0.06f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, baseCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeCol);
                var btnSize = new System.Numerics.Vector2(140f, 0f);
                var wBtn = btnSize.X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wBtn) / 2f);
                var clicked = ImGui.Button("Ko-fi##kofi_btn", btnSize);
                var rectMin = ImGui.GetItemRectMin();
                var rectMax = ImGui.GetItemRectMax();
                var draw = ImGui.GetWindowDrawList();
                var iconStr = Dalamud.Interface.FontAwesomeIcon.Coffee.ToIconString();
                ImGui.PushFont(UiBuilder.IconFont);
                var iconSize = ImGui.CalcTextSize(iconStr);
                var yIcon = rectMin.Y + ((rectMax.Y - rectMin.Y) - iconSize.Y) / 2f;
                var xIcon = rectMin.X + ImGui.GetStyle().FramePadding.X;
                draw.AddText(new System.Numerics.Vector2(xIcon, yIcon), ImGui.GetColorU32(ImGuiCol.Text), iconStr);
                ImGui.PopFont();
                ImGui.PopStyleColor(3);
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Support Sphene Dev on Ko-fi"); ImGui.EndTooltip(); }
                if (clicked)
                {
                    var url = "https://ko-fi.com/sphenedev";
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
                    });
                }
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawAccountSettings()
    {
        var canUse = _app.HasStaffSession;
        if (!canUse)
        {
            ImGui.TextUnformatted("Login to use account settings");
            return;
        }

        var info = _app.GetCurrentCharacter();
        var name = info.HasValue ? info.Value.name : "—";
        var world = info.HasValue ? info.Value.world : "—";
        var suggested = (info.HasValue && !string.IsNullOrWhiteSpace(info.Value.name) && !string.IsNullOrWhiteSpace(info.Value.world)) ? (info.Value.name + "@" + info.Value.world) : "—";
        var staffUser = string.IsNullOrWhiteSpace(_app.CurrentStaffUsername) ? "Not logged in" : _app.CurrentStaffUsername;
        var staffJob = string.IsNullOrWhiteSpace(_app.CurrentStaffJob) ? "—" : _app.CurrentStaffJob;
        var uidDisplay = string.IsNullOrWhiteSpace(_app.CurrentStaffUid) ? "—" : _app.CurrentStaffUid;

        ImGui.TextUnformatted("Account Information");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Columns(2, "acc-info", false);
        ImGui.SetColumnWidth(0, 160f);
        ImGui.TextUnformatted("Character");
        ImGui.NextColumn();
        ImGui.TextUnformatted(name);
        ImGui.NextColumn();
        ImGui.TextUnformatted("Home World");
        ImGui.NextColumn();
        ImGui.TextUnformatted(world);
        ImGui.NextColumn();
        ImGui.TextUnformatted("Suggested Username");
        ImGui.NextColumn();
        ImGui.TextUnformatted(suggested);
        ImGui.NextColumn();
        ImGui.TextUnformatted("User UID");
        ImGui.NextColumn();
        ImGui.TextUnformatted(uidDisplay);
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetWindowFontScale(0.9f);
        if (ImGui.Button(FontAwesomeIcon.Copy.ToIconString() + "##copy_uid_settings"))
        {
            if (!string.IsNullOrWhiteSpace(uidDisplay)) ImGui.SetClipboardText(uidDisplay);
        }
        ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
        ImGui.Columns(1);
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("Security");
        ImGui.SameLine();
        DrawHelpIcon("Change your staff login password.");
        ImGui.Spacing();

        ImGui.PushItemWidth(150f);
        ImGui.InputTextWithHint("##change_login_password_settings", "Change Login Password", ref _staffNewPassword, 64, ImGuiInputTextFlags.Password);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Save Password"))
        {
            _staffPassStatus = "Submitting...";
            var text = _staffNewPassword;
            System.Threading.Tasks.Task.Run(async () =>
            {
                var ok = await _app.StaffSetOwnPasswordAsync(text);
                _staffPassStatus = ok ? "Password updated" : "Update failed";
                if (ok) _staffNewPassword = string.Empty;
            });
        }
        ImGui.SameLine();
        DrawHelpIcon("Updates the password used to login as staff.");
        if (!string.IsNullOrEmpty(_staffPassStatus)) ImGui.TextUnformatted(_staffPassStatus);
    }

    private void DrawPluginOptions()
    {
        ImGui.Spacing();

        ImGui.TextUnformatted("Login Behavior");
        ImGui.Separator();
        var remember = _app.RememberStaffLogin;
        if (ImGui.Checkbox("Remember Login Password", ref remember))
        {
            _rememberStatus = "Submitting...";
            System.Threading.Tasks.Task.Run(async () =>
            {
                var ok = await _app.SetRememberStaffLoginAsync(remember);
                _rememberStatus = ok ? "Saved" : "Failed";
            });
        }
        ImGui.SameLine();
        DrawHelpIcon("Stores your password locally to simplify login.");
        var auto = _app.AutoLoginEnabled;
        if (ImGui.Checkbox("Auto login enabled", ref auto))
        {
            _autoStatus = "Submitting...";
            System.Threading.Tasks.Task.Run(async () =>
            {
                var ok = await _app.SetAutoLoginEnabledAsync(auto);
                _autoStatus = ok ? "Saved" : "Failed";
            });
        }
        ImGui.SameLine();
        DrawHelpIcon("Attempts automatic login on plugin start.");
        if (!string.IsNullOrEmpty(_rememberStatus)) ImGui.TextUnformatted(_rememberStatus);
        if (!string.IsNullOrEmpty(_autoStatus)) ImGui.TextUnformatted(_autoStatus);


        ImGui.Spacing();
        ImGui.TextUnformatted("VIP Nameplate");
        ImGui.Separator();
        var showOverlay = _app.ShowVipOverlay;
        if (ImGui.Checkbox("Show VIP overlay on target", ref showOverlay))
        {
            _app.SetShowVipOverlayAsync(showOverlay).GetAwaiter().GetResult();
        }
        ImGui.SameLine();
        DrawHelpIcon("Displays VIP info overlay on the current target.");
        var showNameplate = _app.ShowVipNameplateHook;
        if (ImGui.Checkbox("Show VIP star in nameplate", ref showNameplate))
        {
            _app.SetShowVipNameplateHookAsync(showNameplate).GetAwaiter().GetResult();
        }
        ImGui.SameLine();
        DrawHelpIcon("Adds a colored star before VIP names in the nameplate.");
        ImGui.PushItemWidth(220f);
        var sliderIndex = GetSliderIndexFromActualKey((int)_app.VipStarColorKey);
        var sliderChanged = ImGui.SliderInt("##vip_star_color_key", ref sliderIndex, 0, 89);
        var displayActual = GetActualKeyFromSliderIndex(sliderIndex);
        if (sliderChanged)
        {
            _app.SetVipStarColorKeyAsync(displayActual).GetAwaiter().GetResult();
        }
        ImGui.SameLine();
        Vector4 rgbBox;
        try
        {
            var rgbaCur = new UIForegroundPayload(_app.VipStarColorKey).RGBA;
            var rCur = (byte)((rgbaCur >> 24) & 0xFF);
            var gCur = (byte)((rgbaCur >> 16) & 0xFF);
            var bCur = (byte)((rgbaCur >> 8) & 0xFF);
            rgbBox = new Vector4(rCur / 255f, gCur / 255f, bCur / 255f, 1f);
        }
        catch { rgbBox = new Vector4(1f, 0.84f, 0f, 1f); }
        ImGui.ColorButton("##vip_star_preview_settings", rgbBox, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop | ImGuiColorEditFlags.NoBorder, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()));
        ImGui.SameLine();
        DrawHelpIcon("Change color of VIP star in nameplate.");
        ImGui.PopItemWidth();

    }

    private static void DrawHelpIcon(string text)
    {
        IconDraw.IconText(FontAwesomeIcon.QuestionCircle, text);
    }

    private static int GetSliderIndexFromActualKey(int actual)
    {
        if (actual >= 0 && actual <= 77) return actual;
        if (actual >= 500 && actual <= 511) return 78 + (actual - 500);
        return 43;
    }

    private static ushort GetActualKeyFromSliderIndex(int index)
    {
        if (index <= 77) return (ushort)index;
        var off = index - 78;
        return (ushort)(500 + off);
    }

    public void Dispose()
    {
        try { _aboutTex?.Dispose(); } catch { }
        _aboutTex = null;
    }
}

