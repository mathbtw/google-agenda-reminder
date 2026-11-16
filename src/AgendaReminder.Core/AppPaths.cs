namespace AgendaReminder.Core;

/// <summary>Emplacements des fichiers de l'application (%APPDATA%\AgendaReminder).</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AgendaReminder");

    public static string Settings => Path.Combine(Root, "settings.json");

    public static string State => Path.Combine(Root, "state.json");

    public static string Cache => Path.Combine(Root, "cache.json");

    /// <summary>Identifiants OAuth "Application de bureau" téléchargés depuis la Google Cloud Console.</summary>
    public static string ClientSecret => Path.Combine(Root, "client_secret.json");

    /// <summary>Dossier où Google.Apis stocke le refresh token.</summary>
    public static string TokenStore => Path.Combine(Root, "token");

    /// <summary>Heures de tâches lues par le module web (id de tâche -> HH:mm).</summary>
    public static string WebTaskTimes => Path.Combine(Root, "web_task_times.json");

    /// <summary>Profil du navigateur intégré (cookies de session Google du module web).</summary>
    public static string WebViewProfile => Path.Combine(Root, "webview");

    public static string Log => Path.Combine(Root, "app.log");
}
