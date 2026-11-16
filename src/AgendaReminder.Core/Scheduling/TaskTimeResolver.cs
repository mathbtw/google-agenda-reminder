using System.Globalization;
using System.Text.RegularExpressions;

namespace AgendaReminder.Core.Scheduling;

/// <summary>
/// Détermine l'heure de rappel d'une tâche Google.
///
/// L'API Google Tasks ne renvoie que la DATE d'échéance (l'heure choisie dans l'UI Google est
/// perdue, le champ "due" vaut toujours minuit UTC). On résout donc l'heure, dans cet ordre :
///   1. une heure réelle dans "due" (si Google se met un jour à la renvoyer) ;
///   2. l'heure lue sur le site Google Agenda par le module web optionnel ;
///   3. une heure écrite dans les notes ou le titre de la tâche ("⏰ 14h30", "@14:30", "à 9h", "14:30"…) ;
///   4. l'heure par défaut des paramètres.
/// </summary>
public static partial class TaskTimeResolver
{
    public readonly record struct Result(DateTimeOffset When, bool IsExplicit, string Origin);

    // Heure précédée d'un marqueur : ⏰ 14h30, @14:30, à 9h, vers 18h, heure: 8h15, rappel 7h
    [GeneratedRegex(
        @"(?:⏰|🕐|@|\b[àa]\b|\bvers\b|\bheure\s*:?|\brappel\s*:?)\s*(?<h>[01]?\d|2[0-3])\s*(?:h|:)\s*(?<m>[0-5]\d)?(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrefixedTime();

    // Heure "nue" mais non ambiguë : 14:30, 14h30 (minutes obligatoires)
    [GeneratedRegex(
        @"(?<![\d:])(?<h>[01]?\d|2[0-3])\s*[:hH]\s*(?<m>[0-5]\d)(?![\d:])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlainTime();

    /// <param name="due">Valeur brute du champ "due" de l'API Tasks (RFC 3339), ex. "2026-11-20T00:00:00.000Z".</param>
    /// <param name="title">Titre de la tâche.</param>
    /// <param name="notes">Notes de la tâche.</param>
    /// <param name="defaultTime">Heure par défaut (HH:mm).</param>
    /// <param name="zone">Fuseau local (null = fuseau du système).</param>
    /// <param name="webTime">Heure lue sur le site Google Agenda (module web), si disponible.</param>
    public static Result? Resolve(string? due, string? title, string? notes, string defaultTime, TimeZoneInfo? zone = null, TimeOnly? webTime = null)
    {
        if (string.IsNullOrWhiteSpace(due))
            return null;
        if (!DateTimeOffset.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dueParsed))
            return null;

        zone ??= TimeZoneInfo.Local;
        var utc = dueParsed.ToUniversalTime();

        // 1. Heure réelle fournie par l'API
        if (utc.TimeOfDay != TimeSpan.Zero)
            return new Result(TimeZoneInfo.ConvertTime(utc, zone), true, "api");

        // La date "due" représente un jour calendaire encodé à minuit UTC : on ne la convertit
        // surtout pas en heure locale (sinon décalage d'un jour selon le fuseau).
        var date = DateOnly.FromDateTime(utc.UtcDateTime);

        // 2. Heure lue sur le site Google Agenda
        if (webTime is { } w)
            return new Result(AtLocal(date, w, zone), true, "web");

        // 3. Heure écrite dans les notes ou le titre
        var found = FindTime(notes, title);
        if (found is { } t)
            return new Result(AtLocal(date, t, zone), true, "text");

        // 4. Heure par défaut
        return new Result(AtLocal(date, ParseTimeOrDefault(defaultTime, new TimeOnly(9, 0)), zone), false, "default");
    }

    /// <summary>Date d'échéance (jour calendaire) d'une valeur "due" de l'API Tasks.</summary>
    public static DateOnly? DueDate(string? due)
    {
        if (string.IsNullOrWhiteSpace(due)
            || !DateTimeOffset.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return null;
        return DateOnly.FromDateTime(parsed.UtcDateTime);
    }

    /// <summary>Cherche une heure dans les textes fournis (prioritaires dans l'ordre donné).</summary>
    public static TimeOnly? FindTime(params string?[] texts)
    {
        foreach (var regex in new[] { PrefixedTime(), PlainTime() })
        {
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var match = regex.Match(text);
                if (!match.Success)
                    continue;
                var h = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
                var m = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
                return new TimeOnly(h, m);
            }
        }
        return null;
    }

    public static TimeOnly ParseTimeOrDefault(string? value, TimeOnly fallback)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && TimeOnly.TryParseExact(value.Trim(), new[] { "HH:mm", "H:mm", "HH\\hmm", "H\\hmm", "H\\h" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return t;
        return fallback;
    }

    /// <summary>Combine une date et une heure "murale" dans le fuseau donné.</summary>
    public static DateTimeOffset AtLocal(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            local = local.AddHours(1); // heure sautée au passage à l'heure d'été
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
