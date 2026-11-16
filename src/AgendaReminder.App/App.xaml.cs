using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using AgendaReminder.App.Services;
using AgendaReminder.Core;

namespace AgendaReminder.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private ReminderEngine? _engine;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\AgendaReminder.SingleInstance", out var isFirst);
        if (!isFirst)
        {
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

        if (!File.Exists(AppPaths.ClientSecret))
        {
            _tray.ShowBalloon("Configuration requise",
                "Placez votre fichier client_secret.json (OAuth « Application de bureau ») dans le dossier qui vient de s'ouvrir, puis « Synchroniser maintenant ».",
                System.Windows.Forms.ToolTipIcon.Warning);
            Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
        }

        _engine.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _engine?.Dispose();
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
