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
    private bool _openAddForm;
    private string _pendingTitle = string.Empty;
    private string _pendingStart = string.Empty;
    private string _pendingEnd = string.Empty;
    private string _pendingAssignedUid = string.Empty;
    private string _pendingJob = string.Empty;
    private string _status = string.Empty;
    private Guid _editingId = Guid.Empty;
    private int _selEventIdx = -1;
    private Guid? _selEventId;
    private bool _openEventForm;
    private Guid _editingEventId = Guid.Empty;
    private string _pendingEventTitle = string.Empty;
    private string _pendingEventStart = string.Empty;
    private int _eventStartYear;
    private int _eventStartMonth;
    private int _eventStartDay;
    private int _eventStartHour;
    private int _eventStartMinute;
    private int _eventDurationMinutes = 240;
    private string _eventStatus = string.Empty;
    private bool _pendingEventFocusActive;
    private string _pendingEventFocusTitle = string.Empty;
    private DateTimeOffset _pendingEventFocusStart;
    private DateTimeOffset _pendingEventFocusEnd;
    private bool _pendingOpenShiftFormForEvent;
    private bool _eventWindowOpen;
    private VenuePlus.State.StaffUser[] _staffUsers = Array.Empty<VenuePlus.State.StaffUser>();
    private string[] _jobs = Array.Empty<string>();
    private DateTimeOffset _lastUsersFetch = DateTimeOffset.MinValue;
    private DateTimeOffset _lastJobsFetch = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUsersResolveFetch = DateTimeOffset.MinValue;
    private bool _usersResolveFetchInFlight;
    private bool _lastUsersUpdateFromServer;
    private bool _requestScheduleTab;
    private bool _isDjForm;
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
    private string _shiftTabFilter = string.Empty;
    private int _shiftTabPageIndex;
    private int _shiftTabSortCol;
    private bool _shiftTabSortAsc = true;
    private bool _shiftTabViewMy = true;
    private DateTime? _shiftTabSelectedDate;
    private bool _useLocalTimeView;
    private const string CalendarMarkerHex = "#4CAF50";
    private const string EventMarkerHex = "#0288D1";
    private const string SelectedDayMarkerHex = "#FFB300";
    private const string DayMarkerHex = "#455A64";
    private const string CurrentShiftRowHex = "#2E7D32";
    private const string PastShiftRowHex = "#424242";
    private const float CalendarLeftMarkerWidth = 4f;
    private const float CalendarLeftMarkerPadding = 2f;
    private const float CalendarDayNumberWidth = 26f;
    private const int CalendarEventLabelMax = 18;

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
        _selEventIdx = -1;
        _selEventId = null;
        _pendingAssignUsername = null;
        _isDjForm = false;
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
        _isDjForm = false;
        _selDjIdx = -1;
        _selDjName = null;
        _selUid = string.IsNullOrWhiteSpace(uid) ? null : uid;
        _selEventIdx = -1;
        _selEventId = null;
        _selUserIdx = -1;
        _pendingAssignUsername = string.IsNullOrWhiteSpace(username) ? null : username;
        if (!string.IsNullOrWhiteSpace(job)) _selJob = job;
    }

    internal void OpenAddFormForDj(string? djName)
    {
        OpenAddForm();
        _isDjForm = true;
        _selUserIdx = -1;
        _selUid = null;
        _selJob = null;
        _selDjName = string.IsNullOrWhiteSpace(djName) ? null : djName;
        _selDjIdx = -1;
        _selEventIdx = -1;
        _selEventId = null;
    }

    internal void OpenEditShift(ShiftEntry e, VenuePlusApp app, bool useLocal)
    {
        BeginEditShift(e, app, useLocal);
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
        _isDjForm = false;
    }

    public bool ConsumeScheduleTabRequest()
    {
        if (!_requestScheduleTab) return false;
        _requestScheduleTab = false;
        return true;
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
        if (DateTimeOffset.TryParseExact(s, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd hh:mm tt", "yyyy-MM-dd h:mm tt", "yyyy-MM-dd" }, CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            value = dt;
            return true;
        }
        if (DateTimeOffset.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt2))
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
        ImGui.TextUnformatted("Schedule Planner");
        ImGui.SameLine();
        ImGui.TextDisabled("Plan event days, shifts and DJ times");
        ImGui.Separator();
        var useLocal = app.ShowShiftTimesInLocalTime;
        _useLocalTimeView = useLocal;
        ApplyPendingEventFocus(canEdit, useLocal);
        var eventEntries = app.GetEventEntries().ToArray();
        ImGui.Separator();
        var nowView = useLocal ? DateTime.Now : DateTime.UtcNow;
        EnsureCalendarState(nowView);
        var maxDay = DaysInMonth(_viewYear, _viewMonth);

        var labelMonth = new DateTime(_viewYear, _viewMonth, 1).ToString("MMMM yyyy");
        if (ImGui.Button("<##cal_prev"))
        {
            _viewMonth -= 1;
            if (_viewMonth <= 0) { _viewMonth = 12; _viewYear -= 1; }
            maxDay = DaysInMonth(_viewYear, _viewMonth);
            if (_viewYear == nowView.Year && _viewMonth == nowView.Month) _selectedDayMain = Math.Min(maxDay, nowView.Day);
            else _selectedDayMain = 0;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Previous month");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(labelMonth);
        ImGui.SameLine();
        if (ImGui.Button(">##cal_next"))
        {
            _viewMonth += 1;
            if (_viewMonth >= 13) { _viewMonth = 1; _viewYear += 1; }
            maxDay = DaysInMonth(_viewYear, _viewMonth);
            if (_viewYear == nowView.Year && _viewMonth == nowView.Month) _selectedDayMain = Math.Min(maxDay, nowView.Day);
            else _selectedDayMain = 0;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Next month");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        if (ImGui.Button("Today"))
        {
            _viewYear = nowView.Year;
            _viewMonth = nowView.Month;
            _selectedDayMain = nowView.Day;
            if (_openAddForm) SyncAddFormDateFromSelection();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Jump to today");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        if (ImGui.Button("Refresh") && !string.IsNullOrWhiteSpace(app.CurrentClubId)) { app.SetClubId(app.CurrentClubId); }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Reload schedule data");
            ImGui.EndTooltip();
        }

        var totalHeight = ImGui.GetContentRegionAvail().Y;
        var calendarHeight = Math.Max(260f, totalHeight);
        ImGui.BeginChild("main_calendar", new System.Numerics.Vector2(0f, calendarHeight), false);
        DrawMainCalendar(app, eventEntries, useLocal);
        ImGui.EndChild();

    }

    public void DrawEventWindowStandalone(VenuePlusApp app)
    {
        if (!_eventWindowOpen) return;
        var canEdit = app.IsOwnerCurrentClub || (app.HasStaffSession && app.StaffCanEditShiftPlan);
        var useLocal = app.ShowShiftTimesInLocalTime;
        _useLocalTimeView = useLocal;
        ApplyPendingEventFocus(canEdit, useLocal);
        var eventEntries = app.GetEventEntries().ToArray();
        var nowView = useLocal ? DateTime.Now : DateTime.UtcNow;
        EnsureCalendarState(nowView);
        DrawEventWindow(app, eventEntries, useLocal, canEdit);
    }

    private void EnsureCalendarState(DateTime nowView)
    {
        if (_viewYear <= 0 || _viewMonth <= 0)
        {
            _viewYear = nowView.Year;
            _viewMonth = nowView.Month;
        }
        var maxDay = DaysInMonth(_viewYear, _viewMonth);
        if (_selectedDayMain <= 0 || _selectedDayMain > maxDay)
        {
            if (_viewYear == nowView.Year && _viewMonth == nowView.Month) _selectedDayMain = Math.Min(maxDay, nowView.Day);
            else _selectedDayMain = 0;
        }
    }

    private void DrawShiftDayPanel(VenuePlusApp app, ShiftEntry[] nonDj, EventEntry[] eventEntries, bool useLocal, bool canEdit)
    {
        EnsureLookups(app);
        var today = useLocal ? DateTime.Now.Date : DateTime.UtcNow.Date;
        var maxDay = DaysInMonth(_viewYear, _viewMonth);
        if (_selectedDayMain <= 0 || _selectedDayMain > maxDay) _selectedDayMain = Math.Min(maxDay, today.Day);
        var day = _selectedDayMain <= 0 ? 1 : _selectedDayMain;
        var selectedDay = new DateTime(_viewYear, _viewMonth, day);

        ImGui.TextUnformatted("Selected Day");
        ImGui.SameLine();
        ImGui.TextUnformatted(selectedDay.ToString("yyyy-MM-dd"));
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Click a day in the calendar to change selection");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        if (ImGui.Button("Go To Today"))
        {
            _viewYear = today.Year;
            _viewMonth = today.Month;
            _selectedDayMain = today.Day;
            if (_openAddForm) SyncAddFormDateFromSelection();
            selectedDay = new DateTime(_viewYear, _viewMonth, _selectedDayMain);
        }
        if (canEdit)
        {
            ImGui.SameLine();
            if (ImGui.Button("Assign Staff"))
            {
                OpenAddFormForUser(null, null, null);
                SyncAddFormDateFromSelection();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Create a staff shift for this day");
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
            if (ImGui.Button("Assign DJ"))
            {
                OpenAddFormForDj(null);
                SyncAddFormDateFromSelection();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Create a DJ shift for this day");
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
            if (ImGui.Button("Add Event"))
            {
                _eventWindowOpen = true;
                OpenEventForm(selectedDay, useLocal);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Create an event for this day");
                ImGui.EndTooltip();
            }
        }

        var dayStart = new DateTime(_viewYear, _viewMonth, day, 0, 0, 0, useLocal ? DateTimeKind.Local : DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var titleDay = dayStart.ToString("yyyy-MM-dd");
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
        var dayEvents = useLocal
            ? eventEntries.Where(e => e.StartAt.ToLocalTime() < dayEnd && e.EndAt.ToLocalTime() > dayStart).ToArray()
            : eventEntries.Where(e => e.StartAt < dayEnd && e.EndAt > dayStart).ToArray();
        Array.Sort(dayEvents, (a, b) => a.StartAt.CompareTo(b.StartAt));
        ImGui.Separator();
        ImGui.TextUnformatted($"Day Plan {titleDay}");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Shifts scheduled for the selected day");
            ImGui.EndTooltip();
        }
        ImGui.TextUnformatted($"Shifts: {listDay.Length} | Events: {dayEvents.Length}");
        if (listDay.Length == 0) ImGui.TextUnformatted("No shifts for this day");

        var eventLookup = BuildEventLookup(eventEntries);
        var rightsCache = app.GetJobRightsCache();

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
        var listTableHeight = Math.Max(140f, ImGui.GetContentRegionAvail().Y);
        var timeSample = TimeFormat.FormatTime(new DateTime(2000, 1, 1, 23, 59, 0));
        var timeRangeSample = timeSample + "-" + timeSample;
        var timeColWidth = ImGui.CalcTextSize(timeRangeSample).X + style.CellPadding.X * 2f;
        if (ImGui.BeginTable("day_list_table", 5, flagsList, new System.Numerics.Vector2(0f, listTableHeight)))
        {
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, timeColWidth);
            ImGui.TableSetupColumn("User", ImGuiTableColumnFlags.WidthStretch, 0.28f);
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthStretch, 0.26f);
            ImGui.TableSetupColumn("Event", ImGuiTableColumnFlags.WidthStretch, 0.26f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsColWidth);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Time");
            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted("User");
            ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted("Job");
            ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted("Event");
            ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted("Actions");
            var currentRowColor = ColorUtil.HexToU32(CurrentShiftRowHex);
            var pastRowColor = ColorUtil.HexToU32(PastShiftRowHex);
            for (int i = 0; i < listDay.Length; i++)
            {
                var e = listDay[i];
                var s = TimeFormat.FormatTime(useLocal ? e.StartAt.ToLocalTime() : e.StartAt);
                var ee = TimeFormat.FormatTime(useLocal ? e.EndAt.ToLocalTime() : e.EndAt);
                var whoLabel = BuildWhoLabel(e, _staffUsers);
                var jobLabel = string.IsNullOrWhiteSpace(e.Job) ? string.Empty : e.Job;
                var eventLabel = ResolveEventLabel(e.EventId, eventLookup, useLocal);

                ImGui.TableNextRow();
                var isCurrent = e.StartAt <= nowServer && e.EndAt > nowServer;
                if (isCurrent) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, currentRowColor);
                else if (e.EndAt <= nowServer) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, pastRowColor);
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(s + "-" + ee);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(whoLabel);
                ImGui.TableSetColumnIndex(2);
                if (!string.IsNullOrWhiteSpace(jobLabel) && rightsCache != null && rightsCache.TryGetValue(jobLabel, out var infoJob))
                {
                    var col = ColorUtil.HexToU32(infoJob.ColorHex);
                    var icon = IconDraw.ParseIcon(infoJob.IconKey);
                    IconDraw.IconText(icon, 0.9f, col);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(jobLabel);
                        ImGui.EndTooltip();
                    }
                }
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(eventLabel);
                ImGui.TableSetColumnIndex(4);
                var yBase = ImGui.GetCursorPosY();
                ImGui.BeginDisabled(!canEdit);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.SetWindowFontScale(0.9f);
                ImGui.SetCursorPosY(yBase + (ImGui.GetFrameHeight() - ImGui.GetFrameHeight()) / 2f);
                var editClicked = ImGui.Button(editIcon2 + $"##edit_day_{e.Id}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Edit shift");
                    ImGui.EndTooltip();
                }
                ImGui.SameLine();
                var rmClicked = ImGui.Button(rmIcon2 + $"##rm_day_{e.Id}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Remove shift");
                    ImGui.EndTooltip();
                }
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
    }

    private void DrawEventWindow(VenuePlusApp app, EventEntry[] eventEntries, bool useLocal, bool canEdit)
    {
        if (!_eventWindowOpen) return;
        var showRight = canEdit && (_openAddForm || _openEventForm);
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(showRight ? 1120f : 760f, 520f), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Event Schedule", ref _eventWindowOpen, ImGuiWindowFlags.NoCollapse))
        {
            if (showRight && ImGui.GetWindowSize().X < 1120f)
            {
                ImGui.SetWindowSize(new System.Numerics.Vector2(1120f, ImGui.GetWindowSize().Y), ImGuiCond.Always);
            }
            if (!showRight && ImGui.GetWindowSize().X > 760f)
            {
                ImGui.SetWindowSize(new System.Numerics.Vector2(760f, ImGui.GetWindowSize().Y), ImGuiCond.Always);
            }
            var style = ImGui.GetStyle();
            var avail = ImGui.GetContentRegionAvail();
            var rightWidth = showRight ? 360f : 0f;
            var leftWidth = Math.Max(320f, avail.X - rightWidth - (showRight ? style.ItemSpacing.X : 0f));
            ImGui.BeginChild("event_window_left", new System.Numerics.Vector2(leftWidth, avail.Y), false);
            DrawEventWindowContents(app, eventEntries, useLocal, canEdit);
            ImGui.EndChild();
            if (showRight)
            {
                ImGui.SameLine();
                ImGui.BeginChild("event_window_right", new System.Numerics.Vector2(0f, avail.Y), false);
                DrawEditForm(app, canEdit);
                DrawEventForm(app, canEdit);
                ImGui.EndChild();
            }
        }
        ImGui.End();
    }

    private void DrawEventWindowContents(VenuePlusApp app, EventEntry[] eventEntries, bool useLocal, bool canEdit)
    {
        var all = app.GetShiftEntries().ToArray();
        var nonDj = FilterNonDj(all);
        var totalHeight = ImGui.GetContentRegionAvail().Y;
        var shiftPanelHeight = Math.Max(260f, totalHeight * 0.55f);
        ImGui.BeginChild("event_window_shift_panel", new System.Numerics.Vector2(0f, shiftPanelHeight), false);
        DrawShiftDayPanel(app, nonDj, eventEntries, useLocal, canEdit);
        ImGui.EndChild();
        ImGui.Spacing();

        var eventPanelHeight = Math.Max(200f, ImGui.GetContentRegionAvail().Y);
        ImGui.BeginChild("event_window_events_panel", new System.Numerics.Vector2(0f, eventPanelHeight), false);

        var today = useLocal ? DateTime.Now.Date : DateTime.UtcNow.Date;
        var maxDay = DaysInMonth(_viewYear, _viewMonth);
        if (_selectedDayMain <= 0 || _selectedDayMain > maxDay) _selectedDayMain = Math.Min(maxDay, today.Day);
        var day = _selectedDayMain <= 0 ? 1 : _selectedDayMain;

        var dayStart = new DateTime(_viewYear, _viewMonth, day, 0, 0, 0, useLocal ? DateTimeKind.Local : DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var titleDay = dayStart.ToString("yyyy-MM-dd");
        var dayEvents = useLocal
            ? eventEntries.Where(e => e.StartAt.ToLocalTime() < dayEnd && e.EndAt.ToLocalTime() > dayStart).ToArray()
            : eventEntries.Where(e => e.StartAt < dayEnd && e.EndAt > dayStart).ToArray();
        Array.Sort(dayEvents, (a, b) => a.StartAt.CompareTo(b.StartAt));
        ImGui.TextUnformatted($"Events {titleDay}");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Select an event to link shifts in the form");
            ImGui.EndTooltip();
        }
        ImGui.TextUnformatted($"Events: {dayEvents.Length}");
        if (dayEvents.Length == 0) ImGui.TextUnformatted("No events for this day");

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
        var listTableHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y);
        var timeSample = TimeFormat.FormatTime(new DateTime(2000, 1, 1, 23, 59, 0));
        var timeRangeSample = timeSample + "-" + timeSample;
        var timeColWidth = ImGui.CalcTextSize(timeRangeSample).X + style.CellPadding.X * 2f;
        if (ImGui.BeginTable("day_event_table_window", canEdit ? 3 : 2, flagsList, new System.Numerics.Vector2(0f, listTableHeight)))
        {
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, timeColWidth);
            ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch);
            if (canEdit) ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsColWidth);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Time");
            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted("Title");
            if (canEdit)
            {
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted("Actions");
            }
            for (int i = 0; i < dayEvents.Length; i++)
            {
                var e = dayEvents[i];
                var s = TimeFormat.FormatTime(useLocal ? e.StartAt.ToLocalTime() : e.StartAt);
                var ee = TimeFormat.FormatTime(useLocal ? e.EndAt.ToLocalTime() : e.EndAt);
                var label = BuildEventShortLabel(e, useLocal);
                var isSelected = _selEventId.HasValue && _selEventId.Value == e.Id;
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(s + "-" + ee);
                ImGui.TableSetColumnIndex(1);
                if (ImGui.Selectable(label + $"##event_sel_window_{e.Id}", isSelected))
                {
                    _selEventId = e.Id;
                    _selEventIdx = -1;
                }
                if (canEdit)
                {
                    ImGui.TableSetColumnIndex(2);
                    var yBase = ImGui.GetCursorPosY();
                    ImGui.BeginDisabled(!canEdit);
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(0.9f);
                    ImGui.SetCursorPosY(yBase + (ImGui.GetFrameHeight() - ImGui.GetFrameHeight()) / 2f);
                    var editClicked = ImGui.Button(editIcon2 + $"##edit_event_day_window_{e.Id}");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Edit event");
                        ImGui.EndTooltip();
                    }
                    ImGui.SameLine();
                    var rmClicked = ImGui.Button(rmIcon2 + $"##rm_event_day_window_{e.Id}");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted("Remove event");
                        ImGui.EndTooltip();
                    }
                    ImGui.SetWindowFontScale(1f);
                    ImGui.PopFont();
                    ImGui.EndDisabled();
                    if (canEdit && editClicked)
                    {
                        BeginEditEvent(e, useLocal);
                    }
                    if (canEdit && rmClicked)
                    {
                        var id = e.Id;
                        System.Threading.Tasks.Task.Run(async () => { await app.RemoveEventAsync(id); });
                    }
                }
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private void DrawEditForm(VenuePlusApp app, bool canEdit)
    {
        if (!_openAddForm || !canEdit) return;
        ImGui.Separator();
        var editing = _editingId != Guid.Empty;
        var isDjForm = _isDjForm;
        ImGui.TextUnformatted(editing ? (isDjForm ? "Edit DJ Shift" : "Edit Staff Shift") : (isDjForm ? "Add DJ Shift" : "Add Staff Shift"));
        ImGui.TextDisabled(isDjForm ? "Mode: DJ shift" : "Mode: staff shift");
        ImGui.TextUnformatted("Details");
        ImGui.PushItemWidth(220f);
        ImGui.InputTextWithHint("##shift_title", "Title (optional)", ref _pendingTitle, 128);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.TextUnformatted("Date");
        ImGui.SameLine();
        ImGui.PushItemWidth(110f);
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
        var hourLabel = TimeFormat.FormatHourOption(_startHour);
        if (ImGui.BeginCombo("##start_hour", hourLabel))
        {
            for (int i = 0; i < HourOptions.Length; i++)
            {
                var h = HourOptions[i];
                var label = TimeFormat.FormatHourOption(h);
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

        ImGui.SameLine();
        ImGui.TextUnformatted("Duration");
        ImGui.SameLine();
        ImGui.PushItemWidth(90f);
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
        ImGui.PopItemWidth();

        var startBase = new DateTime(_startYear, _startMonth, _startDay, _startHour, _startMinute, 0, _useLocalTimeView ? DateTimeKind.Local : DateTimeKind.Utc);
        var startUtc = _useLocalTimeView ? startBase.ToUniversalTime() : startBase;
        var endUtcAuto = startUtc.AddMinutes(_durationMinutes);
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
        EnsureLookups(app);
        TryResolveSelectedUser();
        EnsureUserResolveFetch(app);
        var eventEntries = app.GetEventEntries().OrderBy(e => e.StartAt).ToArray();
        TryResolvePendingEventSelection(eventEntries);
        if (_selEventIdx >= eventEntries.Length) { _selEventIdx = -1; _selEventId = null; }
        if (_selEventIdx < 0 && _selEventId.HasValue)
        {
            for (int i = 0; i < eventEntries.Length; i++)
            {
                if (eventEntries[i].Id == _selEventId.Value)
                {
                    _selEventIdx = i;
                    break;
                }
            }
        }

        var endUtc = endUtcAuto;
        DateTimeOffset startAt = new DateTimeOffset(startUtc);
        DateTimeOffset endAt = new DateTimeOffset(endUtc);
        var canSubmit = endAt > startAt;
        ImGui.TextUnformatted("Event");
        ImGui.PushItemWidth(260f);
        var selEventLabel = _selEventIdx < 0 ? "(none)" : BuildEventComboLabel(eventEntries[_selEventIdx], _useLocalTimeView);
        if (ImGui.BeginCombo("##event_select", selEventLabel))
        {
            bool selNone = _selEventIdx < 0;
            if (ImGui.Selectable("(none)", selNone)) { _selEventIdx = -1; _selEventId = null; }
            for (int i = 0; i < eventEntries.Length; i++)
            {
                var label = BuildEventComboLabel(eventEntries[i], _useLocalTimeView);
                bool sel = _selEventIdx == i;
                if (ImGui.Selectable(label, sel)) { _selEventIdx = i; _selEventId = eventEntries[i].Id; }
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        if (isDjForm)
        {
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
            var canDjSubmit = canSubmit && _selDjIdx >= 0 && !string.IsNullOrWhiteSpace(_selDjName);
            ImGui.BeginDisabled(!canDjSubmit);
            if (ImGui.Button(editing ? "Update DJ Shift" : "Add DJ Shift"))
            {
                _status = "Submitting...";
                var title = (_pendingTitle ?? string.Empty).Trim();
                var djName = _selDjName;
                if (!editing)
                {
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.AddShiftAsync(title, startAt, endAt, _selEventId, null, null, djName);
                        _status = ok ? "Added" : (app.GetLastServerMessage() ?? "Add failed");
                        if (ok) CloseAddForm();
                    });
                }
                else
                {
                    var id = _editingId;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.UpdateShiftAsync(id, title, startAt, endAt, _selEventId, null, null, djName);
                        _status = ok ? "Updated" : (app.GetLastServerMessage() ?? "Update failed");
                        if (ok) CloseAddForm();
                    });
                }
            }
            ImGui.EndDisabled();
        }
        else
        {
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
            ImGui.BeginDisabled(!canSubmit);
            if (ImGui.Button(editing ? "Update Staff Shift" : "Add Staff Shift"))
            {
                _status = "Submitting...";
                var title = (_pendingTitle ?? string.Empty).Trim();
                var uid = _selUid;
                var job = _selJob;
                if (!editing)
                {
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.AddShiftAsync(title, startAt, endAt, _selEventId, uid, job, null);
                        _status = ok ? "Added" : (app.GetLastServerMessage() ?? "Add failed");
                        if (ok) CloseAddForm();
                    });
                }
                else
                {
                    var id = _editingId;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var ok = await app.UpdateShiftAsync(id, title, startAt, endAt, _selEventId, uid, job, null);
                        _status = ok ? "Updated" : (app.GetLastServerMessage() ?? "Update failed");
                        if (ok) CloseAddForm();
                    });
                }
            }
            ImGui.EndDisabled();
        }
        if (ImGui.Button("Close")) { CloseAddForm(); }
        if (!string.IsNullOrEmpty(_status)) ImGui.TextUnformatted(_status);
        ImGui.Separator();
    }

    private void OpenEventForm(DateTime day, bool useLocal)
    {
        _openEventForm = true;
        _editingEventId = Guid.Empty;
        _pendingEventTitle = string.Empty;
        _eventStatus = string.Empty;
        var now = useLocal ? DateTime.Now : DateTime.UtcNow;
        _eventStartYear = day.Year;
        _eventStartMonth = day.Month;
        _eventStartDay = day.Day;
        _eventStartHour = now.Hour;
        _eventStartMinute = (now.Minute / 5) * 5;
        _eventDurationMinutes = 240;
        _pendingEventStart = new DateTime(_eventStartYear, _eventStartMonth, _eventStartDay, 0, 0, 0, useLocal ? DateTimeKind.Local : DateTimeKind.Utc).ToString("yyyy-MM-dd");
    }

    private void BeginEditEvent(EventEntry e, bool useLocal)
    {
        _editingEventId = e.Id;
        _pendingEventTitle = e.Title ?? string.Empty;
        var sView = useLocal ? e.StartAt.ToLocalTime() : e.StartAt;
        var eView = useLocal ? e.EndAt.ToLocalTime() : e.EndAt;
        _eventStartYear = sView.Year;
        _eventStartMonth = sView.Month;
        _eventStartDay = sView.Day;
        _eventStartHour = sView.Hour;
        _eventStartMinute = sView.Minute;
        _pendingEventStart = sView.ToString("yyyy-MM-dd");
        _eventDurationMinutes = (int)Math.Max(0, (eView - sView).TotalMinutes);
        _openEventForm = true;
        _eventStatus = string.Empty;
    }

    private void CloseEventForm()
    {
        _openEventForm = false;
        _editingEventId = Guid.Empty;
        _eventStatus = string.Empty;
    }

    private void DrawEventForm(VenuePlusApp app, bool canEdit)
    {
        if (!_openEventForm || !canEdit) return;
        ImGui.Separator();
        var editing = _editingEventId != Guid.Empty;
        ImGui.TextUnformatted(editing ? "Edit Event" : "Add Event");
        ImGui.TextUnformatted("Details");
        ImGui.PushItemWidth(220f);
        ImGui.InputTextWithHint("##event_title", "Title", ref _pendingEventTitle, 128);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.TextUnformatted("Date");
        ImGui.SameLine();
        ImGui.PushItemWidth(110f);
        ImGui.InputTextWithHint("##event_date", "yyyy-MM-dd", ref _pendingEventStart, 32);
        ImGui.PopItemWidth();
        if (DateTime.TryParseExact(_pendingEventStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
        {
            _eventStartYear = startDate.Year;
            _eventStartMonth = startDate.Month;
            _eventStartDay = startDate.Day;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Time");
        ImGui.SameLine();
        ImGui.PushItemWidth(70f);
        var hourLabel = TimeFormat.FormatHourOption(_eventStartHour);
        if (ImGui.BeginCombo("##event_start_hour", hourLabel))
        {
            for (int i = 0; i < HourOptions.Length; i++)
            {
                var h = HourOptions[i];
                var label = TimeFormat.FormatHourOption(h);
                bool sel = h == _eventStartHour;
                if (ImGui.Selectable(label, sel)) { _eventStartHour = h; }
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.PushItemWidth(70f);
        var minuteLabel = _eventStartMinute.ToString("00");
        if (ImGui.BeginCombo("##event_start_minute", minuteLabel))
        {
            for (int i = 0; i < MinuteOptions.Length; i++)
            {
                var m = MinuteOptions[i];
                var label = m.ToString("00");
                bool sel = m == _eventStartMinute;
                if (ImGui.Selectable(label, sel)) { _eventStartMinute = m; }
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.TextUnformatted("Duration");
        ImGui.SameLine();
        ImGui.PushItemWidth(90f);
        var durLabel = (_eventDurationMinutes >= 60) ? ($"{_eventDurationMinutes / 60}h" + ((_eventDurationMinutes % 60) > 0 ? $" {_eventDurationMinutes % 60}m" : string.Empty)) : ($"{_eventDurationMinutes}m");
        if (ImGui.BeginCombo("##event_duration", durLabel))
        {
            for (int i = 0; i < _durationOptions.Length; i++)
            {
                var v = _durationOptions[i];
                var text = (v >= 60) ? ($"{v / 60}h" + ((v % 60) > 0 ? $" {v % 60}m" : string.Empty)) : ($"{v}m");
                bool sel = v == _eventDurationMinutes;
                if (ImGui.Selectable(text, sel)) { _eventDurationMinutes = v; }
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();

        var startBase = new DateTime(_eventStartYear, _eventStartMonth, _eventStartDay, _eventStartHour, _eventStartMinute, 0, _useLocalTimeView ? DateTimeKind.Local : DateTimeKind.Utc);
        var startUtc = _useLocalTimeView ? startBase.ToUniversalTime() : startBase;
        var endUtc = startUtc.AddMinutes(_eventDurationMinutes);
        DateTimeOffset startAt = new DateTimeOffset(startUtc);
        DateTimeOffset endAt = new DateTimeOffset(endUtc);
        var canSubmit = endAt > startAt;
        ImGui.BeginDisabled(!canSubmit);
        if (ImGui.Button(editing ? "Update Event" : "Add Event"))
        {
            _eventStatus = "Submitting...";
            var title = (_pendingEventTitle ?? string.Empty).Trim();
            if (!editing)
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.AddEventAsync(title, startAt, endAt);
                    _eventStatus = ok ? "Added" : (app.GetLastServerMessage() ?? "Add failed");
                    if (ok)
                    {
                        SetPendingEventFocus(title, startAt, endAt);
                        _pendingOpenShiftFormForEvent = true;
                        CloseEventForm();
                    }
                });
            }
            else
            {
                var id = _editingEventId;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var ok = await app.UpdateEventAsync(id, title, startAt, endAt);
                    _eventStatus = ok ? "Updated" : (app.GetLastServerMessage() ?? "Update failed");
                    if (ok)
                    {
                        SetPendingEventFocus(title, startAt, endAt);
                        _pendingOpenShiftFormForEvent = true;
                        CloseEventForm();
                    }
                });
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Close")) { CloseEventForm(); }
        if (!string.IsNullOrEmpty(_eventStatus)) ImGui.TextUnformatted(_eventStatus);
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
        ImGui.SameLine();
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_shiftTabFilter));
        if (ImGui.Button("Clear")) { _shiftTabFilter = string.Empty; _shiftTabPageIndex = 0; }
        ImGui.EndDisabled();
        ImGui.TextUnformatted("Date");
        ImGui.SameLine();
        ImGui.PushItemWidth(160f);

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
        var eventDays = GetEventDays(list.ToArray(), useLocal);
        var selectedLabel = _shiftTabSelectedDate.HasValue
            ? _shiftTabSelectedDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "All Dates";
        if (ImGui.BeginCombo("##shift_tab_date", selectedLabel))
        {
            bool allSel = !_shiftTabSelectedDate.HasValue;
            if (ImGui.Selectable("All Dates", allSel))
            {
                _shiftTabSelectedDate = null;
                _shiftTabPageIndex = 0;
            }
            for (int i = 0; i < eventDays.Count; i++)
            {
                var day = eventDays[i].Date;
                var label = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                bool sel = _shiftTabSelectedDate.HasValue && day == _shiftTabSelectedDate.Value.Date;
                if (ImGui.Selectable(label, sel))
                {
                    _shiftTabSelectedDate = day;
                    _shiftTabPageIndex = 0;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        var today = useLocal ? DateTime.Now.Date : DateTime.UtcNow.Date;
        ImGui.BeginDisabled(!eventDays.Contains(today));
        if (ImGui.Button("Today"))
        {
            _shiftTabSelectedDate = today;
            _shiftTabPageIndex = 0;
        }
        ImGui.EndDisabled();
        ImGui.PopItemWidth();
        ImGui.Separator();

        if (_shiftTabSelectedDate.HasValue)
        {
            var dayStart = new DateTime(_shiftTabSelectedDate.Value.Year, _shiftTabSelectedDate.Value.Month, _shiftTabSelectedDate.Value.Day, 0, 0, 0, useLocal ? DateTimeKind.Local : DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);
            var filtered = new List<ShiftEntry>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (useLocal)
                {
                    if (e.StartAt.ToLocalTime() < dayEnd && e.EndAt.ToLocalTime() > dayStart) filtered.Add(e);
                }
                else
                {
                    if (e.StartAt < dayEnd && e.EndAt > dayStart) filtered.Add(e);
                }
            }
            list = filtered;
        }
        if (_shiftTabSortCol < 0 || _shiftTabSortCol > 4) _shiftTabSortCol = 0;
        var eventLookup = BuildEventLookup(app.GetEventEntries().ToArray());
        var items = list.ToArray();
        ImGui.TextUnformatted($"Shifts: {items.Length}");
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
                    r = string.Compare(BuildWhoLabel(a, _staffUsers), BuildWhoLabel(b, _staffUsers), StringComparison.OrdinalIgnoreCase);
                    break;
                case 2:
                    r = string.Compare(a.Job ?? string.Empty, b.Job ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    break;
                case 3:
                    r = string.Compare(ResolveEventLabel(a.EventId, eventLookup, useLocal), ResolveEventLabel(b.EventId, eventLookup, useLocal), StringComparison.OrdinalIgnoreCase);
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
        var timeSample = TimeFormat.FormatTime(new DateTime(2000, 1, 1, 23, 59, 0));
        var timeRangeSample = timeSample + "-" + timeSample;
        var timeColWidth = ImGui.CalcTextSize(timeRangeSample).X + style.CellPadding.X * 2f;
        if (ImGui.BeginTable("shift_tab_table", canEdit ? 6 : 5, flagsList, new System.Numerics.Vector2(availX, listTableHeight)))
        {
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, timeColWidth);
            ImGui.TableSetupColumn("User", ImGuiTableColumnFlags.WidthStretch, 0.22f);
            ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthStretch, 0.2f);
            ImGui.TableSetupColumn("Event", ImGuiTableColumnFlags.WidthStretch, 0.22f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 0.26f);
            if (canEdit) ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsColWidth);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0);
            TableSortUi.DrawSortableHeader("Time", 0, ref _shiftTabSortCol, ref _shiftTabSortAsc);
            ImGui.TableSetColumnIndex(1);
            TableSortUi.DrawSortableHeader("User", 1, ref _shiftTabSortCol, ref _shiftTabSortAsc);
            ImGui.TableSetColumnIndex(2);
            TableSortUi.DrawSortableHeader("Job", 2, ref _shiftTabSortCol, ref _shiftTabSortAsc);
            ImGui.TableSetColumnIndex(3);
            TableSortUi.DrawSortableHeader("Event", 3, ref _shiftTabSortCol, ref _shiftTabSortAsc);
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
            var rightsCache = app.GetJobRightsCache();
            for (int i = startIndex; i < endIndex; i++)
            {
                var e = items[i];
                var sView = useLocal ? e.StartAt.ToLocalTime() : e.StartAt;
                var eView = useLocal ? e.EndAt.ToLocalTime() : e.EndAt;
                var whoLabel = BuildWhoLabel(e, _staffUsers);
                var statusLabel = BuildShiftStatus(nowServer, e);
                var jobLabel = string.IsNullOrWhiteSpace(e.Job) ? string.Empty : e.Job;
                var eventLabel = ResolveEventLabel(e.EventId, eventLookup, useLocal);

                ImGui.TableNextRow();
                var isCurrent = e.StartAt <= nowServer && e.EndAt > nowServer;
                if (isCurrent) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, currentRowColor);
                else if (e.EndAt <= nowServer) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, pastRowColor);
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(TimeFormat.FormatTime(sView) + "-" + TimeFormat.FormatTime(eView));
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(whoLabel);
                ImGui.TableSetColumnIndex(2);
                if (!string.IsNullOrWhiteSpace(jobLabel) && rightsCache != null && rightsCache.TryGetValue(jobLabel, out var infoJob))
                {
                    var col = ColorUtil.HexToU32(infoJob.ColorHex);
                    var icon = IconDraw.ParseIcon(infoJob.IconKey);
                    IconDraw.IconText(icon, 0.9f, col);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(jobLabel);
                        ImGui.EndTooltip();
                    }
                }
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(eventLabel);
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
                        _requestScheduleTab = true;
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

    private void DrawMainCalendar(VenuePlusApp app, EventEntry[] events, bool useLocal)
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
                    var dayEvents = useLocal
                        ? events.Where(e => e.StartAt.ToLocalTime() < dayEnd && e.EndAt.ToLocalTime() > dayStart).ToArray()
                        : events.Where(e => e.StartAt < dayEnd && e.EndAt > dayStart).ToArray();
                    if (dayShifts.Length > 0 || dayEvents.Length > 0)
                    {
                        var col = dayShifts.Length > 0
                            ? VenuePlus.Helpers.ColorUtil.HexToU32(CalendarMarkerHex)
                            : VenuePlus.Helpers.ColorUtil.HexToU32(EventMarkerHex);
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, col);
                    }
                    var cellStart = ImGui.GetCursorPos();
                    var cellScreen = ImGui.GetCursorScreenPos();
                    var clickableSize = new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, rowH - style.CellPadding.Y * 2f);
                    if (ImGui.InvisibleButton($"##day_{curDay}", clickableSize))
                    {
                        _selectedDayMain = curDay;
                        _eventWindowOpen = true;
                        if (_openAddForm) SyncAddFormDateFromSelection();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted(dayStart.ToString("yyyy-MM-dd"));
                        ImGui.TextUnformatted($"Shifts: {dayShifts.Length}");
                        ImGui.TextUnformatted($"Events: {dayEvents.Length}");
                        var maxItems = 3;
                        for (int i = 0; i < dayEvents.Length && i < maxItems; i++)
                        {
                            ImGui.TextUnformatted(BuildEventShortLabel(dayEvents[i], useLocal));
                        }
                        if (dayEvents.Length > maxItems) ImGui.TextUnformatted("More events available...");
                        ImGui.EndTooltip();
                    }
                    var drawList = ImGui.GetWindowDrawList();
                    var markerColor = sel
                        ? VenuePlus.Helpers.ColorUtil.HexToU32(SelectedDayMarkerHex)
                        : VenuePlus.Helpers.ColorUtil.HexToU32(DayMarkerHex);
                    drawList.AddRectFilled(
                        cellScreen,
                        new System.Numerics.Vector2(cellScreen.X + CalendarLeftMarkerWidth, cellScreen.Y + clickableSize.Y),
                        markerColor);
                    ImGui.SetCursorPos(new System.Numerics.Vector2(cellStart.X + CalendarLeftMarkerWidth + CalendarLeftMarkerPadding, cellStart.Y));
                    ImGui.TextUnformatted(lbl);
                    ImGui.SetCursorPos(new System.Numerics.Vector2(cellStart.X + CalendarDayNumberWidth, cellStart.Y));
                    if (dayEvents.Length > 0)
                    {
                        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
                        for (int i = 0; i < dayEvents.Length; i++)
                        {
                            var eventLabel = BuildEventShortLabel(dayEvents[i], useLocal);
                            ImGui.TextUnformatted(TruncateLabel(eventLabel, CalendarEventLabelMax));
                        }
                        ImGui.PopTextWrapPos();
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

    private static Dictionary<Guid, EventEntry> BuildEventLookup(EventEntry[] entries)
    {
        var dict = new Dictionary<Guid, EventEntry>();
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.Id != Guid.Empty && !dict.ContainsKey(e.Id)) dict.Add(e.Id, e);
        }
        return dict;
    }

    private void SetPendingEventFocus(string title, DateTimeOffset startAt, DateTimeOffset endAt)
    {
        _pendingEventFocusActive = true;
        _pendingEventFocusTitle = title ?? string.Empty;
        _pendingEventFocusStart = startAt;
        _pendingEventFocusEnd = endAt;
    }

    private void ApplyPendingEventFocus(bool canEdit, bool useLocal)
    {
        if (!_pendingOpenShiftFormForEvent) return;
        _pendingOpenShiftFormForEvent = false;
        SetSelectedDayFromDateTimeOffset(_pendingEventFocusStart, useLocal);
        _selEventId = null;
        _selEventIdx = -1;
        if (canEdit)
        {
            OpenAddFormForUser(null, null, null);
            SyncAddFormDateFromSelection();
        }
    }

    private void SetSelectedDayFromDateTimeOffset(DateTimeOffset date, bool useLocal)
    {
        var dt = useLocal ? date.ToLocalTime().DateTime : date.UtcDateTime;
        _viewYear = dt.Year;
        _viewMonth = dt.Month;
        _selectedDayMain = dt.Day;
    }

    private void TryResolvePendingEventSelection(EventEntry[] entries)
    {
        if (!_pendingEventFocusActive || entries.Length == 0) return;
        int matchIdx = -1;
        int fallbackIdx = -1;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.StartAt == _pendingEventFocusStart && e.EndAt == _pendingEventFocusEnd)
            {
                if (fallbackIdx < 0) fallbackIdx = i;
                if (!string.IsNullOrWhiteSpace(_pendingEventFocusTitle) && string.Equals(e.Title ?? string.Empty, _pendingEventFocusTitle, StringComparison.Ordinal))
                {
                    matchIdx = i;
                    break;
                }
            }
        }
        if (matchIdx < 0) matchIdx = fallbackIdx;
        if (matchIdx >= 0)
        {
            _selEventIdx = matchIdx;
            _selEventId = entries[matchIdx].Id;
            _pendingEventFocusActive = false;
        }
    }

    private static string TruncateLabel(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? string.Empty;
        if (max <= 1) return value.Substring(0, 1);
        return value.Substring(0, max - 1) + "…";
    }

    private static string ResolveEventLabel(Guid? eventId, Dictionary<Guid, EventEntry> lookup, bool useLocal)
    {
        if (!eventId.HasValue) return string.Empty;
        if (!lookup.TryGetValue(eventId.Value, out var entry)) return string.Empty;
        return BuildEventShortLabel(entry, useLocal);
    }

    private static string BuildEventShortLabel(EventEntry entry, bool useLocal)
    {
        var title = entry.Title ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(title)) return title;
        var s = useLocal ? entry.StartAt.ToLocalTime() : entry.StartAt;
        var e = useLocal ? entry.EndAt.ToLocalTime() : entry.EndAt;
        return TimeFormat.FormatTime(s) + "-" + TimeFormat.FormatTime(e);
    }

    private static string BuildEventComboLabel(EventEntry entry, bool useLocal)
    {
        var s = useLocal ? entry.StartAt.ToLocalTime() : entry.StartAt;
        var e = useLocal ? entry.EndAt.ToLocalTime() : entry.EndAt;
        var title = entry.Title ?? string.Empty;
        var date = s.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var range = TimeFormat.FormatTime(s) + "-" + TimeFormat.FormatTime(e);
        if (string.IsNullOrWhiteSpace(title)) return date + " " + range;
        return date + " " + range + " " + title;
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
                    uname = StripHomeworld(u.Username);
                    break;
                }
            }
        }
        var djLabel = string.IsNullOrWhiteSpace(e.DjName) ? null : ("DJ: " + e.DjName);
        var who = !string.IsNullOrWhiteSpace(djLabel) ? djLabel : (!string.IsNullOrWhiteSpace(uname) ? uname : (string.IsNullOrWhiteSpace(e.AssignedUid) ? string.Empty : e.AssignedUid!));
        return who;
    }

    private static string StripHomeworld(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return string.Empty;
        var idx = username.IndexOf('@');
        if (idx <= 0) return username;
        return username.Substring(0, idx);
    }

    private static string BuildShiftLabel(ShiftEntry e, string whoLabel)
    {
        var s = TimeFormat.FormatTime(e.StartAt);
        var ee = TimeFormat.FormatTime(e.EndAt);
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
        _isDjForm = !string.IsNullOrWhiteSpace(e.DjName);
        if (_isDjForm)
        {
            _selDjName = e.DjName;
            _selUid = null;
            _selJob = null;
        }
        else
        {
            _selUid = string.IsNullOrWhiteSpace(e.AssignedUid) ? null : e.AssignedUid;
            _selJob = string.IsNullOrWhiteSpace(e.Job) ? null : e.Job;
            _selDjName = null;
        }
        _selDjIdx = -1;
        _selUserIdx = -1;
        _selEventIdx = -1;
        _selEventId = e.EventId;
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
