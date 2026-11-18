using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgendaReminder.App.Services;
using AgendaReminder.Core.Google;
using AgendaReminder.Core.Models;
using AgendaReminder.Core.Settings;

namespace AgendaReminder.App.Views;

/// <summary>Élément de liste déroulante avec libellé français.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public static class Choices
{
    public static Choice<DisplayStyle>[] DisplayStyles { get; } =
    {
        new(DisplayStyle.FullScreen, "Plein écran"),
        new(DisplayStyle.Toast, "Notification"),
        new(DisplayStyle.None, "Rien"),
    };

    public static Choice<DisplayStyle>[] GoogleReminderStyles { get; } =
    {
        new(DisplayStyle.Toast, "Notification"),
        new(DisplayStyle.FullScreen, "Plein écran"),
        new(DisplayStyle.None, "Ignorer"),
    };

    public static Choice<BlacklistAction>[] BlacklistActions { get; } =
    {
        new(BlacklistAction.Postpone, "Reporter jusqu'à ce que l'app soit quittée"),
        new(BlacklistAction.ToastOnly, "Notification au lieu du plein écran"),
        new(BlacklistAction.Silent, "Ne rien afficher"),
    };

    public static Choice<int?>[] EventReminders { get; } =
    {
        new(null, "Rappel par défaut de l'agenda"),
        new(0, "À l'heure de début"),
        new(5, "5 min avant"),
        new(10, "10 min avant"),
        new(15, "15 min avant"),
        new(30, "30 min avant"),
        new(60, "1 h avant"),
        new(1440, "1 jour avant"),
    };
}

internal static class Ui
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Brush> BrushCache = new();

    /// <summary>Couleur d'un élément selon les paramètres (couleur du type, ou couleur Google pour les évènements).</summary>
    public static Brush AccentFor(ReminderItem item)
    {
        var settings = ReminderEngine.Current?.Settings;
        if (settings is null) return BrushFromHex(AppSettings.DefaultColors()[item.Kind]);
        if (item.Kind == ReminderKind.Event && settings.UseGoogleCalendarColors && !string.IsNullOrWhiteSpace(item.GoogleColor))
            return BrushFromHex(item.GoogleColor!);
        return BrushFromHex(settings.ColorFor(item.Kind));
    }

    public static Brush AccentFor(ReminderKind kind) =>
        BrushFromHex(ReminderEngine.Current?.Settings.ColorFor(kind) ?? AppSettings.DefaultColors()[kind]);

    /// <summary>"#RRGGBB" -> pinceau (gris si la valeur est invalide).</summary>
    public static Brush BrushFromHex(string hex) => BrushCache.GetOrAdd(hex.Trim().ToUpperInvariant(), h =>
    {
        if (TryParseColor(h, out var c))
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
        return Frozen(0x88, 0x88, 0x88);
    });

    public static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>Palette proposée dans les paramètres (couleurs proches de celles de Google Agenda).</summary>
    public static readonly (string Name, string Hex)[] Palette =
    {
        ("Bleu", "#4C8BF5"), ("Myrtille", "#3F51B5"), ("Lavande", "#7986CB"), ("Raisin", "#8E24AA"),
        ("Flamant", "#E67C73"), ("Tomate", "#D50000"), ("Mandarine", "#F4511E"), ("Banane", "#F6BF26"),
        ("Sauge", "#33B679"), ("Basilic", "#0B8043"), ("Paon", "#039BE5"), ("Graphite", "#616161"),
        ("Menthe", "#34C38F"), ("Abricot", "#F5A04C"), ("Rose", "#EC4899"), ("Violet", "#A855F7"),
    };

    public static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static string DayLabel(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (date == today) return "Aujourd'hui";
        if (date == today.AddDays(1)) return "Demain";
        if (date == today.AddDays(-1)) return "Hier";
        var text = date.ToString(date.Year == today.Year ? "dddd d MMMM" : "dddd d MMMM yyyy", Fr);
        return char.ToUpper(text[0], Fr) + text[1..];
    }

    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().Replace(" ", "");
        return TimeOnly.TryParseExact(t, new[] { "H:mm", "HH:mm", "H\\hmm", "HH\\hmm", "H\\h", "HH\\h", "H", "HH" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    /// <summary>"10, 30, 60" -> [10, 30, 60]. Renvoie null si invalide.</summary>
    public static System.Collections.Generic.List<int>? ParseIntList(string? text)
    {
        var result = new System.Collections.Generic.List<int>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < 0)
                return null;
            result.Add(v);
        }
        return result;
    }

    public static string FormatIntList(System.Collections.Generic.IEnumerable<int> values) => string.Join(", ", values);

    public static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Ouverture " + url, ex); }
    }

    public static bool Confirm(DependencyObject owner, string message, string title = "Agenda Reminder") =>
        MessageBox.Show(Window.GetWindow(owner), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Error(DependencyObject owner, string message) =>
        MessageBox.Show(Window.GetWindow(owner), message, "Agenda Reminder", MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>Exécute une action Google en désactivant le bouton et en affichant les erreurs proprement.</summary>
    public static async Task<bool> RunAsync(FrameworkElement source, Func<Task> action)
    {
        var button = source as Button;
        if (button is not null) button.IsEnabled = false;
        try
        {
            await action();
            return true;
        }
        catch (AccountNeedsLoginException ex)
        {
            Error(source, ex.Message + "\n\nOuvrez la page « Comptes » et cliquez sur « Reconnecter ».");
        }
        catch (Google.GoogleApiException ex)
        {
            Log.Error("Action Google", ex);
            Error(source, "Google a refusé l'opération :\n" + (ex.Error?.Message ?? ex.Message));
        }
        catch (Exception ex)
        {
            Log.Error("Action", ex);
            Error(source, ex.Message);
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
        return false;
    }

    /// <summary>Glyphes Segoe Fluent Icons.</summary>
    public static class Glyph
    {
        public const string Calendar = "";
        public const string Tasks = "";
        public const string Accounts = "";
        public const string Settings = "";
        public const string Sync = "";
        public const string Add = "";
        public const string Delete = "";
        public const string Check = "";
        public const string Open = "";
        public const string Circle = "";
    }
}
