using AgendaReminder.Core.Models;
using AgendaReminder.Core.Settings;

namespace AgendaReminder.Core.Scheduling;

/// <summary>Calcule les moments de déclenchement des rappels pour chaque type d'élément.</summary>
public static class TriggerBuilder
{
    /// <param name="googleMinutes">Rappels "popup" définis dans Google (minutes avant). Vide = aucun défini.</param>
    public static IReadOnlyList<DateTimeOffset> ForTimedEvent(DateTimeOffset start, IEnumerable<int> googleMinutes, AppSettings s)
    {
        var minutes = googleMinutes.ToList();
        if (minutes.Count == 0)
            minutes = s.DefaultEventReminderMinutes.ToList();
        if (s.AlsoRemindAtEventStart)
            minutes.Add(0);
        return minutes.Where(m => m >= 0).Distinct().Select(m => start.AddMinutes(-m)).OrderBy(t => t).ToList();
    }

    /// <param name="dayStart">Minuit local du jour de l'évènement.</param>
    public static IReadOnlyList<DateTimeOffset> ForAllDayEvent(DateOnly day, IEnumerable<int> googleMinutes, AppSettings s, TimeZoneInfo zone)
    {
        var minutes = googleMinutes.Where(m => m >= 0).Distinct().ToList();
        if (minutes.Count > 0)
        {
            // Dans Google, le rappel d'un évènement "journée entière" est relatif à minuit du jour J.
            var midnight = TaskTimeResolver.AtLocal(day, TimeOnly.MinValue, zone);
            return minutes.Select(m => midnight.AddMinutes(-m)).OrderBy(t => t).ToList();
        }
        var time = TaskTimeResolver.ParseTimeOrDefault(s.AllDayReminderTime, new TimeOnly(9, 0));
        return new[] { TaskTimeResolver.AtLocal(day, time, zone) };
    }

    public static IReadOnlyList<DateTimeOffset> ForBirthday(DateOnly day, AppSettings s, TimeZoneInfo zone)
    {
        var time = TaskTimeResolver.ParseTimeOrDefault(s.BirthdayReminderTime, new TimeOnly(9, 0));
        return new[] { TaskTimeResolver.AtLocal(day, time, zone) };
    }

    public static IReadOnlyList<DateTimeOffset> ForTask(DateTimeOffset when, AppSettings s)
    {
        var minutes = s.TaskReminderMinutes.Count == 0 ? new List<int> { 0 } : s.TaskReminderMinutes;
        return minutes.Where(m => m >= 0).Distinct().Select(m => when.AddMinutes(-m)).OrderBy(t => t).ToList();
    }
}
