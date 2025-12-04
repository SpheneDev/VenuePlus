using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VenuePlus.Plugin;
using VenuePlus.Services;
using VenuePlus.Helpers;

namespace VenuePlus.UI;

public sealed class ChangelogWindow : Window, System.IDisposable
{
    private readonly VenuePlusApp _app;
    private readonly ChangelogService _service;
    private readonly string _currentVersion;
    private System.Collections.Generic.List<ChangelogEntry>? _entries;
    private Task? _fetchTask;

    public ChangelogWindow(VenuePlusApp app, ChangelogService service, string currentVersion) : base("Changelog")
    {
        _app = app;
        _service = service;
        _currentVersion = currentVersion;
        Size = new Vector2(860f, 520f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(840f, 480f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        RespectCloseHotkey = true;
    }

    public override void Draw()
    {
        if (_entries == null && _fetchTask == null)
        {
            _fetchTask = Task.Run(async () =>
            {
                var list = await _service.FetchAsync("https://sphene.online/venueplus/changelog.json");
                var filtered = list.Where(e => CompareVersion(e.Version, _currentVersion) <= 0).ToList();
                filtered.Sort((a, b) => -CompareVersion(a.Version, b.Version));
                _entries = filtered;
            });
        }

        if (_entries == null)
        {
            ImGui.TextUnformatted("Loading changelog...");
            return;
        }

        ImGui.BeginChild("chlog_scroll", new Vector2(0, ImGui.GetContentRegionAvail().Y), false);
        if (_entries.Count > 0)
        {
            var latest = _entries[0];
            var latestHeaderCol = ColorUtil.HexToVec4("#5E81AC");
            var latestHoverCol = new Vector4(latestHeaderCol.X + 0.08f, latestHeaderCol.Y + 0.08f, latestHeaderCol.Z + 0.08f, 1f);
            var latestActiveCol = new Vector4(latestHeaderCol.X - 0.06f, latestHeaderCol.Y - 0.06f, latestHeaderCol.Z - 0.06f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Header, latestHeaderCol);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, latestHoverCol);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, latestActiveCol);
            var openLatest = ImGui.CollapsingHeader($"Latest v{latest.Version}##chlog_latest", ImGuiTreeNodeFlags.DefaultOpen);
            ImGui.PopStyleColor(3);
            if (openLatest)
            {
                var title = string.IsNullOrWhiteSpace(latest.Title) ? latest.Version : latest.Title;
                var titleCol = ColorUtil.HexToVec4("#88C0D0");
                ImGui.TextColored(titleCol, title);
                ImGui.SameLine();
                ImGui.TextDisabled($"v{latest.Version}");
                if (!string.IsNullOrWhiteSpace(latest.Date)) { ImGui.SameLine(); ImGui.TextDisabled(latest.Date!); }
                if (!string.IsNullOrWhiteSpace(latest.Description)) { ImGui.PushTextWrapPos(); ImGui.TextWrapped(latest.Description!); ImGui.PopTextWrapPos(); }
                for (int i = 0; i < latest.Sections.Count; i++)
                {
                    var sec = latest.Sections[i];
                    if (!string.IsNullOrWhiteSpace(sec.Description))
                    {
                        var descLower = sec.Description.ToLowerInvariant();
                        var secCol = descLower.Contains("fixed") ? ColorUtil.HexToVec4("#8FBCBB")
                                    : descLower.Contains("improved") ? ColorUtil.HexToVec4("#5E81AC")
                                    : descLower.Contains("removed") ? ColorUtil.HexToVec4("#BF616A")
                                    : descLower.Contains("added") ? ColorUtil.HexToVec4("#A3BE8C")
                                    : descLower.Contains("changed") ? ColorUtil.HexToVec4("#EBCB8B")
                                    : descLower.Contains("ui") ? ColorUtil.HexToVec4("#B48EAD")
                                    : ColorUtil.HexToVec4("#D8DEE9");
                        ImGui.TextColored(secCol, sec.Description);
                    }
                    for (int j = 0; j < sec.Items.Count; j++)
                    {
                        ImGui.Bullet();
                        ImGui.SameLine();
                        ImGui.PushTextWrapPos();
                        ImGui.TextWrapped(sec.Items[j]);
                        ImGui.PopTextWrapPos();
                    }
                }
                ImGui.Separator();
            }
            var openOlder = ImGui.CollapsingHeader("Older Versions##chlog_older", ImGuiTreeNodeFlags.None);
            if (openOlder)
            {
                for (int idx = 1; idx < _entries.Count; idx++)
                {
                    var e = _entries[idx];
                    var hdr = $"{(string.IsNullOrWhiteSpace(e.Title) ? e.Version : e.Title)} v{e.Version}##chlog_{e.Version}";
                    var openOne = ImGui.CollapsingHeader(hdr, ImGuiTreeNodeFlags.None);
                    if (!openOne) continue;
                    var title = string.IsNullOrWhiteSpace(e.Title) ? e.Version : e.Title;
                    var titleCol = ColorUtil.HexToVec4("#88C0D0");
                    ImGui.TextColored(titleCol, title);
                    ImGui.SameLine();
                    ImGui.TextDisabled($"v{e.Version}");
                    if (!string.IsNullOrWhiteSpace(e.Date)) { ImGui.SameLine(); ImGui.TextDisabled(e.Date!); }
                    if (!string.IsNullOrWhiteSpace(e.Description)) { ImGui.PushTextWrapPos(); ImGui.TextWrapped(e.Description!); ImGui.PopTextWrapPos(); }
                    for (int i = 0; i < e.Sections.Count; i++)
                    {
                        var sec = e.Sections[i];
                        if (!string.IsNullOrWhiteSpace(sec.Description))
                        {
                            var descLower = sec.Description.ToLowerInvariant();
                            var secCol = descLower.Contains("fixed") ? ColorUtil.HexToVec4("#8FBCBB")
                                        : descLower.Contains("improved") ? ColorUtil.HexToVec4("#5E81AC")
                                        : descLower.Contains("removed") ? ColorUtil.HexToVec4("#BF616A")
                                        : descLower.Contains("added") ? ColorUtil.HexToVec4("#A3BE8C")
                                        : descLower.Contains("changed") ? ColorUtil.HexToVec4("#EBCB8B")
                                        : descLower.Contains("ui") ? ColorUtil.HexToVec4("#B48EAD")
                                        : ColorUtil.HexToVec4("#D8DEE9");
                            ImGui.TextColored(secCol, sec.Description);
                        }
                        for (int j = 0; j < sec.Items.Count; j++)
                        {
                            ImGui.Bullet();
                            ImGui.SameLine();
                            ImGui.PushTextWrapPos();
                            ImGui.TextWrapped(sec.Items[j]);
                            ImGui.PopTextWrapPos();
                        }
                    }
                    ImGui.Separator();
                }
            }
        }
        ImGui.EndChild();
    }

    private static int CompareVersion(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return 0;
        if (string.IsNullOrWhiteSpace(a)) return -1;
        if (string.IsNullOrWhiteSpace(b)) return 1;
        if (System.Version.TryParse(a, out var va) && System.Version.TryParse(b, out var vb))
        {
            return va.CompareTo(vb);
        }
        return string.Compare(a, b, StringComparison.Ordinal);
    }

    public void Dispose()
    {
    }
}
