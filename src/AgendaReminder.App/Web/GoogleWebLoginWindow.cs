using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AgendaReminder.App.Services;
using Microsoft.Web.WebView2.Wpf;

namespace AgendaReminder.App.Web;

/// <summary>
/// Fenêtre de connexion au site Google Agenda pour le module web. La session (cookies) est conservée
/// dans le profil WebView2 de l'app (%APPDATA%\AgendaReminder\webview).
/// </summary>
internal sealed class GoogleWebLoginWindow : Window
{
    private const string LoginUrl =
        "https://accounts.google.com/ServiceLogin?service=cl&continue=https%3A%2F%2Fcalendar.google.com%2Fcalendar%2Fr";

    private readonly WebView2 _web = new();

    /// <summary>Vrai si l'utilisateur a atteint Google Agenda (donc est connecté).</summary>
    public bool Succeeded { get; private set; }

    public GoogleWebLoginWindow()
    {
        Title = "Agenda Reminder — connexion au site Google Agenda (module heures des tâches)";
        Width = 520;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = _web;
        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => _web.Dispose();
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await WebViewHost.GetEnvironmentAsync();
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.SourceChanged += (_, _) => CheckDone();
            _web.CoreWebView2.NavigationCompleted += (_, _) => CheckDone();
            _web.CoreWebView2.Navigate(LoginUrl);
        }
        catch (Exception ex)
        {
            Log.Error("Module web : fenêtre de connexion", ex);
            MessageBox.Show(this,
                "Impossible d'ouvrir le navigateur intégré (runtime Microsoft Edge WebView2).\n\n" + ex.Message,
                "Agenda Reminder", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void CheckDone()
    {
        if (Succeeded)
            return;
        var source = Uri.TryCreate(_web.CoreWebView2?.Source, UriKind.Absolute, out var u) ? u : null;
        if (!WebViewHost.IsCalendar(source))
            return;

        Succeeded = true;
        Title = "Connecté ✔ — cette fenêtre va se fermer";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }
}
