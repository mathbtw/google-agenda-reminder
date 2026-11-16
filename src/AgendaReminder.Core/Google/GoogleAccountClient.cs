using System.Globalization;
using System.Text;
using System.Text.Json;
using AgendaReminder.Core.Models;
using AgendaReminder.Core.Scheduling;
using AgendaReminder.Core.Settings;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;
using GTask = Google.Apis.Tasks.v1.Data.Task;
using GTaskList = Google.Apis.Tasks.v1.Data.TaskList;

namespace AgendaReminder.Core.Google;

/// <summary>Le compte n'a pas (ou plus) de jeton valide : il faut se reconnecter.</summary>
public sealed class AccountNeedsLoginException : Exception
{
    public AccountNeedsLoginException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed record CalendarRef(string Id, string Name, bool Writable, bool Primary);

public sealed record TaskListRef(string Id, string Title);

/// <summary>Une tâche Google, pour la page "Tâches" de la fenêtre principale.</summary>
public sealed record TaskEntry(
    string AccountId, string ListId, string ListTitle, string TaskId, string Title,
    string? Notes, DateOnly? DueDate, TimeOnly? Time, string? Link);

/// <summary>Accès Google (Agenda + Tasks) pour UN compte.</summary>
public sealed class GoogleAccountClient : IDisposable
{
    private static readonly string[] Scopes =
    {
        CalendarService.Scope.CalendarReadonly, // liste des agendas
        CalendarService.Scope.CalendarEvents,   // lecture/écriture des évènements
        TasksService.Scope.Tasks,               // lecture/écriture des tâches
        "openid",
        "email",
    };

    private const string UserKey = "user";

    private readonly CalendarService _calendar;
    private readonly TasksService _tasks;
    private readonly UserCredential _credential;

    public string AccountId { get; }

    private GoogleAccountClient(string accountId, UserCredential credential)
    {
        AccountId = accountId;
        _credential = credential;
        var init = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AgendaReminder",
        };
        _calendar = new CalendarService(init);
        _tasks = new TasksService(init);
    }

    public static bool HasClientSecret => File.Exists(AppPaths.ClientSecret);

