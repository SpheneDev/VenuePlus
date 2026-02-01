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
    private string _recoveryCode = string.Empty;
    private string _recoveryStatus = string.Empty;
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
                if (ImGui.BeginTabBar("SettingsInnerTabs"))
                {
                    if (ImGui.BeginTabItem("Login Behavior"))
                    {
                        DrawSettingsLoginBehavior();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Appearance"))
                    {
                        DrawSettingsAppearance();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Notifications"))
                    {
                        DrawSettingsNotifications();
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
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
                var btnSize = new System.Numerics.Vector2(160f, 0f);
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

                ImGui.Spacing();
                var baseCol2 = VenuePlus.Helpers.ColorUtil.HexToVec4("#5E81AC");
                var hoverCol2 = new System.Numerics.Vector4(baseCol2.X + 0.08f, baseCol2.Y + 0.08f, baseCol2.Z + 0.08f, 1f);
                var activeCol2 = new System.Numerics.Vector4(baseCol2.X - 0.06f, baseCol2.Y - 0.06f, baseCol2.Z - 0.06f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Button, baseCol2);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverCol2);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeCol2);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availX - wBtn) / 2f);
                var clicked2 = ImGui.Button("Changelog##changelog_btn", btnSize);
                var rectMin2 = ImGui.GetItemRectMin();
                var rectMax2 = ImGui.GetItemRectMax();
                var draw2 = ImGui.GetWindowDrawList();
                var iconStr2 = Dalamud.Interface.FontAwesomeIcon.ListUl.ToIconString();
                ImGui.PushFont(UiBuilder.IconFont);
                var iconSize2 = ImGui.CalcTextSize(iconStr2);
                var yIcon2 = rectMin2.Y + ((rectMax2.Y - rectMin2.Y) - iconSize2.Y) / 2f;
                var xIcon2 = rectMin2.X + ImGui.GetStyle().FramePadding.X;
                draw2.AddText(new System.Numerics.Vector2(xIcon2, yIcon2), ImGui.GetColorU32(ImGuiCol.Text), iconStr2);
                ImGui.PopFont();
                ImGui.PopStyleColor(3);
                if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Open plugin changelog"); ImGui.EndTooltip(); }
                if (clicked2) { _app.OpenChangelogWindow(); }
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
        DrawHelpIcon("Change your login password.");
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
        DrawHelpIcon("Updates the password used to login.");
        if (!string.IsNullOrEmpty(_staffPassStatus)) ImGui.TextUnformatted(_staffPassStatus);
        ImGui.Spacing();
        ImGui.TextDisabled("Recovery");
        ImGui.SameLine();
        DrawHelpIcon("Generate a recovery code to reset your password.");
        ImGui.Spacing();
        if (ImGui.Button("Generate Recovery Code"))
        {
            _recoveryStatus = "Submitting...";
            System.Threading.Tasks.Task.Run(async () =>
            {
                var code = await _app.GenerateRecoveryCodeAsync();
                if (!string.IsNullOrWhiteSpace(code))
                {
                    _recoveryCode = code;
                    _recoveryStatus = "Recovery code generated";
                }
                else
                {
                    _recoveryStatus = "Generation failed";
                }
            });
        }
        ImGui.SameLine();
        DrawHelpIcon("Use the code in Password Recovery to reset your password.");
        if (!string.IsNullOrEmpty(_recoveryStatus)) ImGui.TextUnformatted(_recoveryStatus);
        if (!string.IsNullOrWhiteSpace(_recoveryCode))
        {
            ImGui.TextUnformatted(_recoveryCode);
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.9f);
            if (ImGui.Button(FontAwesomeIcon.Copy.ToIconString() + "##copy_recovery_code"))
            {
                ImGui.SetClipboardText(_recoveryCode);
            }
            ImGui.SetWindowFontScale(1f);
            ImGui.PopFont();
        }
    }

    private void DrawSettingsLoginBehavior()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Control login privacy and automation.");
        ImGui.TextDisabled("Store an encrypted password locally and auto-login on startup.");
        ImGui.Spacing();
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
        DrawHelpIcon("Stores your password locally (encrypted) to simplify login.");
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
    }

    private void DrawSettingsAppearance()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Customize how VIP information appears in-game.");
        ImGui.TextDisabled("Nameplate options affect only visual presentation.");
        ImGui.Spacing();
        var showNameplate = _app.ShowVipNameplateHook;
        if (ImGui.Checkbox("Show VIP Indicator in nameplate", ref showNameplate))
        {
            _app.SetShowVipNameplateHookAsync(showNameplate).GetAwaiter().GetResult();
        }
        ImGui.SameLine();
        DrawHelpIcon("Adds a colored symbol before VIP names in the nameplate.");
        ImGui.TextDisabled("Choose symbol & position.");
        ImGui.Spacing();
        var posOptions = new string[] { "Before name", "After name", "Before and after" };
        int posIndex = _app.VipStarPosition == VenuePlus.Configuration.VipStarPosition.Left ? 0 : (_app.VipStarPosition == VenuePlus.Configuration.VipStarPosition.Right ? 1 : 2);
        ImGui.PushItemWidth(160f);
        if (ImGui.Combo("##vip_star_position", ref posIndex, posOptions, posOptions.Length))
        {
            var newPos = posIndex == 0 ? VenuePlus.Configuration.VipStarPosition.Left : (posIndex == 1 ? VenuePlus.Configuration.VipStarPosition.Right : VenuePlus.Configuration.VipStarPosition.Both);
            _app.SetVipStarPositionAsync(newPos).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();
        var preset = new string[] { "None", "★", "☆", "♥", "♡" , "◆", "◇" };
        int presetIndex = 0;
        var currentSym = _app.VipStarChar;
        for (int i = 1; i < preset.Length; i++) { if (string.Equals(currentSym, preset[i], System.StringComparison.Ordinal)) { presetIndex = i; break; } }
        ImGui.PushItemWidth(160f);
        if (ImGui.Combo("##vip_star_preset", ref presetIndex, preset, preset.Length))
        {
            var chosen = presetIndex == 0 ? string.Empty : preset[presetIndex];
            _app.SetVipStarCharAsync(chosen).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.PushItemWidth(140f);
        var starChar = _app.VipStarChar;
        if (ImGui.InputText("##vip_star_char", ref starChar, 16))
        {
            _app.SetVipStarCharAsync(starChar).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        DrawHelpIcon("Symbol preset or custom text; 'None' disables the symbol.");

        ImGui.Spacing();
        var lblEnabled = _app.VipTextEnabled;
        if (ImGui.Checkbox("Show label text", ref lblEnabled))
        {
            _app.SetVipTextEnabledAsync(lblEnabled).GetAwaiter().GetResult();
        }
        ImGui.SameLine();
        DrawHelpIcon("Shows custom label text near the nameplate (e.g., [VIP]).");
        ImGui.PushItemWidth(220f);
        var lblText = _app.VipLabelText;
        if (ImGui.InputText("##vip_label_text", ref lblText, 32))
        {
            _app.SetVipLabelTextAsync(lblText).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();

        var orderOptions = new string[] { "Symbol + Text", "Text + Symbol" };
        int orderIndex = _app.VipLabelOrder == VenuePlus.Configuration.VipLabelOrder.SymbolThenText ? 0 : 1;
        ImGui.PushItemWidth(160f);
        if (ImGui.Combo("##vip_label_order", ref orderIndex, orderOptions, orderOptions.Length))
        {
            var newOrder = orderIndex == 0 ? VenuePlus.Configuration.VipLabelOrder.SymbolThenText : VenuePlus.Configuration.VipLabelOrder.TextThenSymbol;
            _app.SetVipLabelOrderAsync(newOrder).GetAwaiter().GetResult();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        DrawHelpIcon("Choose composition when both symbol and label are enabled.");
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
        DrawHelpIcon("Change color of VIP symbol in nameplate.");
        ImGui.PopItemWidth();
    }

    private void DrawSettingsNotifications()
    {
        var prefs = _app.GetNotificationPreferences();
        ImGui.BeginChild("NotifContentInner", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
        var modes = new string[] { "None", "Chat", "Toast", "Both" };
        ImGui.TextUnformatted("Choose how and which notifications are shown.");
        ImGui.TextDisabled("Select a display mode, then enable categories you care about.");
        ImGui.Spacing();
        int modeIndex = (int)prefs.DisplayMode;
        ImGui.TextUnformatted("Display Mode");
        ImGui.SameLine();
        ImGui.PushItemWidth(160f);
        if (ImGui.Combo("##notif_display_mode_inner", ref modeIndex, modes, modes.Length))
        {
            prefs.DisplayMode = (VenuePlus.Configuration.NotificationDisplayMode)modeIndex;
            _app.SavePluginConfig();
        }
        ImGui.PopItemWidth();
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0, 0, 0, 0));
        if (ImGui.CollapsingHeader("Account & Access", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Login results and password prompts.");
            var s1 = prefs.ShowLoginSuccess; if (ImGui.Checkbox("Show login success", ref s1)) { prefs.ShowLoginSuccess = s1; _app.SavePluginConfig(); }
            var s2 = prefs.ShowLoginFailed; if (ImGui.Checkbox("Show login failed", ref s2)) { prefs.ShowLoginFailed = s2; _app.SavePluginConfig(); }
            var s3 = prefs.ShowPasswordRequired; if (ImGui.Checkbox("Show password required", ref s3)) { prefs.ShowPasswordRequired = s3; _app.SavePluginConfig(); }
        }
        if (ImGui.CollapsingHeader("Roles & Ownership", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Role changes for you and ownership events.");
            var r1 = prefs.ShowRoleChangedSelf; if (ImGui.Checkbox("Show role changed (self)", ref r1)) { prefs.ShowRoleChangedSelf = r1; _app.SavePluginConfig(); }
            var r2 = prefs.ShowOwnershipGranted; if (ImGui.Checkbox("Show ownership granted", ref r2)) { prefs.ShowOwnershipGranted = r2; _app.SavePluginConfig(); }
            var r3 = prefs.ShowOwnershipTransferred; if (ImGui.Checkbox("Show ownership transferred", ref r3)) { prefs.ShowOwnershipTransferred = r3; _app.SavePluginConfig(); }
        }
        if (ImGui.CollapsingHeader("Membership", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Join or removal from venues affecting your account.");
            var m1 = prefs.ShowMembershipJoined; if (ImGui.Checkbox("Show membership joined", ref m1)) { prefs.ShowMembershipJoined = m1; _app.SavePluginConfig(); }
            var m2 = prefs.ShowMembershipRemoved; if (ImGui.Checkbox("Show membership removed", ref m2)) { prefs.ShowMembershipRemoved = m2; _app.SavePluginConfig(); }
        }
        if (ImGui.CollapsingHeader("VIPs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("VIP list changes: added or removed entries.");
            var v1 = prefs.ShowVipAdded; if (ImGui.Checkbox("Show VIP added", ref v1)) { prefs.ShowVipAdded = v1; _app.SavePluginConfig(); }
            var v2 = prefs.ShowVipRemoved; if (ImGui.Checkbox("Show VIP removed", ref v2)) { prefs.ShowVipRemoved = v2; _app.SavePluginConfig(); }
        }
        if (ImGui.CollapsingHeader("DJs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("DJ roster changes in the current venue.");
            var d0 = prefs.ShowDjAdded; if (ImGui.Checkbox("Show DJ added", ref d0)) { prefs.ShowDjAdded = d0; _app.SavePluginConfig(); }
            var d1 = prefs.ShowDjRemoved; if (ImGui.Checkbox("Show DJ removed", ref d1)) { prefs.ShowDjRemoved = d1; _app.SavePluginConfig(); }
        }
        // TODO: Comming soon
        //if (ImGui.CollapsingHeader("Shifts", ImGuiTreeNodeFlags.DefaultOpen))
        //{
        //    ImGui.TextDisabled("Shift schedule updates and removals.");
        //    var sh1 = prefs.ShowShiftCreated; if (ImGui.Checkbox("Show shift created", ref sh1)) { prefs.ShowShiftCreated = sh1; _app.SavePluginConfig(); }
        //    var sh2 = prefs.ShowShiftUpdated; if (ImGui.Checkbox("Show shift updated", ref sh2)) { prefs.ShowShiftUpdated = sh2; _app.SavePluginConfig(); }
        //    var sh3 = prefs.ShowShiftRemoved; if (ImGui.Checkbox("Show shift removed", ref sh3)) { prefs.ShowShiftRemoved = sh3; _app.SavePluginConfig(); }
        //}
        ImGui.PopStyleColor(3);
        ImGui.EndChild();
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

