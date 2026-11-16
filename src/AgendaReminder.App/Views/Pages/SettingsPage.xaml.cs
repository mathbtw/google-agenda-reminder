using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AgendaReminder.App.Services;
using AgendaReminder.Core;
using AgendaReminder.Core.Models;
using AgendaReminder.Core.Policy;
using AgendaReminder.Core.Settings;
using AgendaReminder.Core.Storage;

namespace AgendaReminder.App.Views.Pages;

public partial class SettingsPage : UserControl, IPage
{
    private readonly ReminderEngine _engine;
    private AppSettings _draft = new();
    private ObservableCollection<BlacklistEntry> _blacklist = new();

    internal SettingsPage(ReminderEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Load();
        _engine.StatusChanged += OnStatusChanged;
        Unloaded += (_, _) => _engine.StatusChanged -= OnStatusChanged;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Agenda Reminder {version?.ToString(3)}";
    }

    /// <summary>Pas de rechargement automatique : on ne veut pas écraser une saisie en cours.</summary>
    public void Refresh() { }

    private void OnStatusChanged() => Dispatcher.BeginInvoke(() => WebStatus.Text = "État : " + _engine.WebStatus);

    private void Load()
    {
        // Copie de travail : rien n'est appliqué avant « Enregistrer ».
        _draft = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(_engine.Settings, JsonFileStore.Options), JsonFileStore.Options) ?? new AppSettings();
        var s = _draft;

        EventMinutes.Text = Ui.FormatIntList(s.DefaultEventReminderMinutes);
        RemindAtStart.IsChecked = s.AlsoRemindAtEventStart;
        AllDayTime.Text = s.AllDayReminderTime;
        BirthdayTime.Text = s.BirthdayReminderTime;
        TaskTime.Text = s.DefaultTaskTime;
        TaskMinutes.Text = Ui.FormatIntList(s.TaskReminderMinutes);
        GraceMinutes.Text = s.MissedReminderGraceMinutes.ToString();

        EventStyle.SelectedValue = StyleOf(s, ReminderKind.Event);
        TaskStyle.SelectedValue = StyleOf(s, ReminderKind.Task);
        BirthdayStyle.SelectedValue = StyleOf(s, ReminderKind.Birthday);
        ToastSeconds.Text = s.ToastDurationSeconds.ToString();
        SnoozeOptions.Text = Ui.FormatIntList(s.SnoozeOptionsMinutes);
        PlaySound.IsChecked = s.PlaySound;

        _blacklist = new ObservableCollection<BlacklistEntry>(s.Blacklist);
        BlacklistItems.ItemsSource = _blacklist;
        AutoDetect.IsChecked = s.AutoDetectFullscreenApps;
        AutoDetectAction.SelectedValue = s.AutoDetectAction;
        MaxPostpone.Text = s.MaxPostponeMinutes.ToString();

        SyncMinutes.Text = s.SyncIntervalMinutes.ToString();
        LookAhead.Text = s.LookAheadDays.ToString();

        WebEnabled.IsChecked = s.EnableWebTaskTimes;
        WebMinutes.Text = s.WebTaskTimesRefreshMinutes.ToString();
        WebStatus.Text = "État : " + _engine.WebStatus;

