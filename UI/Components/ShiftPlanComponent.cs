using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private DateTimeOffset _lastUsersResolveFetch = DateTimeOffset.MinValue;
    private bool _usersResolveFetchInFlight;
    private bool _lastUsersUpdateFromServer;
    private int _selUserIdx = -1;
    private string? _selUid;
    private string? _selJob;
    private int _selDjIdx = -1;
    private string? _selDjName;
    private string? _pendingAssignUsername;
    private int _startYear;
    private int _startMonth;
    private int _startDay;
    private int _startHour;
    private int _startMinute;
    private int _durationMinutes = 120;
    private readonly int[] _durationOptions = new[] { 30, 60, 90, 120, 180, 240, 360, 480 };
    private static readonly int[] HourOptions = CreateHourOptions();
    private static readonly int[] MinuteOptions = new[] { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55 };
    private int _viewYear;
    private int _viewMonth;
    private int _selectedDayMain;
    private bool _listMode;
    private string _shiftTabFilter = string.Empty;
    private int _shiftTabPageIndex;
    private int _shiftTabSortCol;
    private bool _shiftTabSortAsc = true;
    private bool _shiftTabViewMy = true;
    private bool _useLocalTimeView;
    private const string CalendarMarkerHex = "#4CAF50";
    private const string CurrentShiftRowHex = "#2E7D32";
    private const string PastShiftRowHex = "#424242";

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
        _pendingAssignUsername = null;
        var now = _useLocalTimeView ? DateTime.Now : DateTime.UtcNow;
        _startYear = now.Year;
        _startMonth = now.Month;
        _startDay = now.Day;
        _startHour = now.Hour;
        _startMinute = (now.Minute / 5) * 5;
        _pendingStart = new DateTime(_startYear, _startMonth, _startDay, 0, 0, 0, _useLocalTimeView ? DateTimeKind.Local : DateTimeKind.Utc).ToString("yyyy-MM-dd");
        SyncAddFormDateFromSelection();
    }

    internal void OpenAddFormForUser(string? uid, string? username, string? job)
    {
        OpenAddForm();
        _selUid = string.IsNullOrWhiteSpace(uid) ? null : uid;
        _selUserIdx = -1;
        _pendingAssignUsername = string.IsNullOrWhiteSpace(username) ? null : username;
        if (!string.IsNullOrWhiteSpace(job)) _selJob = job;
    }

    internal void OpenAddFormForDj(string? djName)
    {
        OpenAddForm();
        _selDjName = string.IsNullOrWhiteSpace(djName) ? null : djName;
        _selDjIdx = -1;
    }

    internal void DrawInlineAddForm(VenuePlusApp app)
    {
        var canEdit = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanEditShiftPlan);
        _useLocalTimeView = app.ShowShiftTimesInLocalTime;
        DrawEditForm(app, canEdit);
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
        _pendingAssignUsername = null;
    }

    public void SetUsersFromServer(StaffUser[] users)
    {
        _staffUsers = users ?? Array.Empty<StaffUser>();
        _lastUsersFetch = DateTimeOffset.UtcNow;
        _lastUsersUpdateFromServer = true;
        TryResolveSelectedUser();
    }

    private void TryResolveSelectedUser()
    {
        if (_selUserIdx >= _staffUsers.Length) { _selUserIdx = -1; }
        if (_selUserIdx < 0 && !string.IsNullOrWhiteSpace(_selUid))
        {
            for (int i = 0; i < _staffUsers.Length; i++)
            {
                var u = _staffUsers[i];
                if (!string.IsNullOrWhiteSpace(u.Uid) && string.Equals(u.Uid, _selUid, StringComparison.Ordinal))
                {
                    _selUserIdx = i;
                    _pendingAssignUsername = null;
                    break;
                }
            }
        }
        if (_selUserIdx < 0 && !string.IsNullOrWhiteSpace(_pendingAssignUsername))
        {
            for (int i = 0; i < _staffUsers.Length; i++)
            {
                var u = _staffUsers[i];
                if (!string.IsNullOrWhiteSpace(u.Username) && string.Equals(u.Username, _pendingAssignUsername, StringComparison.Ordinal))
                {
                    _selUserIdx = i;
                    if (string.IsNullOrWhiteSpace(_selUid)) _selUid = u.Uid;
                    _pendingAssignUsername = null;
                    break;
                }
            }
        }
    }

    private void EnsureUserResolveFetch(VenuePlusApp app)
    {
        if (!_openAddForm) return;
        if (_selUserIdx >= 0) return;
        if (string.IsNullOrWhiteSpace(_selUid) && string.IsNullOrWhiteSpace(_pendingAssignUsername)) return;
        if (_usersResolveFetchInFlight) return;
        var now = DateTimeOffset.UtcNow;
        if (_lastUsersResolveFetch != DateTimeOffset.MinValue && (now - _lastUsersResolveFetch).TotalSeconds < 2) return;
        _lastUsersResolveFetch = now;
        _usersResolveFetchInFlight = true;
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var det = await app.FetchStaffUsersDetailedAsync();
                if (det != null)
                {
                    _staffUsers = det;
                    _lastUsersFetch = DateTimeOffset.UtcNow;
                    _lastUsersUpdateFromServer = true;
                    TryResolveSelectedUser();
                }
            }
            catch { }
            finally { _usersResolveFetchInFlight = false; }
        });
    }

    private void EnsureLookups(VenuePlusApp app)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastUsersFetch == DateTimeOffset.MinValue || (now - _lastUsersFetch).TotalSeconds > 2 || _staffUsers.Length == 0)
        {
            _lastUsersFetch = now;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var det = await app.ListStaffUsersDetailedAsync();
                    _staffUsers = det ?? Array.Empty<VenuePlus.State.StaffUser>();
                    _lastUsersUpdateFromServer = false;
                }
                catch { }
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

    private static int[] CreateHourOptions()
    {
        var arr = new int[24];
        for (int i = 0; i < arr.Length; i++) arr[i] = i;
        return arr;
    }

    private void SyncAddFormDateFromSelection()
    {
        if (_viewYear <= 0 || _viewMonth <= 0 || _selectedDayMain <= 0) return;
        var maxD = DaysInMonth(_viewYear, _viewMonth);
        var day = _selectedDayMain;
        if (day > maxD) day = maxD;
        if (day < 1) day = 1;
        _startYear = _viewYear;
        _startMonth = _viewMonth;
        _startDay = day;
        _pendingStart = new DateTime(_startYear, _startMonth, _startDay, 0, 0, 0, _useLocalTimeView ? DateTimeKind.Local : DateTimeKind.Utc).ToString("yyyy-MM-dd");
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
        if (DateTimeOffset.TryParseExact(s, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            value = dt;
            return true;
        }
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt2))
        {
            value = dt2;
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
        var useLocal = app.ShowShiftTimesInLocalTime;
        _useLocalTimeView = useLocal;
        DrawEditForm(app, canEdit);

        ImGui.Separator();
        var all = app.GetShiftEntries().ToArray();
        var nonDj = FilterNonDj(all);
        var eventDays = GetEventDays(nonDj, useLocal);
        if (_viewYear <= 0 || _viewMonth <= 0) { var nowV = useLocal ? DateTime.Now : DateTime.UtcNow; _viewYear = nowV.Year; _viewMonth = nowV.Month; _selectedDayMain = nowV.Day; }
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
                var pick = PickEventDay(eventDays, useLocal);
                if (pick.HasValue)
                {
                    _viewYear = pick.Value.Year;
                    _viewMonth = pick.Value.Month;
                    _selectedDayMain = pick.Value.Day;
                    if (_openAddForm) SyncAddFormDateFromSelection();
                }
            }
        }
        if (!_listMode)
        {
            var mainCalWidth = ImGui.GetContentRegionAvail().X;
            var mainCalHeight = Math.Max(260f, ImGui.GetContentRegionAvail().Y);
            ImGui.BeginChild("main_calendar", new System.Numerics.Vector2(mainCalWidth, mainCalHeight), false);
            DrawMainCalendar(app, useLocal);
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
                    var pick = PickEventDay(eventDays, useLocal);
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
                            if (_openAddForm) SyncAddFormDateFromSelection();
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
            var dayStart = new DateTime(_viewYear, _viewMonth, day, 0, 0, 0, useLocal ? DateTimeKind.Local : DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);
            var listDay = useLocal
                ? nonDj.Where(e => e.StartAt.ToLocalTime() < dayEnd && e.EndAt.ToLocalTime() > dayStart).ToArray()
                : nonDj.Where(e => e.StartAt < dayEnd && e.EndAt > dayStart).ToArray();
            var nowServer = DateTimeOffset.UtcNow;
            if (dayStart.Date == (useLocal ? nowServer.ToLocalTime().Date : nowServer.Date))
            {
                Array.Sort(listDay, (a, b) => CompareByCurrent(nowServer, a, b));
            }
            else
            {
                Array.Sort(listDay, (a, b) => a.StartAt.CompareTo(b.StartAt));
            }
            var titleDay = dayStart.ToString("yyyy-MM-dd");
            ImGui.TextUnformatted("Plan " + titleDay);
            if (listDay.Length == 0)
            {
                ImGui.TextUnformatted("No events for this day");
            }

            var style = ImGui.GetStyle();
            var editIcon2 = IconDraw.ToIconStringFromKey("PenToSquare");
            var rmIcon2 = IconDraw.ToIconStringFromKey("Trash");
            ImGui.PushFont(UiBuilder.IconFont);
            float actionsWidth = ImGui.CalcTextSize(editIcon2).X + style.FramePadding.X * 2f
                               + ImGui.CalcTextSize(rmIcon2).X + style.FramePadding.X * 2f
                               + style.ItemSpacing.X;
            ImGui.PopFont();
            var actionsColWidth = canEdit ? actionsWidth : 1f;
            var flagsList = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings;
            var listTableHeight = Math.Max(120f, ImGui.GetContentRegionAvail().Y);
            if (ImGui.BeginTable("day_list_table", 4, flagsList, new System.Numerics.Vector2(availX, listTableHeight)))
            {
                ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 0.16f);
                ImGui.TableSetupColumn("User/Job", ImGuiTableColumnFlags.WidthStretch, 0.34f);
                ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch, 0.34f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsColWidth);
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Time");
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted("User/Job");
                ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted("Title");
                ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted("Actions");
                var currentRowColor = ColorUtil.HexToU32(CurrentShiftRowHex);
                var pastRowColor = ColorUtil.HexToU32(PastShiftRowHex);
                for (int i = 0; i < listDay.Length; i++)
                {
                    var e = listDay[i];
                    var s = (useLocal ? e.StartAt.ToLocalTime() : e.StartAt).ToString("HH:mm");
                    var ee = (useLocal ? e.EndAt.ToLocalTime() : e.EndAt).ToString("HH:mm");
                    var whoLabel = BuildWhoLabel(e, _staffUsers);

                    ImGui.TableNextRow();
                    var isCurrent = e.StartAt <= nowServer && e.EndAt > nowServer;
                    if (isCurrent) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, currentRowColor);
                    else if (e.EndAt <= nowServer) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, pastRowColor);
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(s + "-" + ee);
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(whoLabel);
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(e.Title ?? string.Empty);
                    ImGui.TableSetColumnIndex(3);
                    var yBase = ImGui.GetCursorPosY();
                    ImGui.BeginDisabled(!canEdit);
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.9f);
                    ImGui.SetCursorPosY(yBase + (ImGui.GetFrameHeight() - ImGui.GetFrameHeight()) / 2f);
                    var editClicked = ImGui.Button(editIcon2 + $"##edit_day_{e.Id}");
                    ImGui.SameLine();
                    var rmClicked = ImGui.Button(rmIcon2 + $"##rm_day_{e.Id}");
                    ImGui.SetWindowFontScale(1f);
                    ImGui.PopFont();
                    ImGui.EndDisabled();
                    if (canEdit && editClicked)
                    {
                        BeginEditShift(e, app, useLocal);
                    }
                    if (canEdit && rmClicked)
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

    private void DrawEditForm(VenuePlusApp app, bool canEdit)
    {
        if (!_openAddForm || !canEdit) return;
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
        TryResolveSelectedUser();
        EnsureUserResolveFetch(app);
        ImGui.TextUnformatted("Start");
        ImGui.PushItemWidth(120f);
        ImGui.InputTextWithHint("##start_date", "yyyy-MM-dd", ref _pendingStart, 32);
        ImGui.PopItemWidth();
        if (DateTime.TryParseExact(_pendingStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
        {
            _startYear = startDate.Year;
            _startMonth = startDate.Month;
            _startDay = startDate.Day;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Time");
        ImGui.SameLine();
        ImGui.PushItemWidth(70f);
        var hourLabel = _startHour.ToString("00");
        if (ImGui.BeginCombo("##start_hour", hourLabel))
        {
            for (int i = 0; i < HourOptions.Length; i++)
            {
                var h = HourOptions[i];
                var label = h.ToString("00");
                bool sel = h == _startHour;
                if (ImGui.Selectable(label, sel)) { _startHour = h; }
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.PushItemWidth(70f);
        var minuteLabel = _startMinute.ToString("00");
        if (ImGui.BeginCombo("##start_minute", minuteLabel))
        {
            for (int i = 0; i < MinuteOptions.Length; i++)
            {
                var m = MinuteOptions[i];
                var label = m.ToString("00");
                bool sel = m == _startMinute;
                if (ImGui.Selectable(label, sel)) { _startMinute = m; }
            }
            ImGui.EndCombo();
        }
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

        var startBase = new DateTime(_startYear, _startMonth, _startDay, _startHour, _startMinute, 0, _useLocalTimeView ? DateTimeKind.Local : DateTimeKind.Utc);
        var startUtc = _useLocalTimeView ? startBase.ToUniversalTime() : startBase;
        var endUtcAuto = startUtc.AddMinutes(_durationMinutes);
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
                if (ImGui.Selectable(label, sel))
                {
                    _selUserIdx = i;
                    _selUid = string.IsNullOrWhiteSpace(u.Uid) ? null : u.Uid;
                    _selJob = SelectJobForUser(u, _selJob, _jobs);
                }
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        var hasPendingUserResolve = _selUserIdx < 0 && (!string.IsNullOrWhiteSpace(_selUid) || !string.IsNullOrWhiteSpace(_pendingAssignUsername));
        var usersSourceLabel = _usersResolveFetchInFlight ? "Server (syncing)" : (_staffUsers.Length > 0 ? (_lastUsersUpdateFromServer ? "Server" : "Cache") : "Loading");
        ImGui.TextDisabled("User list: " + usersSourceLabel);
        if (hasPendingUserResolve) ImGui.TextDisabled("Resolving assigned user...");

        ImGui.PushItemWidth(220f);
        var allowedJobs = GetAllowedJobsForSelectedUser(_staffUsers, _selUserIdx, _jobs);
        if (!IsJobAllowed(_selJob, allowedJobs)) _selJob = allowedJobs.Length > 0 ? allowedJobs[0] : null;
        var selJobLabel = string.IsNullOrWhiteSpace(_selJob) ? "(none)" : _selJob;
        if (ImGui.BeginCombo("##job", selJobLabel))
        {
            if (ImGui.Selectable("(none)", string.IsNullOrWhiteSpace(_selJob))) { _selJob = null; }
            for (int i = 0; i < allowedJobs.Length; i++)
            {
                var j = allowedJobs[i] ?? string.Empty;
                bool sel = string.Equals(_selJob, j, StringComparison.Ordinal);
                if (ImGui.Selectable(j, sel)) { _selJob = j; }
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();

        var endUtc = endUtcAuto;
        DateTimeOffset startAt = new DateTimeOffset(startUtc);
        DateTimeOffset endAt = new DateTimeOffset(endUtc);
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

    public void DrawStaffShiftTab(VenuePlusApp app)
    {
        EnsureLookups(app);
        ImGui.Spacing();
        var useLocal = app.ShowShiftTimesInLocalTime;
        _useLocalTimeView = useLocal;
        ImGui.TextUnformatted("View");
        ImGui.SameLine();
        if (ImGui.RadioButton("My", _shiftTabViewMy)) { _shiftTabViewMy = true; }
        ImGui.SameLine();
        if (ImGui.RadioButton("All", !_shiftTabViewMy)) { _shiftTabViewMy = false; }
        ImGui.SameLine();
        var canEdit = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanEditShiftPlan);
        ImGui.PushItemWidth(260f);
        ImGui.InputTextWithHint("##shift_tab_filter", "Search shifts by title, DJ, job or user", ref _shiftTabFilter, 256);
        ImGui.PopItemWidth();
        ImGui.Separator();

        var nowServer = DateTimeOffset.UtcNow;
        var all = app.GetShiftEntries().ToArray();
        var list = new List<ShiftEntry>(all.Length);
        var filter = _shiftTabFilter?.Trim() ?? string.Empty;
        var hasFilter = !string.IsNullOrWhiteSpace(filter);
        for (int i = 0; i < all.Length; i++)
        {
            var e = all[i];
            if (_shiftTabViewMy && !IsShiftForCurrentUser(app, e)) continue;
            if (hasFilter && !MatchesShiftFilter(e, filter)) continue;
            list.Add(e);
        }
        var items = list.ToArray();
        Array.Sort(items, (a, b) =>
        {
            int r = 0;
            switch (_shiftTabSortCol)
            {
                default:
                case 0:
                    r = a.StartAt.CompareTo(b.StartAt);
                    break;
                case 1:
                    r = a.StartAt.CompareTo(b.StartAt);
                    break;
                case 2:
                    r = string.Compare(BuildWhoLabel(a, _staffUsers), BuildWhoLabel(b, _staffUsers), StringComparison.OrdinalIgnoreCase);
                    break;
                case 3:
                    r = string.Compare(a.Title ?? string.Empty, b.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    break;
                case 4:
                    r = string.Compare(BuildShiftStatus(nowServer, a), BuildShiftStatus(nowServer, b), StringComparison.OrdinalIgnoreCase);
                    break;
            }
            return _shiftTabSortAsc ? r : -r;
        });

        const int pageSize = 20;
        var totalCount = items.Length;
        var totalPages = Math.Max(1, (totalCount + pageSize - 1) / pageSize);
        if (_shiftTabPageIndex >= totalPages) _shiftTabPageIndex = totalPages - 1;
        if (_shiftTabPageIndex < 0) _shiftTabPageIndex = 0;
        ImGui.BeginDisabled(_shiftTabPageIndex <= 0);
        if (ImGui.Button("Prev##shift_tab_prev")) { _shiftTabPageIndex--; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted($"Page {_shiftTabPageIndex + 1} / {totalPages}");
        ImGui.SameLine();
        ImGui.BeginDisabled(_shiftTabPageIndex >= totalPages - 1);
        if (ImGui.Button("Next##shift_tab_next")) { _shiftTabPageIndex++; }
        ImGui.EndDisabled();
        ImGui.Separator();

        var style = ImGui.GetStyle();
        var editIcon2 = IconDraw.ToIconStringFromKey("PenToSquare");
        var rmIcon2 = IconDraw.ToIconStringFromKey("Trash");
        ImGui.PushFont(UiBuilder.IconFont);
        float actionsWidth = ImGui.CalcTextSize(editIcon2).X + style.FramePadding.X * 2f
                           + ImGui.CalcTextSize(rmIcon2).X + style.FramePadding.X * 2f
                           + style.ItemSpacing.X;
        ImGui.PopFont();
        var actionsColWidth = canEdit ? actionsWidth : 1f;
        var flagsList = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings;
        var listTableHeight = Math.Max(160f, ImGui.GetContentRegionAvail().Y);
        var availX = ImGui.GetContentRegionAvail().X;
        if (ImGui.BeginTable("shift_tab_table", canEdit ? 6 : 5, flagsList, new System.Numerics.Vector2(availX, listTableHeight)))
        {
            ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableSetupColumn("User/Job", ImGuiTableColumnFlags.WidthStretch, 0.25f);
            ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch, 0.35f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 0.3f);
            if (canEdit) ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsColWidth);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0);
            TableSortUi.DrawSortableHeader("Date", 0, ref _shiftTabSortCol, ref _shiftTabSortAsc);
            ImGui.TableSetColumnIndex(1);
            TableSortUi.DrawSortableHeader("Time", 1, ref _shiftTabSortCol, ref _shiftTabSortAsc);
            ImGui.TableSetColumnIndex(2);
            TableSortUi.DrawSortableHeader("User/Job", 2, ref _shiftTabSortCol, ref _shiftTabSortAsc);
            ImGui.TableSetColumnIndex(3);
            TableSortUi.DrawSortableHeader("Title", 3, ref _shiftTabSortCol, ref _shiftTabSortAsc);
            ImGui.TableSetColumnIndex(4);
            TableSortUi.DrawSortableHeader("Status", 4, ref _shiftTabSortCol, ref _shiftTabSortAsc);
            if (canEdit)
            {
                ImGui.TableSetColumnIndex(5);
                ImGui.TextUnformatted("Actions");
            }

            var startIndex = _shiftTabPageIndex * pageSize;
            var endIndex = Math.Min(items.Length, startIndex + pageSize);
            var currentRowColor = ColorUtil.HexToU32(CurrentShiftRowHex);
            var pastRowColor = ColorUtil.HexToU32(PastShiftRowHex);
            for (int i = startIndex; i < endIndex; i++)
            {
                var e = items[i];
                var sView = useLocal ? e.StartAt.ToLocalTime() : e.StartAt;
                var eView = useLocal ? e.EndAt.ToLocalTime() : e.EndAt;
                var whoLabel = BuildWhoLabel(e, _staffUsers);
                var statusLabel = BuildShiftStatus(nowServer, e);

                ImGui.TableNextRow();
                var isCurrent = e.StartAt <= nowServer && e.EndAt > nowServer;
                if (isCurrent) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, currentRowColor);
                else if (e.EndAt <= nowServer) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, pastRowColor);
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(sView.ToString("yyyy-MM-dd"));
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(sView.ToString("HH:mm") + "-" + eView.ToString("HH:mm"));
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(whoLabel);
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(e.Title ?? string.Empty);
                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(statusLabel);
                if (canEdit)
                {
                    ImGui.TableSetColumnIndex(5);
                    var yBase = ImGui.GetCursorPosY();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.9f);
                    ImGui.SetCursorPosY(yBase + (ImGui.GetFrameHeight() - ImGui.GetFrameHeight()) / 2f);
                    var editClicked = ImGui.Button(editIcon2 + $"##edit_shift_tab_{e.Id}");
                    ImGui.SameLine();
                    var rmClicked = ImGui.Button(rmIcon2 + $"##rm_shift_tab_{e.Id}");
                    ImGui.SetWindowFontScale(1f);
                    ImGui.PopFont();
                    if (editClicked)
                    {
                        BeginEditShift(e, app, useLocal);
                    }
                    if (rmClicked)
                    {
                        var id = e.Id;
                        System.Threading.Tasks.Task.Run(async () => { await app.RemoveShiftAsync(id); });
                    }
                }
            }
            ImGui.EndTable();
        }
    }

    private void DrawMainCalendar(VenuePlusApp app, bool useLocal)
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
            var all = FilterNonDj(app.GetShiftEntries().ToArray());
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
                    var dayStart = new DateTime(_viewYear, _viewMonth, curDay, 0, 0, 0, useLocal ? DateTimeKind.Local : DateTimeKind.Utc);
                    var dayEnd = dayStart.AddDays(1);
                    var dayShifts = useLocal
                        ? all.Where(e => e.StartAt.ToLocalTime() < dayEnd && e.EndAt.ToLocalTime() > dayStart).ToArray()
                        : all.Where(e => e.StartAt < dayEnd && e.EndAt > dayStart).ToArray();
                    if (dayShifts.Length > 0)
                    {
                        var col = VenuePlus.Helpers.ColorUtil.HexToU32(CalendarMarkerHex);
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, col);
                    }
                    var clickableSize = new System.Numerics.Vector2(0f, rowH - style.CellPadding.Y * 2f);
                    if (ImGui.Selectable(lbl + $"##day_{curDay}", sel, ImGuiSelectableFlags.AllowDoubleClick, clickableSize))
                    {
                        _selectedDayMain = curDay;
                        _listMode = true;
                        if (_openAddForm) SyncAddFormDateFromSelection();
                    }
                    curDay++;
                }
            }
            ImGui.EndTable();
        }
    }

    private static ShiftEntry[] FilterNonDj(ShiftEntry[] entries)
    {
        var list = new List<ShiftEntry>(entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (string.IsNullOrWhiteSpace(e.DjName)) list.Add(e);
        }
        return list.ToArray();
    }

    private static List<DateTime> GetEventDays(ShiftEntry[] entries, bool useLocal)
    {
        var set = new HashSet<DateTime>();
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            var start = useLocal ? e.StartAt.ToLocalTime().DateTime : e.StartAt.UtcDateTime;
            var end = useLocal ? e.EndAt.ToLocalTime().DateTime : e.EndAt.UtcDateTime;
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

    private static DateTime? PickEventDay(List<DateTime> days, bool useLocal)
    {
        if (days.Count == 0) return null;
        var today = useLocal ? DateTime.Now.Date : DateTime.UtcNow.Date;
        for (int i = 0; i < days.Count; i++)
        {
            if (days[i].Date == today) return days[i];
        }
        return days[0];
    }

    private static int CompareByCurrent(DateTimeOffset nowServer, ShiftEntry a, ShiftEntry b)
    {
        var now = nowServer;
        var aStart = a.StartAt;
        var aEnd = a.EndAt;
        var bStart = b.StartAt;
        var bEnd = b.EndAt;
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
        var s = e.StartAt.ToString("HH:mm");
        var ee = e.EndAt.ToString("HH:mm");
        var title = e.Title ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(whoLabel)) return s + "-" + ee;
        if (string.IsNullOrWhiteSpace(title)) return s + "-" + ee + " " + whoLabel;
        if (string.IsNullOrWhiteSpace(whoLabel)) return s + "-" + ee + " " + title;
        return s + "-" + ee + " " + title + " (" + whoLabel + ")";
    }

    private static string[] GetAllowedJobsForSelectedUser(StaffUser[] users, int selectedIndex, string[] allJobs)
    {
        if (selectedIndex < 0 || selectedIndex >= users.Length) return allJobs ?? Array.Empty<string>();
        var user = users[selectedIndex];
        var userJobs = user.Jobs ?? Array.Empty<string>();
        var set = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < userJobs.Length; i++)
        {
            var j = userJobs[i];
            if (!string.IsNullOrWhiteSpace(j)) set.Add(j);
        }
        if (set.Count == 0 && !string.IsNullOrWhiteSpace(user.Job)) set.Add(user.Job);
        var list = new System.Collections.Generic.List<string>();
        if (allJobs != null)
        {
            for (int i = 0; i < allJobs.Length; i++)
            {
                var j = allJobs[i] ?? string.Empty;
                if (set.Contains(j)) list.Add(j);
            }
        }
        foreach (var j in set)
        {
            if (!list.Contains(j)) list.Add(j);
        }
        return list.ToArray();
    }

    private static bool IsJobAllowed(string? job, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(job) || allowed == null) return false;
        for (int i = 0; i < allowed.Length; i++)
        {
            if (string.Equals(allowed[i], job, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string? SelectJobForUser(StaffUser user, string? currentJob, string[] allJobs)
    {
        var jobs = GetAllowedJobsForSelectedUser(new[] { user }, 0, allJobs);
        if (jobs.Length == 0) return null;
        if (IsJobAllowed(currentJob, jobs)) return currentJob;
        return jobs[0];
    }

    private void BeginEditShift(ShiftEntry e, VenuePlusApp app, bool useLocal)
    {
        _editingId = e.Id;
        _pendingTitle = e.Title ?? string.Empty;
        var sView = useLocal ? e.StartAt.ToLocalTime() : e.StartAt;
        var eView = useLocal ? e.EndAt.ToLocalTime() : e.EndAt;
        _startYear = sView.Year; _startMonth = sView.Month; _startDay = sView.Day; _startHour = sView.Hour; _startMinute = sView.Minute;
        _pendingStart = sView.ToString("yyyy-MM-dd");
        _durationMinutes = (int)Math.Max(0, (eView - sView).TotalMinutes);
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

    private bool IsShiftForCurrentUser(VenuePlusApp app, ShiftEntry e)
    {
        var uid = app.CurrentStaffUid;
        if (!string.IsNullOrWhiteSpace(uid) && string.Equals(e.AssignedUid, uid, StringComparison.Ordinal)) return true;
        var username = app.CurrentStaffUsername;
        if (!string.IsNullOrWhiteSpace(username))
        {
            var u = ResolveUsernameByUid(e.AssignedUid);
            if (!string.IsNullOrWhiteSpace(u) && string.Equals(u, username, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private string ResolveUsernameByUid(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
        for (int i = 0; i < _staffUsers.Length; i++)
        {
            var u = _staffUsers[i];
            if (string.Equals(u.Uid, uid, StringComparison.Ordinal)) return u.Username ?? string.Empty;
        }
        return string.Empty;
    }

    private bool MatchesShiftFilter(ShiftEntry e, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var f = filter.Trim();
        if (!string.IsNullOrWhiteSpace(e.Title) && e.Title.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (!string.IsNullOrWhiteSpace(e.Job) && e.Job.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (!string.IsNullOrWhiteSpace(e.DjName) && e.DjName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (!string.IsNullOrWhiteSpace(e.AssignedUid) && e.AssignedUid.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        var uname = ResolveUsernameByUid(e.AssignedUid);
        if (!string.IsNullOrWhiteSpace(uname) && uname.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static string BuildShiftStatus(DateTimeOffset nowServer, ShiftEntry e)
    {
        if (nowServer < e.StartAt)
        {
            return "Starts in " + FormatDuration(e.StartAt - nowServer);
        }
        if (nowServer < e.EndAt)
        {
            return "Ends in " + FormatDuration(e.EndAt - nowServer);
        }
        return "Ended " + FormatDuration(nowServer - e.EndAt) + " ago";
    }

    private static string FormatDuration(TimeSpan span)
    {
        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        if (minutes < 1) minutes = 1;
        if (minutes >= 60)
        {
            var hours = minutes / 60;
            var mins = minutes % 60;
            if (mins == 0) return hours.ToString(CultureInfo.InvariantCulture) + "h";
            return hours.ToString(CultureInfo.InvariantCulture) + "h " + mins.ToString("00", CultureInfo.InvariantCulture) + "m";
        }
        return minutes.ToString(CultureInfo.InvariantCulture) + "m";
    }
}
