using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgendaReminder.App.Services;
using AgendaReminder.Core.Google;
using Microsoft.Win32;

namespace AgendaReminder.App.Views.Pages;

public sealed record AccountRow(string Id, string Email, bool Enabled, string Status, Brush StatusBrush);

public partial class AccountsPage : UserControl, IPage
{
    private const string GuideUrl = "https://github.com/mathbtw/google-agenda-windows#1-cr%C3%A9er-les-identifiants-google-une-seule-fois-5-min";

    private readonly ReminderEngine _engine;
    private CancellationTokenSource? _adding;

    internal AccountsPage(ReminderEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Refresh();
        Unloaded += (_, _) => _adding?.Cancel();
    }

    public void Refresh()
    {
        var hasSecret = GoogleAccountClient.HasClientSecret;
        SecretStatus.Text = hasSecret
            ? "✔ Identifiants importés. Vous pouvez ajouter vos comptes ci-dessous."
            : "À faire une seule fois : créez un client OAuth « Application de bureau » dans Google Cloud, téléchargez le JSON puis importez-le ici.";
        ImportButton.Content = hasSecret ? "Remplacer…" : "Importer client_secret.json…";
        AddButton.IsEnabled = hasSecret && _adding is null;

        var accounts = _engine.Accounts;
        var ok = Ui.Frozen(0x34, 0xC3, 0x8F);
        var warn = Ui.Frozen(0xE0, 0x6C, 0x4C);
        AccountList.ItemsSource = accounts.Accounts
            .Select(a => accounts.ErrorFor(a.Id) is { } err
                ? new AccountRow(a.Id, a.Email, a.Enabled, "⚠ " + err, warn)
                : new AccountRow(a.Id, a.Email, a.Enabled, a.Enabled ? "Connecté" : "Connecté · rappels désactivés", ok))
            .ToList();
        NoAccount.Visibility = accounts.Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGuide(object sender, RoutedEventArgs e) => Ui.OpenUrl(GuideUrl);

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir le fichier JSON téléchargé depuis Google Cloud",
            Filter = "Identifiants OAuth (*.json)|*.json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            AccountManager.ImportClientSecret(dialog.FileName);
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex.Message);
            return;
        }
        Refresh();
        if (_engine.Accounts.Accounts.Count == 0 && Ui.Confirm(this, "Identifiants importés ✔\n\nConnecter un compte Google maintenant ?"))
            await AddAccountAsync();
    }

    private async void OnAdd(object sender, RoutedEventArgs e) => await AddAccountAsync();

    private async System.Threading.Tasks.Task AddAccountAsync()
    {
        _adding = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        PendingPanel.Visibility = Visibility.Visible;
        AddButton.IsEnabled = false;
        try
        {
            var account = await _engine.Accounts.AddAsync(_adding.Token);
            Log.Info("Compte ajouté : " + account.Email);
            _ = _engine.SyncAsync();
        }
        catch (OperationCanceledException)
        {
            // annulé par l'utilisateur ou délai dépassé
        }
        catch (Exception ex)
        {
            Log.Error("Ajout de compte", ex);
            Ui.Error(this, "La connexion a échoué :\n" + ex.Message);
        }
        finally
        {
            _adding.Dispose();
            _adding = null;
            PendingPanel.Visibility = Visibility.Collapsed;
            Refresh();
        }
    }

    private void OnCancelAdd(object sender, RoutedEventArgs e) => _adding?.Cancel();

    private static AccountRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as AccountRow;

    private void OnToggleEnabled(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row || sender is not CheckBox box) return;
        _engine.Accounts.SetEnabled(row.Id, box.IsChecked == true);
        _ = _engine.SyncAsync();
    }

    private async void OnReconnect(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        _adding = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        PendingPanel.Visibility = Visibility.Visible;
        try
        {
            await _engine.Accounts.ReconnectAsync(row.Id, _adding.Token);
            _ = _engine.SyncAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Reconnexion", ex);
            Ui.Error(this, "La reconnexion a échoué :\n" + ex.Message);
        }
        finally
        {
            _adding.Dispose();
            _adding = null;
            PendingPanel.Visibility = Visibility.Collapsed;
            Refresh();
        }
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (!Ui.Confirm(this, $"Déconnecter {row.Email} ?\n\nL'accès de l'app sera révoqué chez Google. Vos données Google ne sont pas touchées."))
            return;
        await Ui.RunAsync((FrameworkElement)sender, () => _engine.Accounts.RemoveAsync(row.Id, default));
        _ = _engine.SyncAsync();
    }
}
