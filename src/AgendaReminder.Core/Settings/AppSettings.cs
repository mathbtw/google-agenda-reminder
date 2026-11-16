using System.Text.Json.Serialization;
using AgendaReminder.Core.Models;

namespace AgendaReminder.Core.Settings;

/// <summary>Comment un rappel est présenté.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisplayStyle
{
    /// <summary>Petite notification maison en bas à droite.</summary>
    Toast,

    /// <summary>Rappel plein écran épuré.</summary>
    FullScreen,

    /// <summary>Aucun affichage.</summary>
    None,
}

/// <summary>Que faire quand une application blacklistée est au premier plan.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BlacklistAction
{
    /// <summary>Remplace le plein écran par un simple toast.</summary>
    ToastOnly,

    /// <summary>Attend que l'application quitte le premier plan, puis affiche le rappel normalement.</summary>
    Postpone,

    /// <summary>N'affiche rien du tout (le rappel est considéré comme vu).</summary>
    Silent,
}

public sealed class BlacklistEntry
{
    /// <summary>Nom du processus, avec ou sans ".exe" (insensible à la casse). Ex : "valorant.exe", "obs64".</summary>
    public string Process { get; set; } = "";

    public BlacklistAction Action { get; set; } = BlacklistAction.Postpone;
}

public sealed class AppSettings
{
    /// <summary>Intervalle de synchronisation avec Google, en minutes.</summary>
    public int SyncIntervalMinutes { get; set; } = 3;

    /// <summary>Nombre de jours à l'avance récupérés depuis Google.</summary>
    public int LookAheadDays { get; set; } = 14;

    /// <summary>Rappel par défaut (minutes avant) pour un évènement sans rappel configuré dans Google. Vide = aucun.</summary>
    public List<int> DefaultEventReminderMinutes { get; set; } = new() { 10 };

    /// <summary>Rappel supplémentaire au moment exact du début de l'évènement.</summary>
    public bool AlsoRemindAtEventStart { get; set; } = false;

    /// <summary>Heure du rappel pour les évènements "journée entière" (format HH:mm).</summary>
    public string AllDayReminderTime { get; set; } = "09:00";

    /// <summary>Heure du rappel pour les anniversaires (format HH:mm).</summary>
    public string BirthdayReminderTime { get; set; } = "09:00";

    /// <summary>
    /// Heure utilisée pour une tâche quand aucune heure n'est trouvée (l'API Google Tasks ne fournit que la date).
    /// </summary>
    public string DefaultTaskTime { get; set; } = "09:00";

    /// <summary>Rappels (minutes avant l'heure résolue) pour les tâches. 0 = à l'heure pile.</summary>
    public List<int> TaskReminderMinutes { get; set; } = new() { 0 };

    /// <summary>Style d'affichage par type d'élément.</summary>
    public Dictionary<ReminderKind, DisplayStyle> Display { get; set; } = new()
    {
        [ReminderKind.Event] = DisplayStyle.FullScreen,
        [ReminderKind.Birthday] = DisplayStyle.Toast,
        [ReminderKind.Task] = DisplayStyle.FullScreen,
    };

    /// <summary>Applications qui bloquent ou modifient l'affichage des rappels.</summary>
    public List<BlacklistEntry> Blacklist { get; set; } = new()
    {
        new BlacklistEntry { Process = "obs64.exe", Action = BlacklistAction.ToastOnly },
    };

    /// <summary>
    /// Traite automatiquement les jeux plein écran / mode présentation / appels comme une app blacklistée.
    /// </summary>
    public bool AutoDetectFullscreenApps { get; set; } = true;

    public BlacklistAction AutoDetectAction { get; set; } = BlacklistAction.Postpone;

    /// <summary>Au-delà de ce délai, un rappel reporté s'affiche quand même en toast.</summary>
    public int MaxPostponeMinutes { get; set; } = 120;

    public List<int> SnoozeOptionsMinutes { get; set; } = new() { 5, 10, 30 };

    /// <summary>Durée d'affichage d'un toast avant fermeture automatique (0 = reste affiché).</summary>
    public int ToastDurationSeconds { get; set; } = 20;

    /// <summary>Un rappel manqué (PC éteint, en veille…) est encore affiché s'il date de moins de N minutes.</summary>
    public int MissedReminderGraceMinutes { get; set; } = 60;

    public bool PlaySound { get; set; } = true;

    /// <summary>Identifiants d'agendas à ignorer (laisser vide pour tous les agendas cochés dans Google).</summary>
    public List<string> IgnoredCalendarIds { get; set; } = new();
}
