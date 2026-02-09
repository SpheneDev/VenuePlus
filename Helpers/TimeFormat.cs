using System;
using System.Globalization;

namespace VenuePlus.Helpers;

public static class TimeFormat
{
    private static readonly bool UseAmPmTime = IsAmPmCulture();

    public static string FormatTime(DateTimeOffset value, bool includeSeconds = false)
    {
        var fmt = includeSeconds
            ? (UseAmPmTime ? "hh:mm:ss tt" : "HH:mm:ss")
            : (UseAmPmTime ? "hh:mm tt" : "HH:mm");
        return value.ToString(fmt, CultureInfo.CurrentCulture);
    }

    public static string FormatTime(DateTime value, bool includeSeconds = false)
    {
        var fmt = includeSeconds
            ? (UseAmPmTime ? "hh:mm:ss tt" : "HH:mm:ss")
            : (UseAmPmTime ? "hh:mm tt" : "HH:mm");
        return value.ToString(fmt, CultureInfo.CurrentCulture);
    }

    public static string FormatDate(DateTimeOffset value)
    {
        return value.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
    }

    public static string FormatDate(DateTime value)
    {
        return value.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
    }

    public static string FormatDateTime(DateTimeOffset value, bool includeSeconds = false)
    {
        return FormatDate(value) + " " + FormatTime(value, includeSeconds);
    }

    public static string FormatDateTimeUtc(DateTimeOffset value, bool includeSeconds = false)
    {
        return FormatDateTime(value.ToUniversalTime(), includeSeconds) + " UTC";
    }

    public static string FormatHourOption(int hour)
    {
        if (!UseAmPmTime) return hour.ToString("00", CultureInfo.CurrentCulture);
        var dt = new DateTime(2000, 1, 1, hour, 0, 0, DateTimeKind.Unspecified);
        return dt.ToString("hh tt", CultureInfo.CurrentCulture);
    }

    private static bool IsAmPmCulture()
    {
        var pattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern ?? string.Empty;
        return pattern.IndexOf('t') >= 0 || pattern.IndexOf('T') >= 0;
    }
}
