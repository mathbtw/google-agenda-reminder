using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AgendaReminder.App.Interop;
using AgendaReminder.App.Views;
using AgendaReminder.Core;
using AgendaReminder.Core.Google;
using AgendaReminder.Core.Models;
using AgendaReminder.Core.Policy;
using AgendaReminder.Core.Scheduling;
using AgendaReminder.Core.Settings;
using AgendaReminder.Core.Storage;
using Microsoft.Win32;

namespace AgendaReminder.App.Services;

/// <summary>
/// Cœur de l'app : synchronise Google périodiquement, vérifie toutes les 15 s les rappels dus,
/// applique la blacklist et affiche toast / plein écran.
/// </summary>
internal sealed class ReminderEngine : IDisposable
{
    private readonly GoogleSyncService _google = new();
    private readonly ToastManager _toasts = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _syncTimer = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly FileSystemWatcher? _settingsWatcher;
    private readonly DispatcherTimer _settingsDebounce = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>Rappels reportés car une app blacklistée est au premier plan (clé -> rappel, début du report).</summary>
    private readonly Dictionary<string, (DueReminder Reminder, DateTimeOffset Since)> _postponed = new();

    private List<ReminderItem> _items;
    private readonly ReminderState _state;
    private FullScreenReminderWindow? _fullScreen;

    public AppSettings Settings { get; private set; }

    public DateTimeOffset? LastSync { get; private set; }

    public string? LastError { get; private set; }

    public event Action? StatusChanged;

    public ReminderEngine()
    {
        Settings = JsonFileStore.LoadOrCreate(AppPaths.Settings, () => new AppSettings());
        _state = JsonFileStore.LoadOrCreate(AppPaths.State, () => new ReminderState(), writeIfMissing: false);
        // Cache local : les rappels fonctionnent même sans réseau.
        _items = JsonFileStore.LoadOrCreate(AppPaths.Cache, () => new List<ReminderItem>(), writeIfMissing: false);

        _tick.Tick += (_, _) => CheckDue();
        _syncTimer.Tick += async (_, _) => await SyncAsync();

        try
        {
            _settingsWatcher = new FileSystemWatcher(AppPaths.Root, Path.GetFileName(AppPaths.Settings))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _settingsWatcher.Changed += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
            {
                _settingsDebounce.Stop();
                _settingsDebounce.Start();
            });
            _settingsDebounce.Tick += async (_, _) =>
            {
                _settingsDebounce.Stop();
                await ReloadSettingsAsync();
            };
        }
        catch (Exception ex)
        {
            Log.Error("Surveillance de settings.json impossible", ex);
        }

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Start()
    {
        ApplyIntervals();
        _tick.Start();
        _syncTimer.Start();
        CheckDue();
        _ = SyncAsync();
    }

