using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AgendaReminder.App.Services;
using AgendaReminder.Core.Accounts;
using AgendaReminder.Core.Google;
using AgendaReminder.Core.Scheduling;

namespace AgendaReminder.App.Views.Dialogs;

public partial class NewEventDialog : Window
{
    private readonly ReminderEngine _engine;

    private NewEventDialog(ReminderEngine engine)
    {
        _engine = engine;
        InitializeComponent();

        var accounts = engine.Accounts.Accounts.ToList();
        AccountBox.ItemsSource = accounts;
        AccountBox.DisplayMemberPath = nameof(AccountInfo.Email);
        AccountBox.SelectedItem = engine.Accounts.DefaultForCreation;
        if (accounts.Count <= 1)
        {
            AccountLabel.Visibility = Visibility.Collapsed;
            AccountBox.Visibility = Visibility.Collapsed;
        }

        // Par défaut : prochaine heure pleine, durée 1 h.
        var start = DateTime.Now.AddHours(1);
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0);
        DateBox.SelectedDate = start.Date;
        StartBox.Text = start.ToString("HH:mm");
        EndBox.Text = start.AddHours(1).ToString("HH:mm");

        ReminderBox.ItemsSource = Choices.EventReminders;
        ReminderBox.SelectedIndex = 0;

        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>Ouvre la boîte de dialogue ; renvoie vrai si un évènement a été créé.</summary>
    internal static bool Open(DependencyObject owner, ReminderEngine engine)
    {
        if (engine.Accounts.Accounts.Count == 0)
        {
            Ui.Error(owner, "Connectez d'abord un compte Google (page « Comptes »).");
            return false;
        }
        var dialog = new NewEventDialog(engine) { Owner = Window.GetWindow(owner) };
        return dialog.ShowDialog() == true;
    }

    private async void OnAccountChanged(object sender, SelectionChangedEventArgs e) => await LoadCalendarsAsync();

    private async Task LoadCalendarsAsync()
    {
        CalendarBox.ItemsSource = null;
        if (AccountBox.SelectedItem is not AccountInfo account) return;
        try
        {
            var client = await _engine.Accounts.GetClientAsync(account.Id);
            var calendars = (await client.ListCalendarsAsync(default)).Where(c => c.Writable).ToList();
            CalendarBox.ItemsSource = calendars;
            CalendarBox.DisplayMemberPath = nameof(CalendarRef.Name);
            CalendarBox.SelectedItem = calendars.FirstOrDefault(c => c.Primary) ?? calendars.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ShowError("Impossible de lire les agendas : " + ex.Message);
        }
    }

    private void OnAllDayChanged(object sender, RoutedEventArgs e) =>
        TimePanel.IsEnabled = AllDayBox.IsChecked != true;

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var title = TitleBox.Text.Trim();
        if (title.Length == 0) { ShowError("Donnez un titre à l'évènement."); return; }
        if (AccountBox.SelectedItem is not AccountInfo account) { ShowError("Choisissez un compte."); return; }
        if (CalendarBox.SelectedItem is not CalendarRef calendar) { ShowError("Choisissez un agenda."); return; }
        if (DateBox.SelectedDate is not { } date) { ShowError("Choisissez une date."); return; }

        var day = DateOnly.FromDateTime(date);
        var allDay = AllDayBox.IsChecked == true;
        DateTimeOffset start, end;
        var zone = TimeZoneInfo.Local;
        if (allDay)
        {
            start = TaskTimeResolver.AtLocal(day, TimeOnly.MinValue, zone);
            end = TaskTimeResolver.AtLocal(day.AddDays(1), TimeOnly.MinValue, zone);
        }
        else
        {
            if (!Ui.TryParseTime(StartBox.Text, out var s)) { ShowError("Heure de début invalide (ex. 14:30)."); return; }
            if (!Ui.TryParseTime(EndBox.Text, out var en)) { ShowError("Heure de fin invalide (ex. 15:30)."); return; }
            start = TaskTimeResolver.AtLocal(day, s, zone);
            end = TaskTimeResolver.AtLocal(en <= s ? day.AddDays(1) : day, en, zone);
        }

        var reminder = (ReminderBox.SelectedItem as Choice<int?>)?.Value;
        var ok = await Ui.RunAsync(CreateButton, async () =>
        {
            var client = await _engine.Accounts.GetClientAsync(account.Id);
            await client.CreateEventAsync(calendar.Id, title, start, end, allDay,
                LocationBox.Text, DescriptionBox.Text, reminder, default);
        });
        if (!ok) return;

        _ = _engine.SyncAsync();
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
