using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AgendaReminder.App.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AgendaReminder.App.Web;

internal enum ScrapeStatus
{
    Ok,
    NeedsLogin,
    Failed,
}

/// <summary>
/// Ouvre Google Agenda dans un navigateur intégré invisible (hors écran), affiche la vue "jour"
/// d'une date et récupère les textes affichés (aria-label / texte des éléments).
/// L'interprétation de ces textes est faite par <see cref="Core.Web.WebTaskTimeMatcher"/>.
///
/// À créer pour une lecture puis à libérer : WebView2 lance des processus Edge assez gourmands.
/// </summary>
internal sealed class WebTaskTimeScraper : IDisposable
{
    // Récupère les libellés accessibles et textes courts de la zone principale de Google Agenda.
    private const string CollectScript = """
        (() => {
          const root = document.querySelector('[role="main"]') || document.body;
          const out = new Set();
          const sel = '[aria-label],[data-eventid],[data-eventchip],[role="button"],[role="listitem"],[role="row"],[role="gridcell"]';
          let n = 0;
          for (const e of root.querySelectorAll(sel)) {
            if (++n > 4000) break;
            const a = e.getAttribute('aria-label');
            if (a && a.length < 600) out.add(a);
            const t = e.innerText;
            if (t && t.length < 400) out.add(t);
          }
          return JSON.stringify(Array.from(out));
        })()
        """;

    private Window? _window;
    private WebView2? _web;

    private async Task EnsureAsync()
    {
        if (_web is not null)
            return;

        _web = new WebView2();
        _window = new Window
        {
            Title = "AgendaReminder - lecture",
            Width = 1400,
            Height = 1000,
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = _web,
        };
        _window.Show();

        var env = await WebViewHost.GetEnvironmentAsync();
        await _web.EnsureCoreWebView2Async(env);
        _web.CoreWebView2.IsMuted = true;
        _web.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
    }

    /// <summary>Textes de la vue "jour" de Google Agenda pour la date donnée.</summary>
    public async Task<(ScrapeStatus Status, List<string> Snippets)> CollectDayAsync(DateOnly date, CancellationToken ct)
    {
        try
        {
            await EnsureAsync();
            var core = _web!.CoreWebView2;

            var url = $"https://calendar.google.com/calendar/r/day/{date.Year}/{date.Month}/{date.Day}";
            if (!await NavigateAsync(core, url, ct))
                return (ScrapeStatus.Failed, new());

            var current = Uri.TryCreate(core.Source, UriKind.Absolute, out var u) ? u : null;
            if (WebViewHost.IsGoogleLogin(current))
                return (ScrapeStatus.NeedsLogin, new());
            if (!WebViewHost.IsCalendar(current))
                return (ScrapeStatus.Failed, new());

            // Google Agenda est une application JS : on attend que le contenu se stabilise.
            var last = -1;
            var snippets = new List<string>();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            await Task.Delay(1500, ct);
            while (DateTime.UtcNow < deadline)
            {
                snippets = await CollectAsync(core);
                if (snippets.Count > 0 && snippets.Count == last)
                    break;
                last = snippets.Count;
                await Task.Delay(700, ct);
            }

            return (ScrapeStatus.Ok, snippets);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Module web : lecture de Google Agenda", ex);
            return (ScrapeStatus.Failed, new());
        }
    }

    private static async Task<List<string>> CollectAsync(CoreWebView2 core)
    {
        var raw = await core.ExecuteScriptAsync(CollectScript);
        // ExecuteScriptAsync renvoie la valeur en JSON : ici une chaîne JSON contenant elle-même un tableau JSON.
        var json = JsonSerializer.Deserialize<string>(raw);
        return json is null ? new() : JsonSerializer.Deserialize<List<string>>(json) ?? new();
    }

    private static async Task<bool> NavigateAsync(CoreWebView2 core, string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);
        core.NavigationCompleted += OnCompleted;
        try
        {
            core.Navigate(url);
            var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30), ct));
            return finished == tcs.Task && tcs.Task.Result;
        }
        finally
        {
            core.NavigationCompleted -= OnCompleted;
        }
    }

    public void Dispose()
    {
        _web?.Dispose();
        _window?.Close();
        _web = null;
        _window = null;
    }
}
