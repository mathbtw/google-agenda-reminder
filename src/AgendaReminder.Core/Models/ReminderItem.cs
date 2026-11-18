using AgendaReminder.Core.Settings;

namespace AgendaReminder.Core.Models;

/// <summary>Un déclenchement de rappel : à quel moment, et sous quelle forme.</summary>
public sealed record ReminderTrigger(DateTimeOffset At, DisplayStyle Style);

public enum ReminderKind
{
    Event,
    Birthday,
    Task,
}

/// <summary>
/// Un élément Google (évènement, anniversaire ou tâche) normalisé pour le moteur de rappels.
/// </summary>
public sealed record ReminderItem
{
    /// <summary>Identifiant stable, préfixé par la source (ex. "evt:calId:eventId", "task:listId:taskId").</summary>
    public required string Id { get; init; }

    public required ReminderKind Kind { get; init; }

    public required string Title { get; init; }

    /// <summary>Début de l'évènement, ou moment d'échéance résolu pour une tâche.</summary>
    public required DateTimeOffset Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public bool AllDay { get; init; }

    public string? Location { get; init; }

    public string? Notes { get; init; }

    /// <summary>Nom de l'agenda ou de la liste de tâches d'origine.</summary>
    public string? Source { get; init; }

    /// <summary>Moments auxquels un rappel doit se déclencher.</summary>
    public IReadOnlyList<ReminderTrigger> Triggers { get; init; } = Array.Empty<ReminderTrigger>();

    /// <summary>Pour une tâche : vrai si l'heure vient d'une indication explicite (API ou notes), faux si heure par défaut.</summary>
    public bool HasExplicitTime { get; init; } = true;

    /// <summary>Compte Google d'origine.</summary>
    public string? AccountId { get; init; }

    /// <summary>Agenda (évènement) ou liste de tâches (tâche) contenant l'élément.</summary>
    public string? ContainerId { get; init; }

    /// <summary>Identifiant Google de l'évènement ou de la tâche.</summary>
    public string? SourceId { get; init; }

    /// <summary>Couleur Google de l'évènement ou de son agenda (#RRGGBB), si connue.</summary>
    public string? GoogleColor { get; init; }

    /// <summary>Lien vers l'élément sur le site Google.</summary>
    public string? Link { get; init; }

    /// <summary>Vrai si l'utilisateur peut modifier/supprimer l'élément (agenda en écriture).</summary>
    public bool CanEdit { get; init; }

    /// <summary>Pour une tâche : valeur brute du champ "due" de l'API (permet de recalculer l'heure sans re-synchroniser).</summary>
    public string? TaskDue { get; init; }
}
