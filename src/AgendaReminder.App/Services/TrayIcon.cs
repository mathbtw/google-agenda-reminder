using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using AgendaReminder.App.Interop;
using AgendaReminder.Core.Models;

namespace AgendaReminder.App.Services;

/// <summary>Icône de la zone de notification et son menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly ReminderEngine _engine;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _upcoming = new("Prochains rappels");
    private Icon? _currentIcon;
    private int _iconDay = -1;

    public TrayIcon(ReminderEngine engine, Action exit)
    {
        _engine = engine;

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Ouvrir Agenda Reminder", null, (_, _) => App.ShowMainWindow());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(_status);
        menu.Items.Add(_upcoming);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Nouvelle tâche…", null, (_, _) => App.ShowMainWindow(newItem: "task"));
        menu.Items.Add("Nouvel évènement…", null, (_, _) => App.ShowMainWindow(newItem: "event"));
        menu.Items.Add("Synchroniser maintenant", null, async (_, _) => await _engine.SyncAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Paramètres", null, (_, _) => App.ShowMainWindow("settings"));
        menu.Items.Add("Quitter", null, (_, _) => exit());

        menu.Opening += (_, _) => RefreshMenu();

        _icon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "Agenda Reminder",
        };
        // Clic gauche : ouvre la fenêtre principale.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) App.ShowMainWindow();
        };

        _engine.StatusChanged += RefreshStatus;
        RefreshStatus();
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info) =>
        _icon.ShowBalloonTip(8000, title, text, icon);

    private void RefreshStatus()
    {
        UpdateIcon();
        var text = _engine.IsSyncing ? "Synchronisation…" : _engine.LastError is { } err
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

    public void Dispose()
    {
        _engine.StatusChanged -= RefreshStatus;
        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}
