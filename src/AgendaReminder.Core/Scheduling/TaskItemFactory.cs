using AgendaReminder.Core.Models;
using AgendaReminder.Core.Settings;

namespace AgendaReminder.Core.Scheduling;

/// <summary>Construit (ou reconstruit) l'élément de rappel d'une tâche Google.</summary>
public static class TaskItemFactory
{
    public static ReminderItem? Build(
        string id, string? title, string? notes, string? due, string? source,
        AppSettings settings, TimeZoneInfo zone, TimeOnly? webTime, ReminderItem? template = null)
    {
        var resolved = TaskTimeResolver.Resolve(due, title, notes, settings.DefaultTaskTime, zone, webTime);
        if (resolved is not { } r)
            return null;

        return new ReminderItem
        {
            Id = id,
            Kind = ReminderKind.Task,
            Title = string.IsNullOrWhiteSpace(title) ? "(Tâche sans titre)" : title,
            Start = r.When,
            Notes = notes,
            Source = source,
            TaskDue = due,
            HasExplicitTime = r.IsExplicit,
            Triggers = TriggerBuilder.ForTask(r.When, settings),
            AccountId = template?.AccountId,
            ContainerId = template?.ContainerId,
            SourceId = template?.SourceId,
            Link = template?.Link,
            CanEdit = template?.CanEdit ?? false,
        };
    }

    /// <summary>Recalcule l'heure d'une tâche déjà synchronisée (ex. après une lecture du module web).</summary>
    public static ReminderItem Rebuild(ReminderItem task, AppSettings settings, TimeZoneInfo zone, TimeOnly? webTime)
    {
        if (task.Kind != ReminderKind.Task || task.TaskDue is null)
            return task;
        var title = task.Title == "(Tâche sans titre)" ? null : task.Title;
        return Build(task.Id, title, task.Notes, task.TaskDue, task.Source, settings, zone, webTime, task) ?? task;
    }
}