    private static async Task<GoogleAuthorizationCodeFlow> CreateFlowAsync(string accountId, bool selectAccount, CancellationToken ct)
    {
        if (!HasClientSecret)
            throw new FileNotFoundException("client_secret.json introuvable (" + AppPaths.ClientSecret + ")", AppPaths.ClientSecret);

        await using var stream = File.OpenRead(AppPaths.ClientSecret);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = Scopes,
            DataStore = new FileDataStore(AppPaths.TokenStoreFor(accountId), fullPath: true),
            // Permet de choisir un autre compte que celui connecté dans le navigateur.
            Prompt = selectAccount ? "select_account consent" : null,
        });
    }

    /// <summary>Ajoute un compte : ouvre le navigateur pour la connexion et renvoie le client + l'e-mail.</summary>
    public static async Task<(GoogleAccountClient Client, string Email)> AddInteractiveAsync(string accountId, CancellationToken ct)
    {
        var flow = await CreateFlowAsync(accountId, selectAccount: true, ct);
        var app = new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver());
        var credential = await app.AuthorizeAsync(UserKey, ct);
        var email = EmailFromIdToken(credential.Token.IdToken) ?? "Compte Google";
        return (new GoogleAccountClient(accountId, credential), email);
    }

    /// <summary>Ouvre un compte déjà connecté, sans jamais ouvrir le navigateur.</summary>
    public static async Task<GoogleAccountClient> OpenAsync(string accountId, CancellationToken ct)
    {
        var flow = await CreateFlowAsync(accountId, selectAccount: false, ct);
        var token = await flow.LoadTokenAsync(UserKey, ct);
        if (token is null || (token.RefreshToken is null && token.IsStale))
            throw new AccountNeedsLoginException("Reconnexion nécessaire");
        return new GoogleAccountClient(accountId, new UserCredential(flow, UserKey, token));
    }

    /// <summary>Révoque l'accès chez Google (au mieux) et supprime le jeton local.</summary>
    public async Task RevokeAsync(CancellationToken ct)
    {
        try { await _credential.RevokeTokenAsync(ct); }
        catch (Exception) { /* déjà révoqué / hors ligne : on supprime quand même localement */ }
    }

    public static void DeleteLocalToken(string accountId)
    {
        var dir = AppPaths.TokenStoreFor(accountId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    internal static string? EmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Convertit les erreurs d'authentification en <see cref="AccountNeedsLoginException"/>.</summary>
    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (TokenResponseException ex)
        {
            throw new AccountNeedsLoginException("Accès révoqué ou expiré : reconnectez le compte", ex);
        }
        catch (global::Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new AccountNeedsLoginException("Accès refusé : reconnectez le compte", ex);
        }
        catch (global::Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden
            && ex.Message.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
        {
            throw new AccountNeedsLoginException("Autorisations insuffisantes : reconnectez le compte", ex);
        }
    }

    // ───────────────────────────── Lecture ─────────────────────────────

    public Task<List<CalendarListEntry>> ListCalendarEntriesAsync(CancellationToken ct) => Guard(async () =>
    {
        var calendars = new List<CalendarListEntry>();
        string? pageToken = null;
        do
        {
            var req = _calendar.CalendarList.List();
            req.PageToken = pageToken;
            var page = await req.ExecuteAsync(ct);
            calendars.AddRange(page.Items ?? new List<CalendarListEntry>());
            pageToken = page.NextPageToken;
        } while (pageToken is not null);
        return calendars;
    });

    public async Task<List<CalendarRef>> ListCalendarsAsync(CancellationToken ct) =>
        (await ListCalendarEntriesAsync(ct))
            .Select(c => new CalendarRef(c.Id, c.SummaryOverride ?? c.Summary ?? c.Id,
                c.AccessRole is "owner" or "writer", c.Primary == true))
            .OrderByDescending(c => c.Primary).ThenBy(c => c.Name)
            .ToList();

    public Task<List<TaskListRef>> ListTaskListsAsync(CancellationToken ct) => Guard(async () =>
    {
        var lists = new List<TaskListRef>();
        string? pageToken = null;
        do
        {
            var req = _tasks.Tasklists.List();
            req.MaxResults = 100;
            req.PageToken = pageToken;
            var page = await req.ExecuteAsync(ct);
            lists.AddRange((page.Items ?? new List<GTaskList>()).Select(l => new TaskListRef(l.Id, l.Title ?? "Mes tâches")));
            pageToken = page.NextPageToken;
        } while (pageToken is not null);
        return lists;
    });

    /// <summary>Évènements, anniversaires et tâches à venir de ce compte, sous forme de rappels.</summary>
    /// <param name="webTimes">Heures de tâches lues par le module web (id d'élément -> heure), optionnel.</param>
    public async Task<List<ReminderItem>> FetchAsync(
        AppSettings settings, DateTimeOffset now, IReadOnlyDictionary<string, TimeOnly>? webTimes, CancellationToken ct)
    {
        var zone = TimeZoneInfo.Local;
        var items = new List<ReminderItem>();
        items.AddRange(await FetchCalendarAsync(settings, now, zone, ct));
        items.AddRange(await FetchTasksAsync(settings, now, zone, webTimes, ct));
        return items;
    }

    private Task<List<ReminderItem>> FetchCalendarAsync(AppSettings settings, DateTimeOffset now, TimeZoneInfo zone, CancellationToken ct) => Guard(async () =>
    {
        var result = new List<ReminderItem>();
        foreach (var cal in await ListCalendarEntriesAsync(ct))
        {
            // On suit les agendas cochés dans l'interface Google, moins ceux ignorés dans les paramètres.
            if (cal.Selected != true || settings.IgnoredCalendarIds.Contains(cal.Id))
                continue;

            var defaultPopup = (cal.DefaultReminders ?? new List<EventReminder>())
                .Where(r => r.Method == "popup" && r.Minutes is not null)
                .Select(r => r.Minutes!.Value)
                .ToList();

            string? pageToken = null;
            do
            {
                var req = _calendar.Events.List(cal.Id);
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
    });

    private ReminderItem? MapEvent(Event ev, CalendarListEntry cal, List<int> defaultPopup, AppSettings settings, TimeZoneInfo zone)
    {
        if (ev.Status == "cancelled" || ev.Start is null)
            return null;
        if (ev.Attendees?.Any(a => a.Self == true && a.ResponseStatus == "declined") == true)
            return null;
        if (ev.EventType is "workingLocation" or "outOfOffice" or "focusTime")
            return null;

        var isBirthday = ev.EventType == "birthday";
        var title = string.IsNullOrWhiteSpace(ev.Summary) ? "(Sans titre)" : ev.Summary;

        List<int> popupMinutes;
        if (ev.Reminders is null || ev.Reminders.UseDefault == true)
            popupMinutes = defaultPopup;
        else
            popupMinutes = (ev.Reminders.Overrides ?? new List<EventReminder>())
                .Where(r => r.Method == "popup" && r.Minutes is not null)
                .Select(r => r.Minutes!.Value)
                .ToList();

        var common = new ReminderItem
        {
            Id = $"{AccountId}/evt:{cal.Id}:{ev.Id}",
            Kind = isBirthday ? ReminderKind.Birthday : ReminderKind.Event,
            Title = title,
            Start = DateTimeOffset.MinValue,
            Location = ev.Location,
            Notes = ev.Description,
            Source = cal.SummaryOverride ?? cal.Summary,
            AccountId = AccountId,
            ContainerId = cal.Id,
            SourceId = ev.Id,
            Link = ev.HtmlLink,
            CanEdit = !isBirthday && cal.AccessRole is "owner" or "writer",
        };

        if (ev.Start.DateTimeDateTimeOffset is { } start)
        {
            return common with
            {
                Start = start,
                End = ev.End?.DateTimeDateTimeOffset,
                Triggers = TriggerBuilder.ForTimedEvent(start, popupMinutes, settings),
            };
        }

        if (ev.Start.Date is { } dateText
            && DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            var triggers = isBirthday
                ? TriggerBuilder.ForBirthday(day, settings, zone)
                : TriggerBuilder.ForAllDayEvent(day, ev.Reminders?.UseDefault == false ? popupMinutes : Array.Empty<int>(), settings, zone);
            return common with
            {
                Start = TaskTimeResolver.AtLocal(day, TimeOnly.MinValue, zone),
                AllDay = true,
                Triggers = triggers,
            };
        }

        return null;
    }

    private Task<List<ReminderItem>> FetchTasksAsync(
        AppSettings settings, DateTimeOffset now, TimeZoneInfo zone,
        IReadOnlyDictionary<string, TimeOnly>? webTimes, CancellationToken ct) => Guard(async () =>
    {
        var result = new List<ReminderItem>();
        // "due" est une date à minuit UTC : on élargit la fenêtre d'un jour de chaque côté.
        var dueMin = now.UtcDateTime.Date.AddDays(-1);
        var dueMax = now.UtcDateTime.Date.AddDays(settings.LookAheadDays + 1);

        foreach (var list in await ListTaskListsAsync(ct))
        {
            foreach (var task in await ListTasksAsync(list.Id, dueMin, dueMax, ct))
            {
                var id = $"{AccountId}/task:{list.Id}:{task.Id}";
                TimeOnly? webTime = webTimes is not null && webTimes.TryGetValue(id, out var w) ? w : null;
                var template = new ReminderItem
                {
                    Id = id, Kind = ReminderKind.Task, Title = "", Start = default,
                    AccountId = AccountId, ContainerId = list.Id, SourceId = task.Id,
                    Link = task.WebViewLink, CanEdit = true,
                };
                var item = TaskItemFactory.Build(id, task.Title, task.Notes, task.Due, list.Title, settings, zone, webTime, template);
                if (item is not null)
                    result.Add(item);
            }
        }
        return result;
    });

    private async Task<List<GTask>> ListTasksAsync(string listId, DateTime? dueMin, DateTime? dueMax, CancellationToken ct)
    {
        var tasks = new List<GTask>();
        string? pageToken = null;
        do
        {
            var req = _tasks.Tasks.List(listId);
            req.ShowCompleted = false;
            req.ShowHidden = false;
            req.ShowDeleted = false;
            if (dueMin is { } min) req.DueMin = Rfc3339(min);
            if (dueMax is { } max) req.DueMax = Rfc3339(max);
            req.MaxResults = 100;
            req.PageToken = pageToken;
            var page = await req.ExecuteAsync(ct);
            tasks.AddRange((page.Items ?? new List<GTask>()).Where(t => t.Status != "completed" && t.Deleted != true));
            pageToken = page.NextPageToken;
        } while (pageToken is not null);
        return tasks;
    }

    /// <summary>Toutes les tâches non terminées (avec ou sans échéance), pour la page "Tâches".</summary>
    public Task<List<TaskEntry>> ListOpenTasksAsync(AppSettings settings, IReadOnlyDictionary<string, TimeOnly>? webTimes, CancellationToken ct) => Guard(async () =>
    {
        var result = new List<TaskEntry>();
        foreach (var list in await ListTaskListsAsync(ct))
        {
            foreach (var t in await ListTasksAsync(list.Id, null, null, ct))
            {
                var id = $"{AccountId}/task:{list.Id}:{t.Id}";
                TimeOnly? webTime = webTimes is not null && webTimes.TryGetValue(id, out var w) ? w : null;
                var resolved = TaskTimeResolver.Resolve(t.Due, t.Title, t.Notes, settings.DefaultTaskTime, null, webTime);
                result.Add(new TaskEntry(AccountId, list.Id, list.Title, t.Id, t.Title ?? "", t.Notes,
                    TaskTimeResolver.DueDate(t.Due),
                    resolved is { IsExplicit: true } r ? TimeOnly.FromDateTime(r.When.DateTime) : null,
                    t.WebViewLink));
            }
        }
        return result;
    });

    // ───────────────────────────── Écriture ─────────────────────────────

    /// <summary>Crée un évènement. <paramref name="end"/> est exclusif pour un évènement "journée entière".</summary>
    public Task<Event> CreateEventAsync(
        string calendarId, string title, DateTimeOffset start, DateTimeOffset end, bool allDay,
        string? location, string? description, int? reminderMinutes, CancellationToken ct) => Guard(async () =>
    {
        var ev = new Event
        {
            Summary = title,
            Location = string.IsNullOrWhiteSpace(location) ? null : location,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
        };
        if (allDay)
        {
            ev.Start = new EventDateTime { Date = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            ev.End = new EventDateTime { Date = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }
        else
        {
            ev.Start = new EventDateTime { DateTimeDateTimeOffset = start, TimeZone = IanaTimeZone() };
            ev.End = new EventDateTime { DateTimeDateTimeOffset = end, TimeZone = IanaTimeZone() };
        }
        if (reminderMinutes is { } m)
        {
            ev.Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = new List<EventReminder> { new() { Method = "popup", Minutes = m } },
            };
        }
        return await _calendar.Events.Insert(ev, calendarId).ExecuteAsync(ct);
    });

    public Task<bool> DeleteEventAsync(string calendarId, string eventId, CancellationToken ct) => Guard(async () =>
    {
        await _calendar.Events.Delete(calendarId, eventId).ExecuteAsync(ct);
        return true;
    });

    /// <summary>
    /// Crée une tâche. L'API ne stockant pas l'heure, une heure éventuelle est ajoutée aux notes
    /// sous la forme "⏰ HH:mm" (relue ensuite par l'app, et visible sur le téléphone).
    /// </summary>
    public Task<GTask> CreateTaskAsync(string listId, string title, string? notes, DateOnly? due, TimeOnly? time, CancellationToken ct) => Guard(async () =>
    {
        var task = new GTask
        {
            Title = title,
            Notes = ComposeNotes(notes, time),
            Due = due is { } d ? Rfc3339(d.ToDateTime(TimeOnly.MinValue)) : null,
        };
        return await _tasks.Tasks.Insert(task, listId).ExecuteAsync(ct);
    });

    public Task<bool> CompleteTaskAsync(string listId, string taskId, CancellationToken ct) => Guard(async () =>
    {
        await _tasks.Tasks.Patch(new GTask { Status = "completed" }, listId, taskId).ExecuteAsync(ct);
        return true;
    });

    public Task<bool> DeleteTaskAsync(string listId, string taskId, CancellationToken ct) => Guard(async () =>
    {
        await _tasks.Tasks.Delete(listId, taskId).ExecuteAsync(ct);
        return true;
    });

    public static string? ComposeNotes(string? notes, TimeOnly? time)
    {
        var text = notes?.Trim() ?? "";
        if (time is { } t)
            text = $"⏰ {t:HH\\:mm}" + (text.Length > 0 ? "\n" + text : "");
        return text.Length == 0 ? null : text;
    }

    private static string Rfc3339(DateTime utcMidnightOrDate) =>
        utcMidnightOrDate.ToString("yyyy-MM-dd'T'HH:mm:ss'.000Z'", CultureInfo.InvariantCulture);

    private static string? IanaTimeZone()
    {
        var local = TimeZoneInfo.Local;
        if (local.HasIanaId)
            return local.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : null;
    }

    public void Dispose()
    {
        _calendar.Dispose();
        _tasks.Dispose();
    }
}
