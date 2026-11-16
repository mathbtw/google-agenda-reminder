using System;
using System.IO;
using System.Threading.Tasks;
using AgendaReminder.Core;
using Microsoft.Web.WebView2.Core;

namespace AgendaReminder.App.Web;

/// <summary>
/// Environnement WebView2 partagé (même profil = mêmes cookies Google pour la fenêtre de connexion
/// et pour la lecture en arrière-plan).
/// </summary>
internal static class WebViewHost
{
    private static Task<CoreWebView2Environment>? _environment;

    public static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        return _environment ??= Create();

        static async Task<CoreWebView2Environment> Create()
        {
            Directory.CreateDirectory(AppPaths.WebViewProfile);
            var options = new CoreWebView2EnvironmentOptions
            {
                // La fenêtre de lecture est hors écran : on empêche Chromium de la mettre en veille.
                AdditionalBrowserArguments =
                    "--disable-features=CalculateNativeWinOcclusion " +
                    "--disable-background-timer-throttling " +
                    "--disable-renderer-backgrounding " +
                    "--disable-backgrounding-occluded-windows",
                Language = "fr-FR",
            };
            return await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewProfile, options);
        }
    }

    /// <summary>Vrai si le runtime WebView2 est installé (c'est le cas sur Windows 10/11 à jour).</summary>
    public static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsGoogleLogin(Uri? uri) =>
        uri is not null && uri.Host.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsCalendar(Uri? uri) =>
        uri is not null && uri.Host.Equals("calendar.google.com", StringComparison.OrdinalIgnoreCase);
}
