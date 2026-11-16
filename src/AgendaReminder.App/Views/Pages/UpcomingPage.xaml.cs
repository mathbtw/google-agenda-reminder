using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgendaReminder.App.Services;
using AgendaReminder.App.Views.Dialogs;
using AgendaReminder.Core.Models;

namespace AgendaReminder.App.Views.Pages;

public sealed record DayHeader(string Label);

public sealed class UpcomingRow
{
    public required ReminderItem Item { get; init; }
    public required string TimeText { get; init; }
    public required string Subtitle { get; init; }

    public string Title => Item.Title;
    public Brush Accent => Ui.AccentFor(Item.Kind);

    public string KindLabel => Item.Kind switch
    {
        ReminderKind.Task => "Tâche",
        ReminderKind.Birthday => "Anniversaire",
        _ => "Évènement",
    };

    public Visibility CompleteVisibility => Item.Kind == ReminderKind.Task && Item.CanEdit ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OpenVisibility => string.IsNullOrEmpty(Item.Link) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DeleteVisibility => Item.CanEdit ? Visibility.Visible : Visibility.Collapsed;
}

public partial class UpcomingPage : UserControl, IPage
{
    private readonly ReminderEngine _engine;

    internal UpcomingPage(ReminderEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        var accounts = _engine.Accounts;
        var selected = accounts.SelectedId;
        var showAccount = selected is null && accounts.Accounts.Count > 1;
        // Toute la journée d'aujourd'hui (y compris ce qui est déjà passé), puis les jours suivants.
        var items = _engine.Items
            .Where(i => selected is null || i.AccountId == selected)
            .Where(i => i.Start.LocalDateTime.Date >= DateTime.Today)
            .OrderBy(i => i.Start.LocalDateTime.Date).ThenBy(i => i.AllDay ? 0 : 1).ThenBy(i => i.Start)
            .ToList();

        var rows = new List<object>();
        foreach (var day in items.GroupBy(i => DateOnly.FromDateTime(i.Start.LocalDateTime)))
        {
            rows.Add(new DayHeader(Ui.DayLabel(day.Key)));
            foreach (var item in day)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.Location)) parts.Add(item.Location!);
                if (!string.IsNullOrWhiteSpace(item.Source)) parts.Add(item.Source!);
                if (showAccount) parts.Add(accounts.EmailOf(item.AccountId));
                rows.Add(new UpcomingRow { Item = item, TimeText = TimeText(item), Subtitle = string.Join("  ·  ", parts) });
            }
        }
        List.ItemsSource = rows;

        Hint.Text = accounts.Accounts.Count == 0
            ? "Aucun compte connecté. Ouvrez la page « Comptes » pour connecter Google."
            : rows.Count == 0 ? $"Rien de prévu pour les {_engine.Settings.LookAheadDays} prochains jours." : "";
        Hint.Visibility = Hint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string TimeText(ReminderItem item)
    {
        if (item.Kind == ReminderKind.Birthday || item.AllDay) return "Journée";
        if (item.Kind == ReminderKind.Task && !item.HasExplicitTime) return "Dans la journée";
        var start = item.Start.LocalDateTime;
        return item.End is { } end ? $"{start:HH:mm} – {end.LocalDateTime:HH:mm}" : $"{start:HH:mm}";
    }

    private static UpcomingRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as UpcomingRow;

    private async void OnComplete(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var item = row.Item;
        var ok = await Ui.RunAsync((FrameworkElement)sender, async () =>
        {
            var client = await _engine.Accounts.GetClientAsync(item.AccountId!);
            await client.CompleteTaskAsync(item.ContainerId!, item.SourceId!, default);
        });
        if (ok) await _engine.SyncAsync();
    }

    private void OnOpen(object sender, RoutedEventArgs e) => Ui.OpenUrl(RowOf(sender)?.Item.Link);

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var item = row.Item;
        var what = item.Kind == ReminderKind.Task ? "la tâche" : "l'évènement";
        var recurring = item.Kind == ReminderKind.Event && item.SourceId?.Contains('_') == true
            ? "\n\n(Évènement récurrent : seule cette occurrence sera supprimée.)" : "";
        if (!Ui.Confirm(this, $"Supprimer {what} « {item.Title} » de Google ?{recurring}"))
            return;

        var ok = await Ui.RunAsync((FrameworkElement)sender, async () =>
        {
            var client = await _engine.Accounts.GetClientAsync(item.AccountId!);
            if (item.Kind == ReminderKind.Task)
                await client.DeleteTaskAsync(item.ContainerId!, item.SourceId!, default);
            else
                await client.DeleteEventAsync(item.ContainerId!, item.SourceId!, default);
        });
        if (ok) await _engine.SyncAsync();
    }

    private void OnNewEvent(object sender, RoutedEventArgs e) => NewEventDialog.Open(this, _engine);

    private void OnNewTask(object sender, RoutedEventArgs e) => NewTaskDialog.Open(this, _engine);
}
