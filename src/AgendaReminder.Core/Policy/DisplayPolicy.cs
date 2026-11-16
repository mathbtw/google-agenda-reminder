using AgendaReminder.Core.Settings;

namespace AgendaReminder.Core.Policy;

/// <summary>Ce qui est au premier plan au moment du rappel.</summary>
/// <param name="ProcessName">Nom du processus de la fenêtre active (ex. "valorant.exe"), null si inconnu.</param>
/// <param name="IsFullscreenOrBusy">
/// Windows signale un jeu plein écran, une présentation, un appel ou une app plein écran
/// (SHQueryUserNotificationState).
/// </param>
public sealed record ForegroundInfo(string? ProcessName, bool IsFullscreenOrBusy);

public enum DisplayDecision
{
    FullScreen,
    Toast,

    /// <summary>Réessayer plus tard (l'app bloquante est toujours au premier plan).</summary>
    Postpone,

    /// <summary>Ne rien afficher, considérer comme traité.</summary>
    Drop,
}

public static class DisplayPolicy
{
    public static DisplayDecision Decide(DisplayStyle wanted, ForegroundInfo foreground, AppSettings settings, TimeSpan postponedFor)
    {
        if (wanted == DisplayStyle.None)
            return DisplayDecision.Drop;

        var action = FindBlacklistAction(foreground, settings);

        var natural = wanted == DisplayStyle.FullScreen ? DisplayDecision.FullScreen : DisplayDecision.Toast;
        return action switch
        {
            null => natural,
            BlacklistAction.ToastOnly => DisplayDecision.Toast,
            BlacklistAction.Silent => DisplayDecision.Drop,
            BlacklistAction.Postpone when postponedFor >= TimeSpan.FromMinutes(settings.MaxPostponeMinutes) => DisplayDecision.Toast,
            BlacklistAction.Postpone => DisplayDecision.Postpone,
            _ => natural,
        };
    }

    public static BlacklistAction? FindBlacklistAction(ForegroundInfo foreground, AppSettings settings)
    {
        if (foreground.ProcessName is { } name)
        {
            var normalized = Normalize(name);
            var entry = settings.Blacklist.FirstOrDefault(e => Normalize(e.Process) == normalized);
            if (entry is not null)
                return entry.Action;
        }

        if (settings.AutoDetectFullscreenApps && foreground.IsFullscreenOrBusy)
            return settings.AutoDetectAction;

        return null;
    }

    public static string Normalize(string processName)
    {
        var n = Path.GetFileName(processName.Trim()).ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n[..^4] : n;
    }
}
