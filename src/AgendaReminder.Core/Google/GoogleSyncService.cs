using System.Globalization;
using AgendaReminder.Core.Models;
using AgendaReminder.Core.Scheduling;
using AgendaReminder.Core.Settings;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;

namespace AgendaReminder.Core.Google;

/// <summary>Connexion OAuth et lecture des évènements, anniversaires et tâches Google.</summary>
public sealed class GoogleSyncService : IDisposable
{
    private static readonly string[] Scopes =
    {
        CalendarService.Scope.CalendarReadonly,
        TasksService.Scope.TasksReadonly,
    };

    private CalendarService? _calendar;
    private TasksService? _tasks;

    public bool IsConnected => _calendar is not null;

    /// <summary>
    /// Connecte le compte Google. La première fois, ouvre le navigateur pour le consentement ;
    /// ensuite le refresh token stocké dans %APPDATA%\AgendaReminder\token est réutilisé.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        if (!File.Exists(AppPaths.ClientSecret))
            throw new FileNotFoundException(
                "Fichier d'identifiants OAuth introuvable. Placez votre client_secret.json (type « Application de bureau ») ici : "
                + AppPaths.ClientSecret, AppPaths.ClientSecret);

        await using var stream = File.OpenRead(AppPaths.ClientSecret);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, Scopes, "user", ct, new FileDataStore(AppPaths.TokenStore, fullPath: true));

        var init = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AgendaReminder",
        };
        _calendar = new CalendarService(init);
        _tasks = new TasksService(init);
    }

    /// <summary>Déconnecte et supprime le jeton enregistré.</summary>
    public void SignOut()
    {
        Dispose();
        if (Directory.Exists(AppPaths.TokenStore))
            Directory.Delete(AppPaths.TokenStore, recursive: true);
    }

    public async Task<List<ReminderItem>> FetchAsync(AppSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        if (_calendar is null || _tasks is null)
            throw new InvalidOperationException("Non connecté à Google.");

        var zone = TimeZoneInfo.Local;
        var items = new List<ReminderItem>();
        items.AddRange(await FetchCalendarAsync(_calendar, settings, now, zone, ct));
        items.AddRange(await FetchTasksAsync(_tasks, settings, now, zone, ct));
        return items;
    }

    private static async Task<List<ReminderItem>> FetchCalendarAsync(
        CalendarService service, AppSettings settings, DateTimeOffset now, TimeZoneInfo zone, CancellationToken ct)
    {
        var result = new List<ReminderItem>();

        var calendars = new List<CalendarListEntry>();
        string? pageToken = null;
        do
        {
            var req = service.CalendarList.List();
            req.PageToken = pageToken;
            var page = await req.ExecuteAsync(ct);
            calendars.AddRange(page.Items ?? new List<CalendarListEntry>());
            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        foreach (var cal in calendars)
        {
            // On suit les agendas cochés dans l'interface Google, moins ceux ignorés dans les paramètres.
            if (cal.Selected != true || settings.IgnoredCalendarIds.Contains(cal.Id))
                continue;

            var defaultPopup = (cal.DefaultReminders ?? new List<EventReminder>())
                .Where(r => r.Method == "popup" && r.Minutes is not null)
                .Select(r => r.Minutes!.Value)
                .ToList();

            pageToken = null;
            do
            {
                var req = service.Events.List(cal.Id);
                req.TimeMinDateTimeOffset = now.AddDays(-1);
                req.TimeMaxDateTimeOffset = now.AddDays(settings.LookAheadDays);
                req.SingleEvents = true; // développe les évènements récurrents
                req.ShowDeleted = false;
                req.MaxResults = 2500;
                req.PageToken = pageToken;
                var page = await req.ExecuteAsync(ct);

                foreach (var ev in page.Items ?? new List<Event>())
                {
                    var item = MapEvent(ev, cal, defaultPopup, settings, zone);
                    if (item is not null)
                        result.Add(item);
                }
                pageToken = page.NextPageToken;
            } while (pageToken is not null);
        }

        return result;
    }

    private static ReminderItem? MapEvent(Event ev, CalendarListEntry cal, List<int> defaultPopup, AppSettings settings, TimeZoneInfo zone)
    {
        if (ev.Status == "cancelled" || ev.Start is null)
            return null;
        if (ev.Attendees?.Any(a => a.Self == true && a.ResponseStatus == "declined") == true)
            return null;
        // Types sans intérêt pour un rappel
        if (ev.EventType is "workingLocation" or "outOfOffice" or "focusTime")
            return null;

        var isBirthday = ev.EventType == "birthday";
        var title = string.IsNullOrWhiteSpace(ev.Summary) ? "(Sans titre)" : ev.Summary;

        // Rappels popup définis dans Google pour cet évènement
        List<int> popupMinutes;
        if (ev.Reminders is null || ev.Reminders.UseDefault == true)
            popupMinutes = defaultPopup;
        else
            popupMinutes = (ev.Reminders.Overrides ?? new List<EventReminder>())
                .Where(r => r.Method == "popup" && r.Minutes is not null)
                .Select(r => r.Minutes!.Value)
                .ToList();

        var id = $"evt:{cal.Id}:{ev.Id}";

        if (ev.Start.DateTimeDateTimeOffset is { } start)
        {
            return new ReminderItem
            {
                Id = id,
                Kind = isBirthday ? ReminderKind.Birthday : ReminderKind.Event,
                Title = title,
                Start = start,
                End = ev.End?.DateTimeDateTimeOffset,
                Location = ev.Location,
                Notes = ev.Description,
                Source = cal.SummaryOverride ?? cal.Summary,
                Triggers = TriggerBuilder.ForTimedEvent(start, popupMinutes, settings),
            };
        }

        if (ev.Start.Date is { } dateText
            && DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            var triggers = isBirthday
                ? TriggerBuilder.ForBirthday(day, settings, zone)
                : TriggerBuilder.ForAllDayEvent(day, ev.Reminders?.UseDefault == false ? popupMinutes : Array.Empty<int>(), settings, zone);

            return new ReminderItem
            {
                Id = id,
                Kind = isBirthday ? ReminderKind.Birthday : ReminderKind.Event,
                Title = title,
                Start = TaskTimeResolver.AtLocal(day, TimeOnly.MinValue, zone),
                AllDay = true,
                Location = ev.Location,
                Notes = ev.Description,
                Source = cal.SummaryOverride ?? cal.Summary,
                Triggers = triggers,
            };
        }

        return null;
    }

    private static async Task<List<ReminderItem>> FetchTasksAsync(
        TasksService service, AppSettings settings, DateTimeOffset now, TimeZoneInfo zone, CancellationToken ct)
    {
        var result = new List<ReminderItem>();

        var lists = new List<global::Google.Apis.Tasks.v1.Data.TaskList>();
        string? pageToken = null;
        do
        {
            var req = service.Tasklists.List();
            req.MaxResults = 100;
            req.PageToken = pageToken;
            var page = await req.ExecuteAsync(ct);
            lists.AddRange(page.Items ?? new List<global::Google.Apis.Tasks.v1.Data.TaskList>());
            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        // "due" est une date à minuit UTC : on élargit la fenêtre d'un jour de chaque côté.
        var dueMin = now.UtcDateTime.Date.AddDays(-1);
        var dueMax = now.UtcDateTime.Date.AddDays(settings.LookAheadDays + 1);

        foreach (var list in lists)
        {
            pageToken = null;
            do
            {
                var req = service.Tasks.List(list.Id);
                req.ShowCompleted = false;
                req.ShowHidden = false;
                req.ShowDeleted = false;
                req.DueMin = dueMin.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                req.DueMax = dueMax.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                req.MaxResults = 100;
                req.PageToken = pageToken;
                var page = await req.ExecuteAsync(ct);

                foreach (var task in page.Items ?? new List<global::Google.Apis.Tasks.v1.Data.Task>())
                {
                    if (task.Status == "completed" || task.Deleted == true)
                        continue;
                    var resolved = TaskTimeResolver.Resolve(task.Due, task.Title, task.Notes, settings.DefaultTaskTime, zone);
                    if (resolved is not { } r)
                        continue;

                    result.Add(new ReminderItem
                    {
                        Id = $"task:{list.Id}:{task.Id}",
                        Kind = ReminderKind.Task,
                        Title = string.IsNullOrWhiteSpace(task.Title) ? "(Tâche sans titre)" : task.Title,
                        Start = r.When,
                        Notes = task.Notes,
                        Source = list.Title,
                        HasExplicitTime = r.IsExplicit,
                        Triggers = TriggerBuilder.ForTask(r.When, settings),
                    });
                }
                pageToken = page.NextPageToken;
            } while (pageToken is not null);
        }

        return result;
    }

    public void Dispose()
    {
        _calendar?.Dispose();
        _tasks?.Dispose();
        _calendar = null;
        _tasks = null;
    }
}
