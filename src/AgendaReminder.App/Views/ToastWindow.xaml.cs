using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AgendaReminder.App.Interop;

namespace AgendaReminder.App.Views;

/// <summary>
/// Notification maison (non système) : ne vole jamais le focus, reste au-dessus des fenêtres,
/// se ferme seule après un délai (mis en pause au survol de la souris).
/// </summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _autoClose = new();
    private bool _closing;

    /// <summary>Déclenché quand l'utilisateur choisit un snooze (minutes).</summary>
    public event Action<int>? Snoozed;

    public ToastWindow(ReminderView view, IReadOnlyList<int> snoozeOptions, int durationSeconds)
    {
        InitializeComponent();
        DataContext = view;

        if (snoozeOptions.Count > 0)
        {
            var minutes = snoozeOptions[0];
            var snooze = new Button { Content = $"Rappeler dans {minutes} min", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 6, 0) };
            snooze.Click += (_, _) => { Snoozed?.Invoke(minutes); FadeOutAndClose(); };
            Actions.Children.Add(snooze);
        }
        var ok = new Button { Content = "OK", Style = (Style)FindResource("AccentButton") };
        ok.Click += (_, _) => FadeOutAndClose();
        Actions.Children.Add(ok);

        if (durationSeconds > 0)
        {
            _autoClose.Interval = TimeSpan.FromSeconds(durationSeconds);
            _autoClose.Tick += (_, _) => FadeOutAndClose();
            MouseEnter += (_, _) => _autoClose.Stop();
            MouseLeave += (_, _) => _autoClose.Start();
        }

        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            if (durationSeconds > 0) _autoClose.Start();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Pas d'activation au clic + absent du Alt+Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => FadeOutAndClose();

    private void OnBodyClick(object sender, MouseButtonEventArgs e)
    {
        // Les boutons marquent l'évènement comme traité : on n'arrive ici que pour un clic sur le corps.
        App.OpenGoogleCalendar();
        FadeOutAndClose();
    }

    public void FadeOutAndClose()
    {
        if (_closing) return;
        _closing = true;
        _autoClose.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
