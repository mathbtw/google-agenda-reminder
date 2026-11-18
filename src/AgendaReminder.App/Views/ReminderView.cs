using System;
using System.Globalization;
using System.Windows.Media;
using AgendaReminder.Core.Models;

namespace AgendaReminder.App.Views;

/// <summary>Données d'affichage d'un rappel (toast ou plein écran).</summary>
public sealed class ReminderView
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public required ReminderItem Item { get; init; }

    public string Title => Item.Title;

    public string KindLabel => Item.Kind switch
    {
        ReminderKind.Birthday => "ANNIVERSAIRE",
        ReminderKind.Task => "TÂCHE",
        _ => "ÉVÈNEMENT",
    };

    public string Glyph => Item.Kind switch
    {
        ReminderKind.Birthday => "🎂",
        ReminderKind.Task => "✔",
        _ => "📅",
    };

    public Brush Accent => Ui.AccentFor(Item);

    public string WhenText
    {
        get
        {
            var start = Item.Start.ToLocalTime();
            var day = DayLabel(start);
            if (Item.Kind == ReminderKind.Birthday)
                return day;
            if (Item.AllDay)
                return $"{day} · toute la journée";
            if (Item.Kind == ReminderKind.Task)
                return Item.HasExplicitTime ? $"{day} · {start:HH:mm}" : $"Échéance {day.ToLower(Fr)}";
            var end = Item.End?.ToLocalTime();
            return end is { } e ? $"{day} · {start:HH:mm} – {e:HH:mm}" : $"{day} · {start:HH:mm}";
        }
    }

    public string RelativeText
    {
        get
        {
            if (Item.AllDay || Item.Kind == ReminderKind.Birthday || (Item.Kind == ReminderKind.Task && !Item.HasExplicitTime))
                return "";
            var delta = Item.Start - DateTimeOffset.Now;
            var minutes = (int)Math.Round(delta.TotalMinutes);
            return minutes switch
            {
                0 => "maintenant",
                > 0 and < 60 => $"dans {minutes} min",
                >= 60 => $"dans {minutes / 60} h {minutes % 60:00}",
                < 0 and > -60 => $"il y a {-minutes} min",
                _ => "en retard",
            };
        }
    }

    public string Details
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(Item.Location)) parts.Add("📍 " + Item.Location);
            if (!string.IsNullOrWhiteSpace(Item.Source)) parts.Add(Item.Source!);
            return string.Join("   ·   ", parts);
        }
    }

    private static string DayLabel(DateTimeOffset date)
    {
        var today = DateTime.Today;
        if (date.Date == today) return "Aujourd'hui";
        if (date.Date == today.AddDays(1)) return "Demain";
        if (date.Date == today.AddDays(-1)) return "Hier";
        return Fr.TextInfo.ToTitleCase(date.ToString("dddd d MMMM", Fr));
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
