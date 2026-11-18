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
    private readonly Dictionary<ReminderKind, string> _colors = new();
    private ReminderKind? _colorTarget;

    internal SettingsPage(ReminderEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Load();
        _engine.StatusChanged += OnStatusChanged;
        Unloaded += (_, _) => _engine.StatusChanged -= OnStatusChanged;

        VersionText.Text = $"Agenda Reminder {Updater.Current}";
        BuildPalette();

        if (App.Updates is { } updates)
        {
            updates.Changed += OnUpdatesChanged;
            Unloaded += (_, _) => updates.Changed -= OnUpdatesChanged;
        }
        OnUpdatesChanged();
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

        LoadKind(s, ReminderKind.Event, EventNotifOn, EventNotif, EventFullOn, EventFull);
        LoadKind(s, ReminderKind.Task, TaskNotifOn, TaskNotif, TaskFullOn, TaskFull);
        LoadKind(s, ReminderKind.Birthday, BirthdayNotifOn, BirthdayNotif, BirthdayFullOn, BirthdayFull);
        GoogleStyle.SelectedValue = s.GoogleRemindersStyle;
        UseGoogleColors.IsChecked = s.UseGoogleCalendarColors;
        foreach (var kind in new[] { ReminderKind.Event, ReminderKind.Task, ReminderKind.Birthday })
            _colors[kind] = s.ColorFor(kind);
        UpdateSwatches();
        ColorPanel.Visibility = Visibility.Collapsed;

        AllDayTime.Text = s.AllDayReminderTime;
        BirthdayTime.Text = s.BirthdayReminderTime;
        TaskTime.Text = s.DefaultTaskTime;
        GraceMinutes.Text = s.MissedReminderGraceMinutes.ToString();

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
        AutoUpdate.IsChecked = s.CheckForUpdates;
        SavedText.Text = "";
    }

    private static void LoadKind(AppSettings s, ReminderKind kind, CheckBox notifOn, TextBox notif, CheckBox fullOn, TextBox full)
    {
        var r = s.RemindersFor(kind);
        var defaults = KindReminders.Defaults()[kind];
        // Case cochée seulement s'il y a au moins un délai ; sinon on propose un délai par défaut.
        notifOn.IsChecked = r.NotificationEnabled && r.NotificationMinutes.Count > 0;
        notif.Text = Ui.FormatIntList(r.NotificationMinutes.Count > 0 ? r.NotificationMinutes : defaults.NotificationMinutes.DefaultIfEmpty(10));
        fullOn.IsChecked = r.FullScreenEnabled && r.FullScreenMinutes.Count > 0;
        full.Text = Ui.FormatIntList(r.FullScreenMinutes.Count > 0 ? r.FullScreenMinutes : defaults.FullScreenMinutes.DefaultIfEmpty(0));
        notif.IsEnabled = notifOn.IsChecked == true;
        full.IsEnabled = fullOn.IsChecked == true;
    }

    private static KindReminders ReadKind(string name, CheckBox notifOn, TextBox notif, CheckBox fullOn, TextBox full, List<string> errors)
    {
        var r = new KindReminders
        {
            NotificationEnabled = notifOn.IsChecked == true,
            NotificationMinutes = IntList(notif, name + " (notification)", errors),
            FullScreenEnabled = fullOn.IsChecked == true,
            FullScreenMinutes = IntList(full, name + " (plein écran)", errors),
        };
        if (r.NotificationEnabled && r.NotificationMinutes.Count == 0)
            errors.Add($"{name} : indiquez le délai de la notification (ou décochez-la)");
        if (r.FullScreenEnabled && r.FullScreenMinutes.Count == 0)
            errors.Add($"{name} : indiquez le délai du plein écran (ou décochez-le)");
        if (r.NotificationEnabled && r.FullScreenEnabled)
        {
            var overlap = r.NotificationMinutes.Intersect(r.FullScreenMinutes).ToList();
            if (overlap.Count > 0)
                errors.Add($"{name} : la notification et le plein écran tombent au même moment ({string.Join(", ", overlap)} min). Choisissez des délais différents.");
        }
        return r;
    }

    /// <summary>Case décochée = champ des minutes grisé.</summary>
    private void OnFormToggled(object sender, RoutedEventArgs e)
    {
        foreach (var (box, text) in new[]
                 {
                     (EventNotifOn, EventNotif), (EventFullOn, EventFull),
                     (TaskNotifOn, TaskNotif), (TaskFullOn, TaskFull),
                     (BirthdayNotifOn, BirthdayNotif), (BirthdayFullOn, BirthdayFull),
                 })
        {
            if (box is not null && text is not null)
                text.IsEnabled = box.IsChecked == true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        var s = _draft;

        s.Reminders = new Dictionary<ReminderKind, KindReminders>
        {
            [ReminderKind.Event] = ReadKind("Évènements", EventNotifOn, EventNotif, EventFullOn, EventFull, errors),
            [ReminderKind.Task] = ReadKind("Tâches", TaskNotifOn, TaskNotif, TaskFullOn, TaskFull, errors),
            [ReminderKind.Birthday] = ReadKind("Anniversaires", BirthdayNotifOn, BirthdayNotif, BirthdayFullOn, BirthdayFull, errors),
        };
        s.GoogleRemindersStyle = (DisplayStyle)(GoogleStyle.SelectedValue ?? DisplayStyle.Toast);
        s.UseGoogleCalendarColors = UseGoogleColors.IsChecked == true;
        s.Colors = new Dictionary<ReminderKind, string>(_colors);
        s.AllDayReminderTime = Time(AllDayTime, "Heure « journée entière »", errors);
        s.BirthdayReminderTime = Time(BirthdayTime, "Heure des anniversaires", errors);
        s.DefaultTaskTime = Time(TaskTime, "Heure des tâches", errors);
        s.MissedReminderGraceMinutes = Int(GraceMinutes, "Rappel manqué", 0, 1440, errors);

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
        s.CheckForUpdates = AutoUpdate.IsChecked == true;

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

    // ───────── Couleurs ─────────

    private void BuildPalette()
    {
        foreach (var (name, hex) in Ui.Palette)
        {
            var swatch = new Button
            {
                Width = 30, Height = 30, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0),
                ToolTip = $"{name} ({hex})", Tag = hex, Cursor = System.Windows.Input.Cursors.Hand,
                Content = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Ui.BrushFromHex(hex) },
            };
            swatch.Click += (_, _) => SetColor(hex);
            PaletteHost.Children.Add(swatch);
        }
    }

    private void UpdateSwatches()
    {
        void Paint(Button b, ReminderKind k) => b.Content = new Border
        {
            Width = 40, Height = 16, CornerRadius = new CornerRadius(4), Background = Ui.BrushFromHex(_colors[k]),
        };
        Paint(EventColor, ReminderKind.Event);
        Paint(TaskColor, ReminderKind.Task);
        Paint(BirthdayColor, ReminderKind.Birthday);
    }

    private void OnPickColor(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<ReminderKind>(tag, out var kind)) return;
        _colorTarget = kind;
        ColorPanelTitle.Text = kind switch
        {
            ReminderKind.Task => "Couleur des tâches",
            ReminderKind.Birthday => "Couleur des anniversaires",
            _ => "Couleur des évènements",
        };
        HexBox.Text = _colors[kind];
        ColorPanel.Visibility = Visibility.Visible;
    }

    private void SetColor(string hex)
    {
        if (_colorTarget is not { } kind) return;
        _colors[kind] = hex.ToUpperInvariant();
        HexBox.Text = _colors[kind];
        UpdateSwatches();
        SavedText.Text = "Modifications non enregistrées";
    }

    private void OnApplyHex(object sender, RoutedEventArgs e)
    {
        var hex = HexBox.Text.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        if (!Ui.TryParseColor(hex, out _)) { Ui.Error(this, "Couleur invalide : utilisez le format #RRGGBB (ex. #4C8BF5)."); return; }
        SetColor(hex);
    }

    private void OnCloseColor(object sender, RoutedEventArgs e) => ColorPanel.Visibility = Visibility.Collapsed;

    // ───────── Mises à jour ─────────

    private void OnUpdatesChanged() => Dispatcher.BeginInvoke(() =>
    {
        var updates = App.Updates;
        UpdateStatus.Text = updates is null ? "" : updates.Status;
        InstallUpdateButton.Visibility = updates?.Available is null ? Visibility.Collapsed : Visibility.Visible;
        InstallUpdateButton.Content = updates?.Available is { } a
            ? (Updater.IsInstalled ? $"Installer la version {a.Version}" : $"Télécharger la version {a.Version}")
            : "";
    });

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (App.Updates is not { } updates) return;
        await updates.CheckAsync();
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (App.Updates is not { } updates) return;
        await Ui.RunAsync((FrameworkElement)sender, () => updates.InstallAsync());
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
