using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AgendaReminder.App.Services;

internal sealed record UpdateInfo(Version Version, string PageUrl, string? SetupUrl, string? Sha256, long Size);

/// <summary>
/// Mises à jour via les Releases GitHub : vérifie la dernière version publiée, télécharge
/// l'installateur, contrôle son empreinte SHA-256 puis l'exécute en mode silencieux
/// (l'installateur ferme l'app, met à jour et la relance).
/// </summary>
internal sealed class Updater : IDisposable
{
    private const string LatestReleaseApi = "https://api.github.com/repos/mathbtw/google-agenda-reminder/releases/latest";
    public const string ReleasesPage = "https://github.com/mathbtw/google-agenda-reminder/releases/latest";

    private static readonly HttpClient Http = CreateHttp();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly Func<bool> _enabled;

    public UpdateInfo? Available { get; private set; }

    public string Status { get; private set; } = "";

    public bool IsBusy { get; private set; }

    /// <summary>Une nouvelle version vient d'être détectée (ou l'état a changé).</summary>
    public event Action? Changed;

    public Updater(Func<bool> enabled)
    {
        _enabled = enabled;
        _timer.Tick += async (_, _) => { if (_enabled()) await CheckAsync(); };
    }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Installée via l'installateur (sinon : version portable, mise à jour manuelle).</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public void Start()
    {
        _timer.Start();
        // Première vérification un peu après le démarrage, pour ne pas ralentir le lancement de Windows.
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            if (_enabled()) await CheckAsync();
        };
        first.Start();
    }

    public async Task<UpdateInfo?> CheckAsync()
    {
        if (IsBusy) return Available;
        IsBusy = true;
        Status = "Recherche de mises à jour…";
        Changed?.Invoke();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response = await Http.GetAsync(LatestReleaseApi, cts.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Status = "Aucune version publique trouvée (dépôt privé ou aucune release).";
                return null;
            }
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            {
                Status = "Version publiée illisible : " + tag;
                return null;
            }
            latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));

            string? setupUrl = null, sha = null;
            long size = 0;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("AgendaReminder-Setup", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                setupUrl = asset.GetProperty("browser_download_url").GetString();
                size = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                if (asset.TryGetProperty("digest", out var d) && d.GetString() is { } digest
                    && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = digest[7..];
            }

            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
            if (latest > Current)
            {
                Available = new UpdateInfo(latest, page, setupUrl, sha, size);
                Status = $"Version {latest} disponible (installée : {Current}).";
                Log.Info("Mise à jour disponible : " + latest);
            }
            else
            {
                Available = null;
                Status = $"À jour (version {Current}).";
            }
            return Available;
        }
        catch (Exception ex)
        {
            Log.Error("Vérification des mises à jour", ex);
            Status = "Vérification impossible : " + ex.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Télécharge et lance l'installateur de la mise à jour, puis quitte l'app.
    /// Version portable : ouvre simplement la page de téléchargement.
    /// </summary>
    public async Task InstallAsync(IProgress<double>? progress = null)
    {
        var info = Available;
        if (info is null) return;
        if (!IsInstalled || info.SetupUrl is null)
        {
            Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
            return;
        }

        IsBusy = true;
        Status = "Téléchargement de la mise à jour…";
        Changed?.Invoke();
        var path = Path.Combine(Path.GetTempPath(), $"AgendaReminder-Setup-{info.Version}.exe");
        try
        {
            using (var response = await Http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? info.Size;
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = File.Create(path);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n));
                    sha.AppendData(buffer, 0, n);
                    read += n;
                    if (total > 0) progress?.Report((double)read / total);
                }

                var actual = Convert.ToHexString(sha.GetHashAndReset());
                if (info.Sha256 is { } expected && !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Le fichier téléchargé est corrompu (empreinte SHA-256 différente).");
            }

            Log.Info("Lancement de la mise à jour " + info.Version);
            // /SILENT : progression visible mais aucune question ; l'installateur ferme puis relance l'app.
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
            {
                UseShellExecute = true,
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Installation de la mise à jour", ex);
            Status = "Échec de la mise à jour : " + ex.Message;
            try { File.Delete(path); } catch { }
            throw;
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AgendaReminder", Current.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public void Dispose() => _timer.Stop();
}
