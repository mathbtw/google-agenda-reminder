using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AgendaReminder.App.Interop;

namespace AgendaReminder.App.Views;

/// <summary>Rappel plein écran épuré, sur l'écran principal. Peut regrouper plusieurs rappels simultanés.</summary>
public partial class FullScreenReminderWindow : Window
{
    private readonly ObservableCollection<ReminderView> _views = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _closing;

    /// <summary>Snooze demandé pour tous les rappels affichés (minutes).</summary>
    public event Action<IReadOnlyList<ReminderView>, int>? Snoozed;

    public FullScreenReminderWindow(IReadOnlyList<int> snoozeOptions)
    {
        InitializeComponent();
        Items.ItemsSource = _views;

        // Couvre tout l'écran principal, barre des tâches comprise.
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        foreach (var minutes in snoozeOptions)
        {
            var b = new Button { Content = $"+{minutes} min", Style = (Style)FindResource("GhostButton"), FontSize = 16, Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(6, 0, 6, 0) };
            b.Click += (_, _) => { Snoozed?.Invoke(_views.ToList(), minutes); FadeOutAndClose(); };
            Actions.Children.Add(b);
        }
        var ok = new Button { Content = "C'est noté", Style = (Style)FindResource("AccentButton"), FontSize = 16, Padding = new Thickness(28, 10, 28, 10), Margin = new Thickness(6, 0, 6, 0), IsDefault = true };
        ok.Click += (_, _) => FadeOutAndClose();
        Actions.Children.Add(ok);

        _clock.Tick += (_, _) => UpdateClock();
        UpdateClock();

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter) FadeOutAndClose();
        };
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            _clock.Start();
            BringToFront();
        };
        Closed += (_, _) => _clock.Stop();
    }

    public void Add(IEnumerable<ReminderView> views)
    {
        foreach (var v in views)
        {
            if (_views.All(x => x.Item.Id != v.Item.Id))
                _views.Add(v);
        }
        Items.Items.Refresh();
    }

    public void BringToFront()
    {
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(hwnd);
        Focus();
    }

    private void UpdateClock()
    {
        Clock.Text = DateTime.Now.ToString("HH:mm");
        Items.Items.Refresh(); // met à jour "dans X min"
    }

    public void FadeOutAndClose()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
