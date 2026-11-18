using AgendaReminder.Core.Models;
using AgendaReminder.Core.Settings;

namespace AgendaReminder.Core.Scheduling;

/// <summary>
/// Calcule les rappels (moment + forme) de chaque élément. Pour chaque type, l'utilisateur choisit
/// les minutes "avant" des notifications et celles du plein écran ; si les deux tombent au même
/// moment, le plein écran l'emporte.
/// </summary>
public static class TriggerBuilder
{
    /// <param name="googleMinutes">Rappels "popup" définis dans Google pour l'évènement (minutes avant).</param>
    public static IReadOnlyList<ReminderTrigger> ForTimedEvent(DateTimeOffset start, IEnumerable<int> googleMinutes, AppSettings s)
    {
        var entries = FromKind(start, s.RemindersFor(ReminderKind.Event)).ToList();
        if (s.GoogleRemindersStyle != DisplayStyle.None)
            entries.AddRange(googleMinutes.Where(m => m >= 0).Select(m => (start.AddMinutes(-m), s.GoogleRemindersStyle)));
        return Merge(entries);
    }

    /// <summary>
    /// Évènement "journée entière" : les rappels de l'app sont relatifs à l'heure <see cref="AppSettings.AllDayReminderTime"/> ;
    /// ceux de Google sont relatifs à minuit du jour J (convention Google).
    /// </summary>
    public static IReadOnlyList<ReminderTrigger> ForAllDayEvent(DateOnly day, IEnumerable<int> googleMinutes, AppSettings s, TimeZoneInfo zone)
    {
        var time = TaskTimeResolver.ParseTimeOrDefault(s.AllDayReminderTime, new TimeOnly(9, 0));
        var anchor = TaskTimeResolver.AtLocal(day, time, zone);
        var entries = FromKind(anchor, s.RemindersFor(ReminderKind.Event)).ToList();
        if (s.GoogleRemindersStyle != DisplayStyle.None)
        {
            var midnight = TaskTimeResolver.AtLocal(day, TimeOnly.MinValue, zone);
            entries.AddRange(googleMinutes.Where(m => m >= 0).Select(m => (midnight.AddMinutes(-m), s.GoogleRemindersStyle)));
        }
        return Merge(entries);
    }

    /// <summary>Anniversaire : rappels relatifs à <see cref="AppSettings.BirthdayReminderTime"/>.</summary>
    public static IReadOnlyList<ReminderTrigger> ForBirthday(DateOnly day, AppSettings s, TimeZoneInfo zone)
    {
        var time = TaskTimeResolver.ParseTimeOrDefault(s.BirthdayReminderTime, new TimeOnly(9, 0));
        return Merge(FromKind(TaskTimeResolver.AtLocal(day, time, zone), s.RemindersFor(ReminderKind.Birthday)));
    }

    /// <summary>Tâche : rappels relatifs à l'heure résolue de la tâche.</summary>
    public static IReadOnlyList<ReminderTrigger> ForTask(DateTimeOffset when, AppSettings s) =>
        Merge(FromKind(when, s.RemindersFor(ReminderKind.Task)));

    private static IEnumerable<(DateTimeOffset At, DisplayStyle Style)> FromKind(DateTimeOffset anchor, KindReminders k) =>
        k.ActiveNotificationMinutes.Where(m => m >= 0).Select(m => (anchor.AddMinutes(-m), DisplayStyle.Toast))
            .Concat(k.ActiveFullScreenMinutes.Where(m => m >= 0).Select(m => (anchor.AddMinutes(-m), DisplayStyle.FullScreen)));

    /// <summary>Un seul rappel par instant ; le plein écran l'emporte sur la notification.</summary>
    private static IReadOnlyList<ReminderTrigger> Merge(IEnumerable<(DateTimeOffset At, DisplayStyle Style)> entries) =>
        entries
            .Where(e => e.Style != DisplayStyle.None)
            .GroupBy(e => e.At)
            .Select(g => new ReminderTrigger(g.Key,
                g.Any(e => e.Style == DisplayStyle.FullScreen) ? DisplayStyle.FullScreen : DisplayStyle.Toast))
            .OrderBy(t => t.At)
            .ToList();
}
