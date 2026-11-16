using AgendaReminder.Core.Models;

namespace AgendaReminder.Core.Scheduling;

/// <summary>Rappel reporté par l'utilisateur ("snooze").</summary>
public sealed class Snooze
{
    public required ReminderItem Item { get; init; }

    public required DateTimeOffset FireAt { get; init; }
}

/// <summary>État persistant du moteur (rappels déjà affichés, snoozes en cours).</summary>
public sealed class ReminderState
{
    /// <summary>Clé de rappel déjà traité -> moment du déclenchement (pour purge).</summary>
    public Dictionary<string, DateTimeOffset> Fired { get; set; } = new();

    public List<Snooze> Snoozes { get; set; } = new();
}

public sealed record DueReminder(ReminderItem Item, DateTimeOffset FireAt, string Key, bool IsSnooze);

public static class ReminderScheduler
{
    public static string KeyFor(ReminderItem item, DateTimeOffset fireAt) =>
        $"{item.Id}|{fireAt.UtcDateTime:yyyy-MM-ddTHH:mmZ}";

    /// <summary>
    /// Rappels à afficher maintenant. Un rappel dont l'heure est passée depuis plus de <paramref name="grace"/>
    /// (PC éteint, veille…) est ignoré pour éviter une avalanche au démarrage.
    /// </summary>
    public static List<DueReminder> GetDue(IEnumerable<ReminderItem> items, ReminderState state, DateTimeOffset now, TimeSpan grace)
    {
        var due = new List<DueReminder>();

        foreach (var item in items)
        {
            foreach (var trigger in item.Triggers)
            {
                if (trigger > now || now - trigger > grace)
                    continue;
                var key = KeyFor(item, trigger);
                if (state.Fired.ContainsKey(key))
                    continue;
                due.Add(new DueReminder(item, trigger, key, IsSnooze: false));
            }
        }

        foreach (var snooze in state.Snoozes)
        {
            if (snooze.FireAt <= now)
                due.Add(new DueReminder(snooze.Item, snooze.FireAt, "snooze|" + KeyFor(snooze.Item, snooze.FireAt), IsSnooze: true));
        }

        // Si un élément a plusieurs rappels en retard, on n'affiche que le plus récent ;
        // les plus anciens sont marqués comme traités.
        var kept = new List<DueReminder>();
        foreach (var group in due.GroupBy(d => d.Item.Id))
        {
            var ordered = group.OrderByDescending(d => d.FireAt).ToList();
            kept.Add(ordered[0]);
            foreach (var older in ordered.Skip(1))
                MarkHandled(state, older);
        }
        return kept.OrderBy(d => d.FireAt).ToList();
    }

    /// <summary>À appeler une fois le rappel affiché (ou volontairement ignoré).</summary>
    public static void MarkHandled(ReminderState state, DueReminder reminder)
    {
        if (reminder.IsSnooze)
            state.Snoozes.RemoveAll(s => s.Item.Id == reminder.Item.Id && s.FireAt == reminder.FireAt);
        else
            state.Fired[reminder.Key] = reminder.FireAt;
    }

    public static void AddSnooze(ReminderState state, ReminderItem item, DateTimeOffset fireAt)
    {
        state.Snoozes.RemoveAll(s => s.Item.Id == item.Id);
        state.Snoozes.Add(new Snooze { Item = item, FireAt = fireAt });
    }

    /// <summary>Retire les entrées "Fired" trop anciennes pour garder le fichier d'état petit.</summary>
    public static void Prune(ReminderState state, DateTimeOffset now, TimeSpan keep)
    {
        foreach (var key in state.Fired.Where(kv => now - kv.Value > keep).Select(kv => kv.Key).ToList())
            state.Fired.Remove(key);
    }

    /// <summary>Prochains rappels prévus (pour l'affichage dans le menu).</summary>
    public static IEnumerable<(ReminderItem Item, DateTimeOffset FireAt)> Upcoming(IEnumerable<ReminderItem> items, ReminderState state, DateTimeOffset now)
    {
        return items
            .SelectMany(i => i.Triggers.Where(t => t > now).Select(t => (Item: i, FireAt: t)))
            .Concat(state.Snoozes.Where(s => s.FireAt > now).Select(s => (Item: s.Item, FireAt: s.FireAt)))
            .OrderBy(x => x.FireAt);
    }
}