    private void ApplyIntervals() =>
        _syncTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, Settings.SyncIntervalMinutes));

    public async Task ReloadSettingsAsync()
    {
        try
        {
            Settings = JsonFileStore.LoadOrCreate(AppPaths.Settings, () => new AppSettings());
            ApplyIntervals();
            Log.Info("Paramètres rechargés");
            await SyncAsync(); // les déclencheurs dépendent des paramètres
        }
        catch (Exception ex)
        {
            LastError = "settings.json invalide : " + ex.Message;
            Log.Error("Rechargement des paramètres", ex);
            StatusChanged?.Invoke();
        }
    }

    public async Task SyncAsync()
    {
        if (!await _syncGate.WaitAsync(0))
            return; // une synchro est déjà en cours
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            if (!_google.IsConnected)
                await _google.ConnectAsync(cts.Token);

            var items = await _google.FetchAsync(Settings, DateTimeOffset.Now, cts.Token);
            _items = items;
            JsonFileStore.Save(AppPaths.Cache, _items);
            LastSync = DateTimeOffset.Now;
            LastError = null;
            Log.Info($"Synchro OK : {items.Count} éléments");
            CheckDue();
        }
        catch (Exception ex)
        {
            LastError = ex is FileNotFoundException ? "client_secret.json manquant" : ex.Message;
            Log.Error("Synchro Google", ex);
        }
        finally
        {
            _syncGate.Release();
            StatusChanged?.Invoke();
        }
    }

    public void SignOut()
    {
        _google.SignOut();
        LastSync = null;
        LastError = "Déconnecté";
        StatusChanged?.Invoke();
    }

    public IEnumerable<(ReminderItem Item, DateTimeOffset FireAt)> Upcoming() =>
        ReminderScheduler.Upcoming(_items, _state, DateTimeOffset.Now);

    private void CheckDue()
    {
        var now = DateTimeOffset.Now;
        var due = ReminderScheduler.GetDue(_items, _state, now, TimeSpan.FromMinutes(Settings.MissedReminderGraceMinutes));

        // Les rappels déjà reportés restent candidats même au-delà du délai de grâce,
        // sauf si l'élément a disparu de Google entre-temps (tâche terminée, évènement supprimé…).
        foreach (var (key, (reminder, _)) in _postponed.ToList())
        {
            var stillExists = reminder.IsSnooze || _items.Any(i => i.Id == reminder.Item.Id);
            if (!stillExists)
                _postponed.Remove(key);
            else if (due.All(d => d.Key != key))
                due.Add(reminder);
        }

        if (due.Count == 0)
            return;

        var foreground = ForegroundAppDetector.Get();
        var fullScreen = new List<DueReminder>();
        var changed = false;

        foreach (var reminder in due.OrderBy(d => d.FireAt))
        {
            var style = Settings.Display.TryGetValue(reminder.Item.Kind, out var s) ? s : DisplayStyle.Toast;
            var postponedFor = _postponed.TryGetValue(reminder.Key, out var p) ? now - p.Since : TimeSpan.Zero;
            var decision = DisplayPolicy.Decide(style, foreground, Settings, postponedFor);

            if (decision == DisplayDecision.Postpone)
            {
                if (!_postponed.ContainsKey(reminder.Key))
                {
                    _postponed[reminder.Key] = (reminder, now);
                    Log.Info($"Reporté ({foreground.ProcessName ?? "plein écran"}) : {reminder.Item.Title}");
                }
                continue;
            }

            _postponed.Remove(reminder.Key);
            ReminderScheduler.MarkHandled(_state, reminder);
            changed = true;

            switch (decision)
            {
                case DisplayDecision.FullScreen:
                    fullScreen.Add(reminder);
                    break;
                case DisplayDecision.Toast:
                    ShowToast(reminder.Item);
                    break;
                case DisplayDecision.Drop:
                    Log.Info($"Ignoré ({foreground.ProcessName}) : {reminder.Item.Title}");
                    break;
            }
        }

        if (fullScreen.Count > 0)
            ShowFullScreen(fullScreen.Select(r => r.Item));

        if (changed)
        {
            ReminderScheduler.Prune(_state, now, TimeSpan.FromDays(3));
            SaveState();
        }
    }

    public void ShowToast(ReminderItem item)
    {
        if (Settings.PlaySound)
            System.Media.SystemSounds.Asterisk.Play();
        _toasts.Show(new ReminderView { Item = item }, Settings.SnoozeOptionsMinutes, Settings.ToastDurationSeconds,
            minutes => Snooze(item, minutes));
    }

    public void ShowFullScreen(IEnumerable<ReminderItem> items)
    {
        var views = items.Select(i => new ReminderView { Item = i }).ToList();
        if (_fullScreen is null)
        {
            if (Settings.PlaySound)
                System.Media.SystemSounds.Exclamation.Play();
            _fullScreen = new FullScreenReminderWindow(Settings.SnoozeOptionsMinutes);
            _fullScreen.Snoozed += (shown, minutes) =>
            {
                foreach (var v in shown) Snooze(v.Item, minutes);
            };
            _fullScreen.Closed += (_, _) => _fullScreen = null;
            _fullScreen.Add(views);
            _fullScreen.Show();
        }
        else
        {
            _fullScreen.Add(views);
            _fullScreen.BringToFront();
        }
    }

    private void Snooze(ReminderItem item, int minutes)
    {
        ReminderScheduler.AddSnooze(_state, item, DateTimeOffset.Now.AddMinutes(minutes));
        SaveState();
        StatusChanged?.Invoke();
    }

    private void SaveState()
    {
        try
        {
            JsonFileStore.Save(AppPaths.State, _state);
        }
        catch (Exception ex)
        {
            Log.Error("Sauvegarde de l'état", ex);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Application.Current.Dispatcher.BeginInvoke(async () =>
            {
                CheckDue();
                await Task.Delay(TimeSpan.FromSeconds(10)); // laisser le réseau revenir
                await SyncAsync();
            });
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _tick.Stop();
        _syncTimer.Stop();
        _settingsWatcher?.Dispose();
        _google.Dispose();
    }
}
