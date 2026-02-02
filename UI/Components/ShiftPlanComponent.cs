using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using VenuePlus.Plugin;
using VenuePlus.State;
using VenuePlus.Helpers;

namespace VenuePlus.UI.Components;

public sealed class ShiftPlanComponent
{
    private string _filter = string.Empty;
    private bool _openAddForm;
    private string _pendingTitle = string.Empty;
    private string _pendingStart = string.Empty;
    private string _pendingEnd = string.Empty;
    private string _pendingAssignedUid = string.Empty;
    private string _pendingJob = string.Empty;
    private string _status = string.Empty;
    private Guid _editingId = Guid.Empty;
    private VenuePlus.State.StaffUser[] _staffUsers = Array.Empty<VenuePlus.State.StaffUser>();
    private string[] _jobs = Array.Empty<string>();
    private DateTimeOffset _lastUsersFetch = DateTimeOffset.MinValue;
    private DateTimeOffset _lastJobsFetch = DateTimeOffset.MinValue;
    private int _selUserIdx = -1;
    private string? _selUid;
    private string? _selJob;
    private int _selDjIdx = -1;
    private string? _selDjName;
    private int _startYear;
    private int _startMonth;
    private int _startDay;
    private int _startHour;
    private int _startMinute;
    private int _endYear;
    private int _endMonth;
    private int _endDay;
    private int _endHour;
    private int _endMinute;
    private int _durationMinutes = 120;
    private readonly int[] _durationOptions = new[] { 30, 60, 90, 120, 180, 240, 360, 480 };
    private int _viewYear;
    private int _viewMonth;
    private int _selectedDayMain;
    private bool _listMode;
    private const string CalendarMarkerHex = "#4CAF50";

    public void OpenAddForm()
    {
        _openAddForm = true;
        _status = string.Empty;
        _editingId = Guid.Empty;
        _pendingTitle = string.Empty;
        _pendingStart = string.Empty;
        _pendingEnd = string.Empty;
        _pendingAssignedUid = string.Empty;
        _pendingJob = string.Empty;
        _selUserIdx = -1;
        _selUid = null;
        _selJob = null;
        _selDjIdx = -1;
        _selDjName = null;
        var now = DateTime.Now;
        _startYear = now.Year;
        _startMonth = now.Month;
        _startDay = now.Day;
        _startHour = now.Hour;
        _startMinute = (now.Minute / 5) * 5;
        var endLocal = now.AddHours(2);
        _endYear = endLocal.Year;
        _endMonth = endLocal.Month;
        _endDay = endLocal.Day;
        _endHour = endLocal.Hour;
        _endMinute = (endLocal.Minute / 5) * 5;
    }

    public void CloseAddForm()
    {
        _openAddForm = false;
        _status = string.Empty;
        _editingId = Guid.Empty;
        _pendingTitle = string.Empty;
        _pendingStart = string.Empty;
        _pendingEnd = string.Empty;
        _pendingAssignedUid = string.Empty;
        _pendingJob = string.Empty;
        _selUserIdx = -1;
        _selUid = null;
        _selJob = null;
        _selDjIdx = -1;
        _selDjName = null;
    }

