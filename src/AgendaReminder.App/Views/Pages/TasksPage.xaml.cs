using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AgendaReminder.App.Services;
using AgendaReminder.App.Views.Dialogs;
using AgendaReminder.Core.Google;

namespace AgendaReminder.App.Views.Pages;

public sealed class TaskRow
{
    public required TaskEntry Task { get; init; }
    public required string Subtitle { get; init; }
    public string Title => string.IsNullOrWhiteSpace(Task.Title) ? "(Sans titre)" : Task.Title;
    public Visibility OpenVisibility => string.IsNullOrEmpty(Task.Link) ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>Toutes les tâches non terminées (avec ou sans échéance), lues en direct chez Google.</summary>
public partial class TasksPage : UserControl, IPage
{
    private readonly ReminderEngine _engine;
    private int _loadVersion;

    internal TasksPage(ReminderEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    public void Refresh() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var accounts = _engine.Accounts;
        var targets = accounts.Accounts
            .Where(a => accounts.SelectedId is null || a.Id == accounts.SelectedId)
            .ToList();

        if (targets.Count == 0)
        {
            List.ItemsSource = null;
            SetHint("Aucun compte connecté. Ouvrez la page « Comptes » pour connecter Google.");
            return;
        }

        SetHint("Chargement…");
        var entries = new List<TaskEntry>();
        var errors = new List<string>();
        foreach (var account in targets)
        {
            try
            {
                var client = await accounts.GetClientAsync(account.Id);
                entries.AddRange(await client.ListOpenTasksAsync(_engine.Settings, _engine.WebTimes, default));
            }
            catch (Exception ex)
            {
                errors.Add($"{account.Email} : {ex.Message}");
            }
        }
        if (version != _loadVersion) return; // un chargement plus récent a été lancé

        var showAccount = targets.Count > 1;
        var rows = new List<object>();
        foreach (var group in entries
                     .GroupBy(t => (t.AccountId, t.ListId, t.ListTitle))
                     .OrderBy(g => g.Key.ListTitle))
        {
            var header = group.Key.ListTitle + (showAccount ? "  ·  " + accounts.EmailOf(group.Key.AccountId) : "");
            rows.Add(new DayHeader(header));
            foreach (var t in group.OrderBy(t => t.DueDate ?? DateOnly.MaxValue).ThenBy(t => t.Time ?? TimeOnly.MaxValue))
                rows.Add(new TaskRow { Task = t, Subtitle = Subtitle(t) });
        }
        List.ItemsSource = rows;

        SetHint(errors.Count > 0
            ? "⚠ " + string.Join("\n⚠ ", errors)
            : rows.Count == 0 ? "Aucune tâche en cours. 🎉" : "");
    }

    private static string Subtitle(TaskEntry t)
    {
        var parts = new List<string>();
        if (t.DueDate is { } d)
        {
            var overdue = d < DateOnly.FromDateTime(DateTime.Today) ? "En retard · " : "";
            parts.Add(overdue + Ui.DayLabel(d) + (t.Time is { } time ? $" · {time:HH\\:mm}" : ""));
        }
        else
        {
            parts.Add("Sans échéance");
        }
        if (!string.IsNullOrWhiteSpace(t.Notes))
            parts.Add(t.Notes!.Replace('\n', ' ').Trim());
        return string.Join("  ·  ", parts);
    }

    private void SetHint(string text)
    {
        Hint.Text = text;
        Hint.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static TaskRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as TaskRow;

    private async void OnComplete(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var ok = await Ui.RunAsync((FrameworkElement)sender, async () =>
        {
            var client = await _engine.Accounts.GetClientAsync(row.Task.AccountId);
            await client.CompleteTaskAsync(row.Task.ListId, row.Task.TaskId, default);
        });
        if (ok) await AfterChangeAsync();
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (!Ui.Confirm(this, $"Supprimer la tâche « {row.Title} » de Google ?")) return;
        var ok = await Ui.RunAsync((FrameworkElement)sender, async () =>
        {
            var client = await _engine.Accounts.GetClientAsync(row.Task.AccountId);
            await client.DeleteTaskAsync(row.Task.ListId, row.Task.TaskId, default);
        });
        if (ok) await AfterChangeAsync();
    }

    private void OnOpen(object sender, RoutedEventArgs e) => Ui.OpenUrl(RowOf(sender)?.Task.Link);

    private async void OnReload(object sender, RoutedEventArgs e) => await LoadAsync();

    private void OnNewTask(object sender, RoutedEventArgs e)
    {
        if (NewTaskDialog.Open(this, _engine))
            _ = LoadAsync();
    }

    private async Task AfterChangeAsync()
    {
        await LoadAsync();
        _ = _engine.SyncAsync(); // met aussi à jour les rappels
    }
}
