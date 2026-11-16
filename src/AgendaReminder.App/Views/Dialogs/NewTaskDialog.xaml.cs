using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AgendaReminder.App.Services;
using AgendaReminder.Core.Accounts;
using AgendaReminder.Core.Google;

namespace AgendaReminder.App.Views.Dialogs;

public partial class NewTaskDialog : Window
{
    private readonly ReminderEngine _engine;

    private NewTaskDialog(ReminderEngine engine)
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

        DateBox.SelectedDate = DateTime.Today;
        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>Ouvre la boîte de dialogue ; renvoie vrai si une tâche a été créée.</summary>
    internal static bool Open(DependencyObject owner, ReminderEngine engine)
    {
        if (engine.Accounts.Accounts.Count == 0)
        {
            Ui.Error(owner, "Connectez d'abord un compte Google (page « Comptes »).");
            return false;
        }
        var dialog = new NewTaskDialog(engine) { Owner = Window.GetWindow(owner) };
        return dialog.ShowDialog() == true;
    }

    private async void OnAccountChanged(object sender, SelectionChangedEventArgs e) => await LoadListsAsync();

    private async Task LoadListsAsync()
    {
        TaskListBox.ItemsSource = null;
        if (AccountBox.SelectedItem is not AccountInfo account) return;
        try
        {
            var client = await _engine.Accounts.GetClientAsync(account.Id);
            var lists = await client.ListTaskListsAsync(default);
            TaskListBox.ItemsSource = lists;
            TaskListBox.DisplayMemberPath = nameof(TaskListRef.Title);
            TaskListBox.SelectedItem = lists.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ShowError("Impossible de lire les listes de tâches : " + ex.Message);
        }
    }

    private void OnHasDueChanged(object sender, RoutedEventArgs e)
    {
        var hasDue = HasDueBox.IsChecked == true;
        DateBox.IsEnabled = hasDue;
        TimeBox.IsEnabled = hasDue;
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var title = TitleBox.Text.Trim();
        if (title.Length == 0) { ShowError("Donnez un titre à la tâche."); return; }
        if (AccountBox.SelectedItem is not AccountInfo account) { ShowError("Choisissez un compte."); return; }
        if (TaskListBox.SelectedItem is not TaskListRef list) { ShowError("Choisissez une liste."); return; }

        DateOnly? due = null;
        TimeOnly? time = null;
        if (HasDueBox.IsChecked == true)
        {
            if (DateBox.SelectedDate is not { } date) { ShowError("Choisissez une date d'échéance."); return; }
            due = DateOnly.FromDateTime(date);
            if (!string.IsNullOrWhiteSpace(TimeBox.Text))
            {
                if (!Ui.TryParseTime(TimeBox.Text, out var t)) { ShowError("Heure invalide (ex. 14:30)."); return; }
                time = t;
            }
        }

        var ok = await Ui.RunAsync(CreateButton, async () =>
        {
            var client = await _engine.Accounts.GetClientAsync(account.Id);
            await client.CreateTaskAsync(list.Id, title, NotesBox.Text, due, time, default);
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
