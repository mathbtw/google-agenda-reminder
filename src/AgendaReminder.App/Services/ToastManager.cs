using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AgendaReminder.App.Views;

namespace AgendaReminder.App.Services;

/// <summary>Empile les toasts en bas à droite de l'écran principal, au-dessus de la barre des tâches.</summary>
internal sealed class ToastManager
{
    private const int MaxVisible = 4;
    private readonly List<ToastWindow> _open = new();

    public void Show(ReminderView view, IReadOnlyList<int> snoozeOptions, int durationSeconds, Action<int> onSnooze)
    {
        if (_open.Count >= MaxVisible)
            _open[0].FadeOutAndClose();

        var toast = new ToastWindow(view, snoozeOptions, durationSeconds);
        toast.Snoozed += onSnooze;
        toast.Closed += (_, _) =>
        {
            _open.Remove(toast);
            Layout();
        };
        toast.SizeChanged += (_, _) => Layout();

        _open.Add(toast);
        toast.Left = -10000; // positionné après le calcul de la hauteur
        toast.Show();
        Layout();
    }

    private void Layout()
    {
        var area = SystemParameters.WorkArea;
        var bottom = area.Bottom;
        foreach (var toast in Enumerable.Reverse(_open))
        {
            var height = toast.ActualHeight > 0 ? toast.ActualHeight : 150;
            bottom -= height;
            toast.Left = area.Right - toast.Width;
            toast.Top = bottom;
        }
    }
}
