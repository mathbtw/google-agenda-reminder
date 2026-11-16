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

    public static Brush AccentFor(ReminderKind kind) => kind switch
    {
        ReminderKind.Birthday => Frozen(0xF5, 0xA0, 0x4C),
        ReminderKind.Task => Frozen(0x34, 0xC3, 0x8F),
        _ => Frozen(0x4C, 0x8B, 0xF5),
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
