using System;
using System.Globalization;

namespace HousingStudio.UI.Model;

/// <summary>When something happened, as the Recent list says it: "today 05.24", "yesterday", "Monday", "3 Oct".</summary>
public static class When
{
    /// <param name="clock">The time of day in the player's own format (the window passes <c>HH:mm</c> in the current culture, so "05.24" or "05:24").</param>
    public static string Relative(DateTime at, DateTime now, Func<DateTime, string> clock)
    {
        var days = (now.Date - at.Date).TotalDays;
        if (days <= 0) return $"today {clock(at)}";
        if (days < 2) return "yesterday";
        if (days < 7) return at.ToString("dddd", CultureInfo.InvariantCulture);
        return at.Year == now.Year
            ? at.ToString("d MMM", CultureInfo.InvariantCulture)
            : at.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
