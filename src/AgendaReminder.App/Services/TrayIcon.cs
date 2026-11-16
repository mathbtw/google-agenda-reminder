using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using AgendaReminder.App.Interop;
using AgendaReminder.Core;
using AgendaReminder.Core.Models;

namespace AgendaReminder.App.Services;

/// <summary>Icône de la zone de notification et son menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly ReminderEngine _engine;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _upcoming = new("Prochains rappels");
    private readonly ToolStripMenuItem _startup = new("Lancer au démarrage de Windows") { CheckOnClick = true };
    private readonly ToolStripMenuItem _web = new("Heure des tâches (module web, expérimental)");
    private readonly ToolStripMenuItem _webStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _webEnabled = new("Activer") { CheckOnClick = true };
    private bool _refreshingMenu;
    private Icon? _currentIcon;
    private int _iconDay = -1;

    public TrayIcon(ReminderEngine engine, Action exit)
    {
        _engine = engine;

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(_upcoming);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Synchroniser maintenant", null, async (_, _) => await _engine.SyncAsync());
        menu.Items.Add("Ouvrir Google Agenda", null, (_, _) => App.OpenGoogleCalendar());
        var test = new ToolStripMenuItem("Tester");
        test.DropDownItems.Add("Notification", null, (_, _) => _engine.ShowToast(SampleItem(ReminderKind.Event)));
        test.DropDownItems.Add("Rappel plein écran", null, (_, _) => _engine.ShowFullScreen(new[] { SampleItem(ReminderKind.Task) }));
        test.DropDownItems.Add("Application au premier plan (dans 5 s)", null, async (_, _) =>
        {
            await System.Threading.Tasks.Task.Delay(5000);
            var fg = ForegroundAppDetector.Get();
            _icon!.ShowBalloonTip(5000, "Application au premier plan",
                $"{fg.ProcessName ?? "(inconnue)"}\nPlein écran / occupé : {(fg.IsFullscreenOrBusy ? "oui" : "non")}", ToolTipIcon.Info);
        });
        menu.Items.Add(test);

        _web.DropDownItems.Add(_webStatus);
        _web.DropDownItems.Add(_webEnabled);
        _web.DropDownItems.Add("Se connecter au site Google Agenda…", null, async (_, _) => await _engine.LoginWebAsync());
        _web.DropDownItems.Add("Lire les heures maintenant", null, async (_, _) =>
        {
            var result = await _engine.RefreshWebTaskTimesAsync();
            ShowBalloon("Heure des tâches", result);
        });
        _webEnabled.CheckedChanged += async (_, _) =>
        {
            if (_refreshingMenu) return;
            await _engine.SetWebTaskTimesEnabledAsync(_webEnabled.Checked);
        };
        menu.Items.Add(_web);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Paramètres (settings.json)", null, (_, _) => Open(AppPaths.Settings));
        menu.Items.Add("Ouvrir le dossier de l'app", null, (_, _) => Open(AppPaths.Root));
        menu.Items.Add(_startup);
        menu.Items.Add("Se déconnecter de Google", null, (_, _) => _engine.SignOut());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => exit());

        _startup.CheckedChanged += (_, _) =>
        {
            if (_refreshingMenu) return;
            try { StartupRegistration.Set(_startup.Checked); }
            catch (Exception ex) { Log.Error("Démarrage automatique", ex); }
        };

        menu.Opening += (_, _) => RefreshMenu();

        _icon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "Agenda Reminder",
        };
        _icon.DoubleClick += (_, _) => App.OpenGoogleCalendar();

        _engine.StatusChanged += RefreshStatus;
        RefreshStatus();
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info) =>
        _icon.ShowBalloonTip(8000, title, text, icon);

    private void RefreshStatus()
    {
        UpdateIcon();
        var text = _engine.LastError is { } err
            ? "⚠ " + err
            : _engine.LastSync is { } t ? $"Synchronisé à {t:HH:mm}" : "Synchronisation…";
        _status.Text = text;
        // Le texte d'infobulle est limité à 63 caractères.
        var tip = "Agenda Reminder — " + text;
        _icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    private void RefreshMenu()
    {
        RefreshStatus();
        _refreshingMenu = true;
        _startup.Checked = StartupRegistration.IsEnabled;
        _webEnabled.Checked = _engine.Settings.EnableWebTaskTimes;
        _refreshingMenu = false;
        _webStatus.Text = "État : " + _engine.WebStatus;

        _upcoming.DropDownItems.Clear();
        var next = _engine.Upcoming().Take(10).ToList();
        if (next.Count == 0)
        {
            _upcoming.DropDownItems.Add(new ToolStripMenuItem("Aucun rappel prévu") { Enabled = false });
            return;
        }
        foreach (var (item, fireAt) in next)
        {
            var kind = item.Kind switch { ReminderKind.Task => "✔", ReminderKind.Birthday => "🎂", _ => "📅" };
            var when = fireAt.ToLocalTime();
            var day = when.Date == DateTime.Today ? "" : when.ToString("ddd d ") ;
            _upcoming.DropDownItems.Add(new ToolStripMenuItem($"{day}{when:HH:mm}   {kind} {item.Title}") { Enabled = false });
        }
    }

    /// <summary>Icône générée : un petit calendrier avec le numéro du jour.</summary>
    private void UpdateIcon()
    {
        var day = DateTime.Today.Day;
        if (day == _iconDay) return;
        _iconDay = day;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var body = new SolidBrush(Color.White);
            using var header = new SolidBrush(Color.FromArgb(0x4C, 0x8B, 0xF5));
            g.FillRectangle(body, 2, 4, 28, 26);
            g.FillRectangle(header, 2, 4, 28, 8);
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new SolidBrush(Color.FromArgb(0x20, 0x21, 0x24));
            var s = day.ToString();
            var size = g.MeasureString(s, font);
            g.DrawString(s, font, text, (32 - size.Width) / 2, 12 + (18 - size.Height) / 2);
        }

        var hIcon = bmp.GetHicon();
        var newIcon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        _icon.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;
    }

    private static ReminderItem SampleItem(ReminderKind kind) => new()
    {
        Id = "test:" + Guid.NewGuid(),
        Kind = kind,
        Title = kind == ReminderKind.Task ? "Envoyer le dossier à Julie" : "Réunion d'équipe",
        Start = DateTimeOffset.Now.AddMinutes(10),
        End = DateTimeOffset.Now.AddMinutes(40),
        Location = kind == ReminderKind.Event ? "Salle 3" : null,
        Source = "Test",
    };

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Ouverture " + path, ex); }
    }

    public void Dispose()
    {
        _engine.StatusChanged -= RefreshStatus;
        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}
