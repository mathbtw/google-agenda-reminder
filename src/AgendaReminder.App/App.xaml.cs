using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using AgendaReminder.App.Services;
using AgendaReminder.App.Views;
using AgendaReminder.App.Views.Dialogs;
using AgendaReminder.Core;
using AgendaReminder.Core.Google;

namespace AgendaReminder.App;

public partial class App : Application
{
    private const string ShowEventName = @"Local\AgendaReminder.Show";

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private ReminderEngine? _engine;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private Updater? _updates;
    private Version? _notifiedVersion;

    /// <summary>Service de mise à jour (null avant le démarrage).</summary>
    internal static Updater? Updates => Instance._updates;

    private static App Instance => (App)Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\AgendaReminder.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            // Déjà lancée (ex. raccourci du menu Démarrer) : on demande à l'instance existante d'afficher sa fenêtre.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Exception non gérée", args.Exception);
            args.Handled = true; // l'app tourne en tâche de fond : on évite de planter pour un rappel
        };

        Directory.CreateDirectory(AppPaths.Root);
        Log.Info("Démarrage");

        _engine = new ReminderEngine();
        _tray = new TrayIcon(_engine, () => Shutdown());
        ListenForShowRequests();

        _updates = new Updater(() => _engine.Settings.CheckForUpdates);
        _updates.Changed += OnUpdatesChanged;
        _updates.Start();

        var startedByWindows = Array.Exists(e.Args, a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        if (!GoogleAccountClient.HasClientSecret || _engine.Accounts.Accounts.Count == 0)
            ShowMainWindow("accounts"); // première utilisation : on guide la configuration
        else if (!startedByWindows)
            ShowMainWindow();

        _engine.Start();

    }

    /// <summary>Nouvelle version détectée : une notification de la zone de notification (une fois par version).</summary>
    private void OnUpdatesChanged()
    {
        if (_updates?.Available is not { } info || info.Version == _notifiedVersion) return;
        _notifiedVersion = info.Version;
        _tray?.ShowBalloon("Mise à jour disponible",
            $"Agenda Reminder {info.Version} est disponible. Ouvrez l'app pour l'installer.");
    }

    /// <summary>Un second lancement de l'exe signale cet évènement : on affiche la fenêtre principale.</summary>
    private void ListenForShowRequests()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(() => ShowMainWindow());
        })
        { IsBackground = true, Name = "ShowSignal" };
        thread.Start();
    }

    /// <summary>Affiche (ou crée) la fenêtre principale, éventuellement sur une page ou avec une création.</summary>
    public static void ShowMainWindow(string? page = null, string? newItem = null)
    {
        var app = Instance;
        if (app._engine is null) return;

        if (app._main is null)
        {
            app._main = new MainWindow(app._engine, page);
            app._main.Closed += (_, _) =>
            {
                app._main = null;
                // La fenêtre est fermée : on rend la mémoire au système (l'app reste dans la zone de notification).
                GC.Collect();
                GC.WaitForPendingFinalizers();
            };
            app._main.Show();
        }
        else
        {
            if (page is not null) app._main.Navigate(page);
            if (app._main.WindowState == WindowState.Minimized) app._main.WindowState = WindowState.Normal;
            app._main.Show();
        }
        app._main.Activate();

        if (newItem == "task") NewTaskDialog.Open(app._main, app._engine);
        else if (newItem == "event") NewEventDialog.Open(app._main, app._engine);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updates?.Dispose();
        _tray?.Dispose();
        _engine?.Dispose();
        _showSignal?.Dispose();
        _singleInstance?.Dispose();
        Log.Info("Arrêt");
        base.OnExit(e);
    }

    public static void OpenGoogleCalendar()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://calendar.google.com/") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Ouverture de Google Agenda", ex);
        }
    }
}
