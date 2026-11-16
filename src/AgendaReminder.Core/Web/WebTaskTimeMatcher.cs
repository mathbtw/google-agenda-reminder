using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AgendaReminder.Core.Web;

/// <summary>Tâche connue via l'API officielle, dont on cherche l'heure sur la page Google Agenda.</summary>
public sealed record TaskToMatch(string Id, string Title, DateOnly DueDate);

/// <summary>
/// Retrouve l'heure des tâches dans les textes extraits de la vue "jour" de Google Agenda
/// (aria-label et texte des éléments affichés).
///
/// Volontairement prudent : on préfère ne rien renvoyer (=> repli sur l'heure écrite / par défaut)
/// plutôt qu'une heure douteuse. Le format exact de la page Google n'est pas documenté et peut changer,
/// donc on ne dépend que de deux choses : le titre de la tâche et une heure lisible à côté.
/// </summary>
public static partial class WebTaskTimeMatcher
{
    // 14:30, 14 h 30, 14h30, 9h, 2:30pm, 2 pm, 2:30 p.m.
    [GeneratedRegex(
        @"(?<![\d:])(?<h>[01]?\d|2[0-3])\s*(?:(?<sep>[:h])\s*(?<m>[0-5]\d)?)?\s*(?<ap>[ap]\.?\s?m\.?)?(?![\d:])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"\b(tâche|tache|task|à faire|to-do|todo)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaskWord();

    [GeneratedRegex(@"\b(toute la journée|journée entière|all day|all-day)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AllDayWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>
    /// Associe une heure aux tâches d'un même jour à partir des textes de la vue "jour" de cette date.
    /// </summary>
    /// <param name="snippets">Textes extraits de la page (aria-label, innerText…).</param>
    /// <param name="tasksOfDay">Tâches dont l'échéance est ce jour-là.</param>
    public static Dictionary<string, TimeOnly> Match(IEnumerable<string> snippets, IReadOnlyList<TaskToMatch> tasksOfDay)
    {
        var result = new Dictionary<string, TimeOnly>();
        var normalizedSnippets = snippets
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 600)
            .Select(s => (Raw: s, Norm: Normalize(s)))
            .ToList();

        foreach (var task in tasksOfDay)
        {
            var title = Normalize(task.Title);
            if (title.Length < 2)
                continue;

            // Deux tâches du même jour avec le même titre : impossible de les distinguer.
            if (tasksOfDay.Count(t => Normalize(t.Title) == title) > 1)
                continue;

            // Une autre tâche du jour dont le titre contient celui-ci ("Appeler" vs "Appeler Paul") :
            // on exclut les textes qui correspondent mieux à l'autre tâche.
            var longerTitles = tasksOfDay
                .Select(t => Normalize(t.Title))
                .Where(t => t != title && t.Contains(title, StringComparison.Ordinal))
                .ToList();

            var candidates = normalizedSnippets
                .Where(s => ContainsTitle(s.Norm, title) && !longerTitles.Any(l => ContainsTitle(s.Norm, l)))
                .ToList();
            if (candidates.Count == 0)
                continue;

            // Si certains textes se présentent explicitement comme une tâche, on ne garde qu'eux
            // (évite de confondre avec un évènement qui porterait le même titre).
            var taskLike = candidates.Where(c => TaskWord().IsMatch(c.Norm)).ToList();
            if (taskLike.Count > 0)
                candidates = taskLike;

            var times = candidates
                .Where(c => !AllDayWord().IsMatch(c.Norm))
                .Select(c => ExtractTime(RemoveTitle(c.Norm, title)))
                .Where(t => t is not null)
                .Select(t => t!.Value)
                .Distinct()
                .ToList();

            // Une seule heure cohérente => on la retient ; sinon c'est ambigu, on s'abstient.
            if (times.Count == 1)
                result[task.Id] = times[0];
        }

        return result;
    }

    /// <summary>Première heure lisible dans un texte (format 24 h ou 12 h am/pm).</summary>
    public static TimeOnly? ExtractTime(string text)
    {
        foreach (Match m in TimeRegex().Matches(text))
        {
            var hasSep = m.Groups["sep"].Success;
            var hasAmPm = m.Groups["ap"].Success;
            // Un nombre seul ("3 rappels") n'est pas une heure.
            if (!hasSep && !hasAmPm)
                continue;
            // "2h" sans minutes peut être une durée ; on n'accepte "Xh" que suivi de minutes ou de am/pm.
            if (m.Groups["sep"].Value.Equals("h", StringComparison.OrdinalIgnoreCase) && !m.Groups["m"].Success && !hasAmPm)
            {
                // ...sauf en début de texte ou après une virgule, typique des aria-label ("14 h, Ma tâche").
                var before = text[..m.Index].TrimEnd();
                if (before.Length > 0 && !before.EndsWith(',') && !before.EndsWith('à') && !before.EndsWith("at", StringComparison.Ordinal))
                    continue;
            }

            var h = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            var min = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
            if (hasAmPm)
            {
                if (h is < 1 or > 12) continue;
                var pm = m.Groups["ap"].Value.StartsWith("p", StringComparison.OrdinalIgnoreCase);
                h = (h % 12) + (pm ? 12 : 0);
            }
            return new TimeOnly(h, min);
        }
        return null;
    }

    public static string Normalize(string text)
    {
        // Minuscule, espaces normalisés (y compris insécables), sans accents pour tolérer les variantes.
        var lower = text.Replace(' ', ' ').Replace(' ', ' ').ToLowerInvariant();
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return Spaces().Replace(sb.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    private static bool ContainsTitle(string normalizedText, string normalizedTitle)
    {
        var idx = normalizedText.IndexOf(normalizedTitle, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var startOk = idx == 0 || !char.IsLetterOrDigit(normalizedText[idx - 1]);
            var end = idx + normalizedTitle.Length;
            var endOk = end == normalizedText.Length || !char.IsLetterOrDigit(normalizedText[end]);
            if (startOk && endOk)
                return true;
            idx = normalizedText.IndexOf(normalizedTitle, idx + 1, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>Retire le titre pour ne pas lire une heure qui ferait partie du titre lui-même ("Réunion 10:45").</summary>
    private static string RemoveTitle(string normalizedText, string normalizedTitle) =>
        normalizedText.Replace(normalizedTitle, " , ", StringComparison.Ordinal);
}