    private void EnsureLookups(VenuePlusApp app)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastUsersFetch == DateTimeOffset.MinValue || (now - _lastUsersFetch).TotalSeconds > 2 || _staffUsers.Length == 0)
        {
            _lastUsersFetch = now;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try { var det = await app.ListStaffUsersDetailedAsync(); _staffUsers = det ?? Array.Empty<VenuePlus.State.StaffUser>(); } catch { }
            });
        }
        if (_lastJobsFetch == DateTimeOffset.MinValue || (now - _lastJobsFetch).TotalSeconds > 5 || _jobs.Length == 0)
        {
            _lastJobsFetch = now;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try { var arr = await app.ListJobsAsync(); _jobs = arr ?? Array.Empty<string>(); } catch { }
            });
        }
    }

    private static int DaysInMonth(int year, int month)
    {
        if (month < 1) month = 1; if (month > 12) month = 12;
        if (year < 1970) year = 1970; if (year > 2100) year = 2100;
        return DateTime.DaysInMonth(year, month);
    }

    private void DrawCalendar(string id, ref int year, ref int month, ref int day)
    {
        var style = ImGui.GetStyle();
        ImGui.PushID(id);
        ImGui.BeginGroup();
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");
        var btnPrev = ImGui.Button("<");
        ImGui.SameLine();
        ImGui.TextUnformatted(monthName);
        ImGui.SameLine();
        var btnNext = ImGui.Button(">");
        if (btnPrev)
        {
            month -= 1; if (month <= 0) { month = 12; year -= 1; }
            var maxD = DaysInMonth(year, month); if (day > maxD) day = maxD;
        }
        if (btnNext)
        {
            month += 1; if (month >= 13) { month = 1; year += 1; }
            var maxD = DaysInMonth(year, month); if (day > maxD) day = maxD;
        }
        var first = new DateTime(year, month, 1);
        int startCol = ((int)first.DayOfWeek + 6) % 7;
        int days = DaysInMonth(year, month);
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings;
        if (ImGui.BeginTable("cal_" + id, 7, flags))
        {
            string[] wk = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            for (int c = 0; c < 7; c++) { ImGui.TableSetColumnIndex(c); ImGui.TextUnformatted(wk[c]); }
            int curDay = 1;
            for (int r = 0; r < 6 && curDay <= days; r++)
            {
                ImGui.TableNextRow();
                for (int c = 0; c < 7; c++)
                {
                    ImGui.TableSetColumnIndex(c);
                    if (r == 0 && c < startCol) { ImGui.TextUnformatted(""); continue; }
                    if (curDay > days) { ImGui.TextUnformatted(""); continue; }
                    bool sel = day == curDay;
                    var label = curDay.ToString();
                    if (ImGui.Selectable(label, sel, ImGuiSelectableFlags.AllowDoubleClick, new System.Numerics.Vector2(28f, 0f))) { day = curDay; }
                    curDay++;
                }
            }
            ImGui.EndTable();
        }
        ImGui.EndGroup();
        ImGui.PopID();
    }

    private static bool TryParseDateTime(string text, out DateTimeOffset value)
    {
        value = DateTimeOffset.MinValue;
        var s = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (DateTimeOffset.TryParseExact(s, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            value = dt.ToUniversalTime();
            return true;
        }
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt2))
        {
            value = dt2.ToUniversalTime();
            return true;
        }
        return false;
    }

    public void Draw(VenuePlusApp app)
    {
        var canEdit = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanEditShiftPlan);
        ImGui.Spacing();
        EnsureLookups(app);
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##shift_filter", "Search shifts by title, job or uid", ref _filter, 256);
        ImGui.PopItemWidth();
        if (canEdit)
        {
            var style = ImGui.GetStyle();
            var btnW = ImGui.CalcTextSize("Add Shift").X + style.FramePadding.X * 2f;
            var startX = ImGui.GetCursorPosX();
            var rightX = startX + ImGui.GetContentRegionAvail().X - btnW;
            ImGui.SameLine(rightX);
            if (ImGui.Button("Add Shift")) { OpenAddForm(); EnsureLookups(app); }
            if (ImGui.IsItemHovered()) { ImGui.BeginTooltip(); ImGui.TextUnformatted("Create a new shift"); ImGui.EndTooltip(); }
        }
        if (_openAddForm && canEdit)
        {
            ImGui.Separator();
            ImGui.TextUnformatted(_editingId == Guid.Empty ? "Add Shift" : "Edit Shift");
            ImGui.PushItemWidth(220f);
            ImGui.InputTextWithHint("##shift_title", "Title (optional)", ref _pendingTitle, 128);
            ImGui.PopItemWidth();
            var djEntries = app.GetDjEntries().OrderBy(e => e.DjName, StringComparer.Ordinal).ToArray();
            if (_selDjIdx >= djEntries.Length) { _selDjIdx = -1; _selDjName = null; }
            if (_selDjIdx < 0 && !string.IsNullOrWhiteSpace(_selDjName))
            {
                for (int i = 0; i < djEntries.Length; i++)
                {
                    if (string.Equals(djEntries[i].DjName, _selDjName, StringComparison.Ordinal))
                    {
                        _selDjIdx = i;
                        break;
                    }
                }
            }
            ImGui.TextUnformatted("DJ");
            ImGui.PushItemWidth(220f);
            var selDjLabel = _selDjIdx < 0 ? "(none)" : djEntries[_selDjIdx].DjName;
            if (ImGui.BeginCombo("##dj_select", selDjLabel))
            {
                bool selNone = _selDjIdx < 0;
                if (ImGui.Selectable("(none)", selNone)) { _selDjIdx = -1; _selDjName = null; }
                for (int i = 0; i < djEntries.Length; i++)
                {
                    var label = djEntries[i].DjName;
                    bool sel = _selDjIdx == i;
                    if (ImGui.Selectable(label, sel)) { _selDjIdx = i; _selDjName = label; }
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
            EnsureLookups(app);
            var leftWidth = 260f;
            var calHeight = 180f;
            ImGui.BeginChild("start_calendar", new System.Numerics.Vector2(leftWidth, calHeight), false);
            ImGui.TextUnformatted("Start Date");
            DrawCalendar("start", ref _startYear, ref _startMonth, ref _startDay);
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("start_time_panel", new System.Numerics.Vector2(Math.Max(220f, ImGui.GetContentRegionAvail().X - leftWidth - ImGui.GetStyle().ItemSpacing.X), calHeight), false);
            ImGui.TextUnformatted("Start Time");
            ImGui.PushItemWidth(140f);
            ImGui.SliderInt("Hour", ref _startHour, 0, 23);
            ImGui.SliderInt("Minute", ref _startMinute, 0, 59);
            _startMinute = Math.Max(0, Math.Min(59, _startMinute));
            _startMinute = (_startMinute / 5) * 5;
            ImGui.PopItemWidth();

            ImGui.TextUnformatted("Duration");
            var durLabel = (_durationMinutes >= 60) ? ($"{_durationMinutes / 60}h" + ((_durationMinutes % 60) > 0 ? $" {_durationMinutes % 60}m" : string.Empty)) : ($"{_durationMinutes}m");
            if (ImGui.BeginCombo("##duration", durLabel))
            {
                for (int i = 0; i < _durationOptions.Length; i++)
                {
                    var v = _durationOptions[i];
                    var text = (v >= 60) ? ($"{v / 60}h" + ((v % 60) > 0 ? $" {v % 60}m" : string.Empty)) : ($"{v}m");
                    bool sel = v == _durationMinutes;
                    if (ImGui.Selectable(text, sel)) { _durationMinutes = v; }
                }
                ImGui.EndCombo();
            }

            var startLocal = new DateTime(_startYear, _startMonth, _startDay, _startHour, _startMinute, 0, DateTimeKind.Local);
            var endLocalAuto = startLocal.AddMinutes(_durationMinutes);
            _endYear = endLocalAuto.Year;
            _endMonth = endLocalAuto.Month;
            _endDay = endLocalAuto.Day;
            _endHour = endLocalAuto.Hour;
            _endMinute = endLocalAuto.Minute;
            ImGui.EndChild();
            ImGui.TextUnformatted("Assigned User");
            ImGui.PushItemWidth(220f);
            if (_selUserIdx >= _staffUsers.Length) { _selUserIdx = -1; _selUid = null; }
            var selUserLabel = _selUserIdx < 0 ? "(none)" : ($"{_staffUsers[_selUserIdx].Username} [{_staffUsers[_selUserIdx].Job}]");
            if (ImGui.BeginCombo("##assigned_user", selUserLabel))
            {
                bool selNone = _selUserIdx < 0;
                if (ImGui.Selectable("(none)", selNone)) { _selUserIdx = -1; _selUid = null; }
                for (int i = 0; i < _staffUsers.Length; i++)
                {
                    var u = _staffUsers[i];
                    var label = (u.Username ?? string.Empty) + " [" + (u.Job ?? string.Empty) + "]";
                    bool sel = _selUserIdx == i;
                    if (ImGui.Selectable(label, sel)) { _selUserIdx = i; _selUid = string.IsNullOrWhiteSpace(u.Uid) ? null : u.Uid; if (string.IsNullOrWhiteSpace(_selJob)) _selJob = u.Job ?? _selJob; }
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();

            ImGui.PushItemWidth(220f);
            var selJobLabel = string.IsNullOrWhiteSpace(_selJob) ? "(none)" : _selJob;
            if (ImGui.BeginCombo("##job", selJobLabel))
            {
                if (ImGui.Selectable("(none)", string.IsNullOrWhiteSpace(_selJob))) { _selJob = null; }
                for (int i = 0; i < _jobs.Length; i++)
                {
                    var j = _jobs[i] ?? string.Empty;
                    bool sel = string.Equals(_selJob, j, StringComparison.Ordinal);
                    if (ImGui.Selectable(j, sel)) { _selJob = j; }
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();

            

            var endLocal = endLocalAuto;
            DateTimeOffset startAt = new DateTimeOffset(startLocal).ToUniversalTime();
            DateTimeOffset endAt = new DateTimeOffset(endLocal).ToUniversalTime();
            var canSubmit = endAt > startAt;
            ImGui.BeginDisabled(!canSubmit);
            if (ImGui.Button(_editingId == Guid.Empty ? "Add" : "Update"))
            {
                _status = "Submitting...";
                var title = (_pendingTitle ?? string.Empty).Trim();
                var uid = _selUid;
                var job = _selJob;
                var djName = _selDjName;
                if (_editingId == Guid.Empty)
                {
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.AddShiftAsync(title, startAt, endAt, uid, job, djName);
                        _status = ok ? "Added" : (app.GetLastServerMessage() ?? "Add failed");
                        if (ok) CloseAddForm();
                    });
                }
                else
                {
                    var id = _editingId;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.UpdateShiftAsync(id, title, startAt, endAt, uid, job, djName);
                        _status = ok ? "Updated" : (app.GetLastServerMessage() ?? "Update failed");
                        if (ok) CloseAddForm();
                    });
                }
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close")) { CloseAddForm(); }
            if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
            ImGui.Separator();
        }

        ImGui.Separator();
        var all = app.GetShiftEntries().ToArray();
        var eventDays = GetEventDays(all);
        if (_viewYear <= 0 || _viewMonth <= 0) { var nowV = DateTime.Now; _viewYear = nowV.Year; _viewMonth = nowV.Month; _selectedDayMain = nowV.Day; }
        if (!_listMode)
        {
            var labelMonth = new DateTime(_viewYear, _viewMonth, 1).ToString("MMMM yyyy");
            if (ImGui.Button("<##cal_prev")) { _viewMonth -= 1; if (_viewMonth <= 0) { _viewMonth = 12; _viewYear -= 1; } var maxD = DaysInMonth(_viewYear, _viewMonth); if (_selectedDayMain > maxD) _selectedDayMain = maxD; }
            ImGui.SameLine(); ImGui.TextUnformatted(labelMonth);
            ImGui.SameLine(); if (ImGui.Button(">##cal_next")) { _viewMonth += 1; if (_viewMonth >= 13) { _viewMonth = 1; _viewYear += 1; } var maxD = DaysInMonth(_viewYear, _viewMonth); if (_selectedDayMain > maxD) _selectedDayMain = maxD; }
            ImGui.SameLine(); if (ImGui.Button("Refresh")) { var club = app.CurrentClubId ?? string.Empty; if (!string.IsNullOrWhiteSpace(club)) app.SetClubId(club); }
            ImGui.SameLine();
            if (ImGui.Button("Schedule"))
            {
                _listMode = true;
                var pick = PickEventDay(eventDays);
                if (pick.HasValue)
                {
                    _viewYear = pick.Value.Year;
                    _viewMonth = pick.Value.Month;
                    _selectedDayMain = pick.Value.Day;
                }
            }
        }
        if (!_listMode)
        {
            var mainCalWidth = ImGui.GetContentRegionAvail().X;
            var mainCalHeight = Math.Max(260f, ImGui.GetContentRegionAvail().Y);
            ImGui.BeginChild("main_calendar", new System.Numerics.Vector2(mainCalWidth, mainCalHeight), false);
            DrawMainCalendar(app);
            ImGui.EndChild();
        }
        else
        {
            EnsureLookups(app);
            if (ImGui.Button("Refresh") && !string.IsNullOrWhiteSpace(app.CurrentClubId)) { app.SetClubId(app.CurrentClubId); }
            ImGui.SameLine();
            if (ImGui.Button("Calendar")) { _listMode = false; }
            if (eventDays.Count > 0)
            {
                var currentDay = new DateTime(_viewYear, _viewMonth, Math.Max(1, _selectedDayMain));
                var selectedIndex = eventDays.FindIndex(d => d.Date == currentDay.Date);
                if (selectedIndex < 0)
                {
                    var pick = PickEventDay(eventDays);
                    if (pick.HasValue)
                    {
                        _viewYear = pick.Value.Year;
                        _viewMonth = pick.Value.Month;
                        _selectedDayMain = pick.Value.Day;
                        selectedIndex = eventDays.FindIndex(d => d.Date == pick.Value.Date);
                    }
                }
                if (selectedIndex < 0) selectedIndex = 0;
                var label = eventDays[selectedIndex].ToString("yyyy-MM-dd");
                ImGui.SameLine();
                ImGui.TextUnformatted("Date");
                ImGui.SameLine();
                ImGui.PushItemWidth(140f);
                if (ImGui.BeginCombo("##schedule_day", label))
                {
                    for (int i = 0; i < eventDays.Count; i++)
                    {
                        var d = eventDays[i];
                        var text = d.ToString("yyyy-MM-dd");
                        bool sel = i == selectedIndex;
                        if (ImGui.Selectable(text, sel))
                        {
                            _viewYear = d.Year;
                            _viewMonth = d.Month;
                            _selectedDayMain = d.Day;
                            selectedIndex = i;
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.PopItemWidth();
            }
            else
            {
                ImGui.SameLine();
                ImGui.TextUnformatted("No event days");
            }
            var listHeight = Math.Max(140f, ImGui.GetContentRegionAvail().Y);
            var availX = ImGui.GetContentRegionAvail().X;
            ImGui.BeginChild("day_plan_panel", new System.Numerics.Vector2(availX, listHeight), false);
            var day = _selectedDayMain <= 0 ? 1 : _selectedDayMain;
            var dayLocalStart = new DateTime(_viewYear, _viewMonth, day, 0, 0, 0, DateTimeKind.Local);
            var dayLocalEnd = dayLocalStart.AddDays(1);
            var listDay = all.Where(e => e.StartAt.ToLocalTime() < dayLocalEnd && e.EndAt.ToLocalTime() > dayLocalStart).ToArray();
            var nowLocal = DateTimeOffset.Now;
            if (dayLocalStart.Date == nowLocal.ToLocalTime().Date)
            {
                Array.Sort(listDay, (a, b) => CompareByCurrent(nowLocal, a, b));
            }
            else
            {
                Array.Sort(listDay, (a, b) => a.StartAt.CompareTo(b.StartAt));
            }
            var titleDay = dayLocalStart.ToString("yyyy-MM-dd");
            ImGui.TextUnformatted("Plan " + titleDay);
            if (listDay.Length == 0)
            {
                ImGui.TextUnformatted("No events for this day");
            }

            var panelHeight = ImGui.GetContentRegionAvail().Y;
            var hourTableHeight = Math.Max(180f, Math.Min(260f, panelHeight * 0.45f));
            var listTableHeight = Math.Max(120f, panelHeight - hourTableHeight - ImGui.GetStyle().ItemSpacing.Y);
            var hourFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings;
            if (ImGui.BeginTable("day_hour_table", 2, hourFlags, new System.Numerics.Vector2(availX, hourTableHeight)))
            {
                ImGui.TableSetupColumn("Hour", ImGuiTableColumnFlags.WidthStretch, 0.18f);
                ImGui.TableSetupColumn("Events", ImGuiTableColumnFlags.WidthStretch, 0.82f);
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Hour");
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted("Events");
                for (int h = 0; h < 24; h++)
                {
                    var hourStart = dayLocalStart.AddHours(h);
                    var hourEnd = hourStart.AddHours(1);
                    var sb = new StringBuilder();
                    for (int i = 0; i < listDay.Length; i++)
                    {
                        var e = listDay[i];
                        var sLocal = e.StartAt.ToLocalTime();
                        var eLocal = e.EndAt.ToLocalTime();
                        if (sLocal < hourEnd && eLocal > hourStart)
                        {
                            if (sb.Length > 0) sb.Append(" | ");
                            var whoLabel = BuildWhoLabel(e, _staffUsers);
                            sb.Append(BuildShiftLabel(e, whoLabel));
                        }
                    }
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(hourStart.ToString("HH:00"));
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(sb.Length > 0 ? sb.ToString() : string.Empty);
                }
                ImGui.EndTable();
            }

            var style = ImGui.GetStyle();
            var editIcon2 = IconDraw.ToIconStringFromKey("PenToSquare");
            var rmIcon2 = IconDraw.ToIconStringFromKey("Trash");
            ImGui.PushFont(UiBuilder.IconFont);
            float actionsWidth = ImGui.CalcTextSize(editIcon2).X + style.FramePadding.X * 2f
                               + ImGui.CalcTextSize(rmIcon2).X + style.FramePadding.X * 2f
                               + style.ItemSpacing.X;
            ImGui.PopFont();
            var flagsList = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings;
            if (ImGui.BeginTable("day_list_table", 4, flagsList, new System.Numerics.Vector2(availX, listTableHeight)))
            {
                ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.16f);
                ImGui.TableSetupColumn("User/Job", ImGuiTableColumnFlags.WidthStretch, 0.34f);
                ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch, 0.34f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Time");
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted("User/Job");
                ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted("Title");
                ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted("Actions");
                for (int i = 0; i < listDay.Length; i++)
                {
                    var e = listDay[i];
                    var s = e.StartAt.ToLocalTime().ToString("HH:mm");
                    var ee = e.EndAt.ToLocalTime().ToString("HH:mm");
                    var whoLabel = BuildWhoLabel(e, _staffUsers);

                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(s + "-" + ee);
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(whoLabel);
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(e.Title ?? string.Empty);
                    ImGui.TableSetColumnIndex(3);
                    var yBase = ImGui.GetCursorPosY();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.9f);
                    ImGui.SetCursorPosY(yBase + (ImGui.GetFrameHeight() - ImGui.GetFrameHeight()) / 2f);
                    var editClicked = ImGui.Button(editIcon2 + $"##edit_day_{e.Id}");
                    ImGui.SameLine();
                    var rmClicked = ImGui.Button(rmIcon2 + $"##rm_day_{e.Id}");
                    ImGui.SetWindowFontScale(1f);
                    ImGui.PopFont();
                    if (editClicked)
                    {
                        _editingId = e.Id;
                        _pendingTitle = e.Title ?? string.Empty;
                        var sL = e.StartAt.ToLocalTime();
                        var eL = e.EndAt.ToLocalTime();
                        _startYear = sL.Year; _startMonth = sL.Month; _startDay = sL.Day; _startHour = sL.Hour; _startMinute = sL.Minute;
                        _durationMinutes = (int)Math.Max(0, (eL - sL).TotalMinutes);
                        _selUid = string.IsNullOrWhiteSpace(e.AssignedUid) ? null : e.AssignedUid;
                        _selJob = string.IsNullOrWhiteSpace(e.Job) ? null : e.Job;
                        _selDjName = string.IsNullOrWhiteSpace(e.DjName) ? null : e.DjName;
                        _selDjIdx = -1;
                        _selUserIdx = -1;
                        for (int j = 0; j < _staffUsers.Length; j++) { var u = _staffUsers[j]; if (!string.IsNullOrWhiteSpace(_selUid) && string.Equals(u.Uid, _selUid, StringComparison.Ordinal)) { _selUserIdx = j; break; } }
                        EnsureLookups(app);
                        _openAddForm = true;
                        _status = string.Empty;
                    }
                    if (rmClicked)
                    {
                        var id = e.Id;
                        System.Threading.Tasks.Task.Run(async () => { await app.RemoveShiftAsync(id); });
                    }
                }
                ImGui.EndTable();
            }
            ImGui.EndChild();
        }
    }

    private void DrawMainCalendar(VenuePlusApp app)
    {
        var first = new DateTime(_viewYear, _viewMonth, 1);
        int startCol = ((int)first.DayOfWeek + 6) % 7;
        int days = DaysInMonth(_viewYear, _viewMonth);
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoSavedSettings;
        var size = new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y);
        if (ImGui.BeginTable("main_cal_tbl", 7, flags, size))
        {
            string[] wk = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int c = 0; c < 7; c++) ImGui.TableSetupColumn(wk[c], ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            for (int c = 0; c < 7; c++) { ImGui.TableSetColumnIndex(c); ImGui.TextUnformatted(wk[c]); }
            int curDay = 1;
            var all = app.GetShiftEntries().ToArray();
            var availY = ImGui.GetContentRegionAvail().Y;
            var style = ImGui.GetStyle();
            var weekCount = Math.Max(1, (int)Math.Ceiling((startCol + days) / 7.0));
            var rowH = availY / weekCount;
            if (rowH < 24f) rowH = 24f;
            for (int r = 0; r < weekCount && curDay <= days; r++)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, rowH);
                for (int c = 0; c < 7; c++)
                {
                    ImGui.TableSetColumnIndex(c);
                    if (r == 0 && c < startCol) { ImGui.TextUnformatted(""); continue; }
                    if (curDay > days) { ImGui.TextUnformatted(""); continue; }
                    bool sel = _selectedDayMain == curDay;
                    var lbl = curDay.ToString();
                    var dayStart = new DateTime(_viewYear, _viewMonth, curDay, 0, 0, 0, DateTimeKind.Local);
                    var dayEnd = dayStart.AddDays(1);
                    var dayShifts = all.Where(e => e.StartAt.ToLocalTime() < dayEnd && e.EndAt.ToLocalTime() > dayStart).ToArray();
                    if (dayShifts.Length > 0)
                    {
                        var col = VenuePlus.Helpers.ColorUtil.HexToU32(CalendarMarkerHex);
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, col);
                    }
                    var clickableSize = new System.Numerics.Vector2(0f, rowH - style.CellPadding.Y * 2f);
                    if (ImGui.Selectable(lbl + $"##day_{curDay}", sel, ImGuiSelectableFlags.AllowDoubleClick, clickableSize)) { _selectedDayMain = curDay; _listMode = true; }
                    curDay++;
                }
            }
            ImGui.EndTable();
        }
    }

    private static List<DateTime> GetEventDays(ShiftEntry[] entries)
    {
        var set = new HashSet<DateTime>();
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            var start = e.StartAt.ToLocalTime();
            var end = e.EndAt.ToLocalTime();
            var dayStart = start.Date;
            var dayEnd = end.Date;
            if (end.TimeOfDay == TimeSpan.Zero && dayEnd > dayStart) dayEnd = dayEnd.AddDays(-1);
            for (var d = dayStart; d <= dayEnd; d = d.AddDays(1))
            {
                set.Add(d);
            }
        }
        var list = set.ToList();
        list.Sort((a, b) => a.Date.CompareTo(b.Date));
        return list;
    }

    private static DateTime? PickEventDay(List<DateTime> days)
    {
        if (days.Count == 0) return null;
        var today = DateTime.Now.Date;
        for (int i = 0; i < days.Count; i++)
        {
            if (days[i].Date == today) return days[i];
        }
        return days[0];
    }

    private static int CompareByCurrent(DateTimeOffset nowLocal, ShiftEntry a, ShiftEntry b)
    {
        var now = nowLocal.ToLocalTime();
        var aStart = a.StartAt.ToLocalTime();
        var aEnd = a.EndAt.ToLocalTime();
        var bStart = b.StartAt.ToLocalTime();
        var bEnd = b.EndAt.ToLocalTime();
        var aCurrent = aStart <= now && aEnd > now;
        var bCurrent = bStart <= now && bEnd > now;
        if (aCurrent != bCurrent) return aCurrent ? -1 : 1;
        var aPast = aEnd <= now;
        var bPast = bEnd <= now;
        if (aPast != bPast) return aPast ? 1 : -1;
        return a.StartAt.CompareTo(b.StartAt);
    }

    private static string BuildWhoLabel(ShiftEntry e, StaffUser[] users)
    {
        string? uname = null;
        if (!string.IsNullOrWhiteSpace(e.AssignedUid))
        {
            for (int i = 0; i < users.Length; i++)
            {
                var u = users[i];
                if (string.Equals(u.Uid, e.AssignedUid, StringComparison.Ordinal))
                {
                    uname = u.Username;
                    break;
                }
            }
        }
        var djLabel = string.IsNullOrWhiteSpace(e.DjName) ? null : ("DJ: " + e.DjName);
        var who = !string.IsNullOrWhiteSpace(djLabel) ? djLabel : (!string.IsNullOrWhiteSpace(uname) ? uname : (string.IsNullOrWhiteSpace(e.AssignedUid) ? string.Empty : e.AssignedUid!));
        return string.IsNullOrWhiteSpace(e.Job) ? who : (string.IsNullOrWhiteSpace(who) ? e.Job! : (who + " [" + e.Job + "]"));
    }

    private static string BuildShiftLabel(ShiftEntry e, string whoLabel)
    {
        var s = e.StartAt.ToLocalTime().ToString("HH:mm");
        var ee = e.EndAt.ToLocalTime().ToString("HH:mm");
        var title = e.Title ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(whoLabel)) return s + "-" + ee;
        if (string.IsNullOrWhiteSpace(title)) return s + "-" + ee + " " + whoLabel;
        if (string.IsNullOrWhiteSpace(whoLabel)) return s + "-" + ee + " " + title;
        return s + "-" + ee + " " + title + " (" + whoLabel + ")";
    }
}