        StartWithWindows.IsChecked = StartupRegistration.IsEnabled;
        SavedText.Text = "";
    }

    private static DisplayStyle StyleOf(AppSettings s, ReminderKind kind) =>
        s.Display.TryGetValue(kind, out var style) ? style : DisplayStyle.Toast;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        var s = _draft;

        s.DefaultEventReminderMinutes = IntList(EventMinutes, "Rappels des évènements", errors);
        s.AlsoRemindAtEventStart = RemindAtStart.IsChecked == true;
        s.AllDayReminderTime = Time(AllDayTime, "Heure « journée entière »", errors);
        s.BirthdayReminderTime = Time(BirthdayTime, "Heure des anniversaires", errors);
        s.DefaultTaskTime = Time(TaskTime, "Heure des tâches", errors);
        s.TaskReminderMinutes = IntList(TaskMinutes, "Rappels des tâches", errors);
        s.MissedReminderGraceMinutes = Int(GraceMinutes, "Rappel manqué", 0, 1440, errors);

        s.Display[ReminderKind.Event] = (DisplayStyle)(EventStyle.SelectedValue ?? DisplayStyle.FullScreen);
        s.Display[ReminderKind.Task] = (DisplayStyle)(TaskStyle.SelectedValue ?? DisplayStyle.FullScreen);
        s.Display[ReminderKind.Birthday] = (DisplayStyle)(BirthdayStyle.SelectedValue ?? DisplayStyle.Toast);
        s.ToastDurationSeconds = Int(ToastSeconds, "Durée des notifications", 0, 3600, errors);
        s.SnoozeOptionsMinutes = IntList(SnoozeOptions, "Boutons « Rappeler dans »", errors);
        s.PlaySound = PlaySound.IsChecked == true;

        s.Blacklist = _blacklist.Where(b => !string.IsNullOrWhiteSpace(b.Process))
            .GroupBy(b => DisplayPolicy.Normalize(b.Process)).Select(g => g.First()).ToList();
        s.AutoDetectFullscreenApps = AutoDetect.IsChecked == true;
        s.AutoDetectAction = (BlacklistAction)(AutoDetectAction.SelectedValue ?? BlacklistAction.Postpone);
        s.MaxPostponeMinutes = Int(MaxPostpone, "Délai maximal de report", 1, 1440, errors);

        s.SyncIntervalMinutes = Int(SyncMinutes, "Intervalle de synchro", 1, 120, errors);
        s.LookAheadDays = Int(LookAhead, "Jours à l'avance", 1, 90, errors);

        s.EnableWebTaskTimes = WebEnabled.IsChecked == true;
        s.WebTaskTimesRefreshMinutes = Int(WebMinutes, "Lecture du module web", 5, 1440, errors);

        if (errors.Count > 0)
        {
            Ui.Error(this, "Certaines valeurs sont invalides :\n\n• " + string.Join("\n• ", errors));
            return;
        }

        try
        {
            _engine.SaveSettings(s);
        }
        catch (Exception ex)
        {
            Ui.Error(this, "Enregistrement impossible : " + ex.Message);
            return;
        }
        Load();
        SavedText.Text = $"Enregistré à {DateTime.Now:HH:mm}";
    }

    private void OnRevert(object sender, RoutedEventArgs e) => Load();

    private static List<int> IntList(TextBox box, string name, List<string> errors)
    {
        var list = Ui.ParseIntList(box.Text);
        if (list is null) { errors.Add($"{name} : nombres séparés par des virgules (ex. 10, 30)"); return new(); }
        return list.Distinct().OrderBy(x => x).ToList();
    }

    private static int Int(TextBox box, string name, int min, int max, List<string> errors)
    {
        if (int.TryParse(box.Text.Trim(), out var v) && v >= min && v <= max) return v;
        errors.Add($"{name} : nombre entre {min} et {max}");
        return min;
    }

    private static string Time(TextBox box, string name, List<string> errors)
    {
        if (Ui.TryParseTime(box.Text, out var t)) return t.ToString("HH\\:mm");
        errors.Add($"{name} : heure invalide (ex. 09:00)");
        return "09:00";
    }

    // ───────── Blacklist ─────────

    private void OnProcessPickerOpened(object? sender, EventArgs e)
    {
        // Applications qui ont une fenêtre visible, triées par nom.
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId)
                    names.Add(p.ProcessName + ".exe");
            }
            catch { /* processus protégé */ }
            finally { p.Dispose(); }
        }
        ProcessPicker.ItemsSource = names.ToList();
    }

    private void OnAddBlacklist(object sender, RoutedEventArgs e)
    {
        var name = ProcessPicker.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (!name.Contains('.')) name += ".exe";
        if (_blacklist.Any(b => DisplayPolicy.Normalize(b.Process) == DisplayPolicy.Normalize(name))) return;
        _blacklist.Add(new BlacklistEntry { Process = name, Action = BlacklistAction.Postpone });
        ProcessPicker.Text = "";
    }

    private void OnRemoveBlacklist(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BlacklistEntry entry)
            _blacklist.Remove(entry);
    }

    // ───────── Tests ─────────

    private void OnTestToast(object sender, RoutedEventArgs e) => _engine.ShowToast(SampleItem(ReminderKind.Event));

    private void OnTestFullScreen(object sender, RoutedEventArgs e) => _engine.ShowFullScreen(new[] { SampleItem(ReminderKind.Task) });

    private static ReminderItem SampleItem(ReminderKind kind) => new()
    {
        Id = "test:" + Guid.NewGuid(),
        Kind = kind,
        Title = kind == ReminderKind.Task ? "Envoyer le dossier à Julie" : "Réunion d'équipe",
        Start = DateTimeOffset.Now.AddMinutes(10),
        End = kind == ReminderKind.Event ? DateTimeOffset.Now.AddMinutes(40) : null,
        Location = kind == ReminderKind.Event ? "Salle 3" : null,
        Source = "Test",
    };

    // ───────── Module web ─────────

    private async void OnWebLogin(object sender, RoutedEventArgs e) => await _engine.LoginWebAsync();

    private async void OnWebRead(object sender, RoutedEventArgs e)
    {
        if (sender is Button b) b.IsEnabled = false;
        WebStatus.Text = "État : lecture en cours…";
        try { WebStatus.Text = "État : " + await _engine.RefreshWebTaskTimesAsync(); }
        finally { if (sender is Button b2) b2.IsEnabled = true; }
    }

    // ───────── Système ─────────

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        try { StartupRegistration.Set(StartWithWindows.IsChecked == true); }
        catch (Exception ex) { Ui.Error(this, ex.Message); }
    }

    private void OnOpenData(object sender, RoutedEventArgs e) => Ui.OpenUrl(AppPaths.Root);

    private void OnOpenJson(object sender, RoutedEventArgs e) => Ui.OpenUrl(AppPaths.Settings);
}
