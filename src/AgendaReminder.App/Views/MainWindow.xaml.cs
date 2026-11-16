using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AgendaReminder.App.Services;
using AgendaReminder.App.Views.Pages;

namespace AgendaReminder.App.Views;

/// <summary>Pages de la fenêtre principale : rafraîchies quand les données changent.</summary>
internal interface IPage
{
    void Refresh();
}

/// <summary>
/// Fenêtre principale. Créée à la demande et entièrement libérée à la fermeture :
/// l'app ne garde en mémoire que l'icône de notification et le moteur de rappels.
/// </summary>
public partial class MainWindow : Window
{
    private sealed record AccountChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ReminderEngine _engine;
    private bool _updatingSwitcher;

    internal MainWindow(ReminderEngine engine, string? page)
    {
        _engine = engine;
        InitializeComponent();

        _engine.StatusChanged += OnStatusChanged;
        _engine.ItemsChanged += OnItemsChanged;
        _engine.Accounts.Changed += OnAccountsChanged;
        Closed += (_, _) =>
        {
            _engine.StatusChanged -= OnStatusChanged;
            _engine.ItemsChanged -= OnItemsChanged;
            _engine.Accounts.Changed -= OnAccountsChanged;
            PageHost.Content = null;
        };

        FillAccountSwitcher();
        UpdateStatus();
        Navigate(page ?? "upcoming");
    }

    internal ReminderEngine Engine => _engine;

    public void Navigate(string page)
    {
        var item = Nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == page);
        if (item is not null && !ReferenceEquals(Nav.SelectedItem, item))
            Nav.SelectedItem = item; // déclenche OnNavigate
        else if (item is not null)
            ShowPage(page);
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is ListBoxItem { Tag: string page })
            ShowPage(page);
    }

    private void ShowPage(string page)
    {
        PageHost.Content = page switch
        {
            "tasks" => new TasksPage(_engine),
            "accounts" => new AccountsPage(_engine),
            "settings" => new SettingsPage(_engine),
            _ => new UpcomingPage(_engine),
        };
    }

    private void FillAccountSwitcher()
    {
        _updatingSwitcher = true;
        var accounts = _engine.Accounts;
        var choices = new[] { new AccountChoice(null, "Tous les comptes") }
            .Concat(accounts.Accounts.Select(a => new AccountChoice(a.Id, a.Email)))
            .ToList();
        AccountSwitcher.ItemsSource = choices;
        AccountSwitcher.SelectedItem = choices.FirstOrDefault(c => c.Id == accounts.SelectedId) ?? choices[0];
        AccountSwitcher.IsEnabled = accounts.Accounts.Count > 1;
        _updatingSwitcher = false;
    }

    private void OnAccountSwitched(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSwitcher || AccountSwitcher.SelectedItem is not AccountChoice choice)
            return;
        _engine.Accounts.SelectedId = choice.Id; // déclenche OnAccountsChanged -> rafraîchit la page
    }

    private void OnAccountsChanged() => Dispatcher.BeginInvoke(() =>
    {
        FillAccountSwitcher();
        (PageHost.Content as IPage)?.Refresh();
    });

    private void OnItemsChanged() => Dispatcher.BeginInvoke(() => (PageHost.Content as IPage)?.Refresh());

    private void OnStatusChanged() => Dispatcher.BeginInvoke(UpdateStatus);

    private void UpdateStatus()
    {
        SyncButton.IsEnabled = !_engine.IsSyncing;
        StatusText.Text = _engine.IsSyncing
            ? "Synchronisation…"
            : _engine.LastError is { } err
                ? "⚠ " + err
                : _engine.LastSync is { } t ? $"Synchronisé à {t:HH:mm}" : "";
    }

    private async void OnSync(object sender, RoutedEventArgs e) => await _engine.SyncAsync();
}
