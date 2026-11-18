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

/// <summary>
/// Rappels d'un type d'élément. Minutes avant le début (évènement), l'heure de la tâche, ou l'heure
/// de rappel des anniversaires / évènements "journée entière". Liste vide = pas de rappel de cette forme.
/// </summary>
public sealed class KindReminders
{
    public List<int> NotificationMinutes { get; set; } = new();

    public List<int> FullScreenMinutes { get; set; } = new();

    public static Dictionary<ReminderKind, KindReminders> Defaults() => new()
    {
        [ReminderKind.Event] = new() { NotificationMinutes = { 10 }, FullScreenMinutes = { 2 } },
        [ReminderKind.Task] = new() { NotificationMinutes = { 10 }, FullScreenMinutes = { 0 } },
        [ReminderKind.Birthday] = new() { NotificationMinutes = { 0 } },
    };
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

    /// <summary>
    /// Rappels de l'app pour chaque type d'élément : notifications et plein écran, chacun à ses propres
    /// moments (minutes avant). Ex. évènements : notification 10 min avant, plein écran 2 min avant.
    /// </summary>
    public Dictionary<ReminderKind, KindReminders> Reminders { get; set; } = KindReminders.Defaults();

    /// <summary>Forme des rappels déjà configurés dans Google sur les évènements (None = les ignorer).</summary>
    public DisplayStyle GoogleRemindersStyle { get; set; } = DisplayStyle.Toast;

    /// <summary>Réglages d'un type, avec repli sur les valeurs par défaut s'il manque dans le fichier.</summary>
    public KindReminders RemindersFor(ReminderKind kind) =>
        Reminders.TryGetValue(kind, out var r) && r is not null ? r : KindReminders.Defaults()[kind];

    /// <summary>Heure du rappel pour les évènements "journée entière" (format HH:mm).</summary>
    public string AllDayReminderTime { get; set; } = "09:00";

    /// <summary>Heure du rappel pour les anniversaires (format HH:mm).</summary>
    public string BirthdayReminderTime { get; set; } = "09:00";

    /// <summary>Couleur de chaque type d'élément (#RRGGBB).</summary>
    public Dictionary<ReminderKind, string> Colors { get; set; } = DefaultColors();

    /// <summary>Évènements : utiliser la couleur affichée dans Google Agenda (évènement ou agenda) plutôt que la couleur ci-dessus.</summary>
    public bool UseGoogleCalendarColors { get; set; } = false;

    public static Dictionary<ReminderKind, string> DefaultColors() => new()
    {
        [ReminderKind.Event] = "#4C8BF5",
        [ReminderKind.Task] = "#34C38F",
        [ReminderKind.Birthday] = "#F5A04C",
    };

    public string ColorFor(ReminderKind kind) =>
        Colors.TryGetValue(kind, out var c) && !string.IsNullOrWhiteSpace(c) ? c : DefaultColors()[kind];

    /// <summary>Vérifie au démarrage puis toutes les 6 h si une nouvelle version est publiée.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Heure utilisée pour une tâche quand aucune heure n'est trouvée (l'API Google Tasks ne fournit que la date).
    /// </summary>
    public string DefaultTaskTime { get; set; } = "09:00";

    /// <summary>
    /// Module expérimental : lit l'heure réelle des tâches sur le site Google Agenda via un navigateur
    /// intégré (WebView2) connecté à votre compte. Désactivé par défaut ; en cas d'échec, l'app revient
    /// automatiquement à l'heure écrite dans la tâche / l'heure par défaut.
    /// </summary>
    public bool EnableWebTaskTimes { get; set; } = false;

    /// <summary>Fréquence de lecture du site Google Agenda par le module web, en minutes.</summary>
    public int WebTaskTimesRefreshMinutes { get; set; } = 30;

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
