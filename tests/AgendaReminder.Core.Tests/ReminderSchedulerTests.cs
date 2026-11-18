using AgendaReminder.Core.Models;
using AgendaReminder.Core.Scheduling;
using AgendaReminder.Core.Settings;
using Xunit;

namespace AgendaReminder.Core.Tests;

public class ReminderSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(60);

    private static ReminderItem Item(string id, params DateTimeOffset[] triggers) => new()
    {
        Id = id,
        Kind = ReminderKind.Event,
        Title = id,
        Start = Now.AddMinutes(10),
        Triggers = triggers.Select(t => new ReminderTrigger(t, DisplayStyle.Toast)).ToList(),
    };

    [Fact]
    public void DueTrigger_IsReturnedOnce()
    {
        var state = new ReminderState();
        var items = new[] { Item("a", Now.AddMinutes(-1)) };

        var due = ReminderScheduler.GetDue(items, state, Now, Grace);
        Assert.Single(due);

        ReminderScheduler.MarkHandled(state, due[0]);
        Assert.Empty(ReminderScheduler.GetDue(items, state, Now, Grace));
    }

    [Fact]
    public void FutureAndTooOldTriggers_AreIgnored()
    {
        var items = new[] { Item("a", Now.AddMinutes(5)), Item("b", Now.AddHours(-3)) };
        Assert.Empty(ReminderScheduler.GetDue(items, new ReminderState(), Now, Grace));
    }

    [Fact]
    public void SeveralMissedTriggers_OnlyLatestIsShown_OthersMarked()
    {
        var state = new ReminderState();
        var items = new[] { Item("a", Now.AddMinutes(-30), Now.AddMinutes(-5)) };

        var due = ReminderScheduler.GetDue(items, state, Now, Grace);

        Assert.Single(due);
        Assert.Equal(Now.AddMinutes(-5), due[0].FireAt);
        Assert.Single(state.Fired); // l'ancien rappel est consommé
    }

    [Fact]
    public void Snooze_FiresLater_AndIsRemovedWhenHandled()
    {
        var state = new ReminderState();
        var item = Item("a");
        ReminderScheduler.AddSnooze(state, item, Now.AddMinutes(5), DisplayStyle.FullScreen);

        Assert.Empty(ReminderScheduler.GetDue(new[] { item }, state, Now, Grace));

        var later = Now.AddMinutes(6);
        var due = ReminderScheduler.GetDue(new[] { item }, state, later, Grace);
        Assert.Single(due);
        Assert.True(due[0].IsSnooze);
        Assert.Equal(DisplayStyle.FullScreen, due[0].Style);

        ReminderScheduler.MarkHandled(state, due[0]);
        Assert.Empty(state.Snoozes);
    }

    [Fact]
    public void Prune_RemovesOldFiredKeys()
    {
        var state = new ReminderState();
        state.Fired["old"] = Now.AddDays(-10);
        state.Fired["recent"] = Now.AddHours(-1);

        ReminderScheduler.Prune(state, Now, TimeSpan.FromDays(3));

        Assert.Equal(new[] { "recent" }, state.Fired.Keys);
    }

    private static AppSettings With(int[] notif, int[] full, DisplayStyle google = DisplayStyle.Toast)
    {
        var s = new AppSettings { GoogleRemindersStyle = google };
        s.Reminders[ReminderKind.Event] = new KindReminders { NotificationMinutes = notif.ToList(), FullScreenMinutes = full.ToList() };
        return s;
    }

    [Fact]
    public void TimedEvent_NotificationThenFullScreen()
    {
        var start = Now.AddHours(2);
        var triggers = TriggerBuilder.ForTimedEvent(start, Array.Empty<int>(), With(new[] { 10 }, new[] { 2 }));

        Assert.Equal(new[]
        {
            new ReminderTrigger(start.AddMinutes(-10), DisplayStyle.Toast),
            new ReminderTrigger(start.AddMinutes(-2), DisplayStyle.FullScreen),
        }, triggers);
    }

    [Fact]
    public void SameMinute_FullScreenWins()
    {
        var start = Now.AddHours(2);
        var triggers = TriggerBuilder.ForTimedEvent(start, new[] { 5 }, With(new[] { 5 }, new[] { 5 }));

        Assert.Equal(new[] { new ReminderTrigger(start.AddMinutes(-5), DisplayStyle.FullScreen) }, triggers);
    }

    [Fact]
    public void GoogleReminders_UseConfiguredStyle_OrAreIgnored()
    {
        var start = Now.AddHours(2);

        var asFull = TriggerBuilder.ForTimedEvent(start, new[] { 30 }, With(Array.Empty<int>(), Array.Empty<int>(), DisplayStyle.FullScreen));
        Assert.Equal(new[] { new ReminderTrigger(start.AddMinutes(-30), DisplayStyle.FullScreen) }, asFull);

        var ignored = TriggerBuilder.ForTimedEvent(start, new[] { 30 }, With(new[] { 10 }, Array.Empty<int>(), DisplayStyle.None));
        Assert.Equal(new[] { new ReminderTrigger(start.AddMinutes(-10), DisplayStyle.Toast) }, ignored);
    }

    [Fact]
    public void Task_UsesTaskSettings()
    {
        var s = new AppSettings();
        s.Reminders[ReminderKind.Task] = new KindReminders { NotificationMinutes = { 15 }, FullScreenMinutes = { 0 } };
        var when = Now.AddHours(1);

        var triggers = TriggerBuilder.ForTask(when, s);

        Assert.Equal(new[]
        {
            new ReminderTrigger(when.AddMinutes(-15), DisplayStyle.Toast),
            new ReminderTrigger(when, DisplayStyle.FullScreen),
        }, triggers);
    }

    [Fact]
    public void MissingKindInSettingsFile_FallsBackToDefaults()
    {
        var s = new AppSettings { Reminders = new() };
        Assert.NotEmpty(TriggerBuilder.ForTask(Now, s));
    }

    [Fact]
    public void DueReminder_CarriesTriggerStyle()
    {
        var item = new ReminderItem
        {
            Id = "a", Kind = ReminderKind.Event, Title = "a", Start = Now,
            Triggers = new[] { new ReminderTrigger(Now.AddMinutes(-1), DisplayStyle.FullScreen) },
        };
        var due = ReminderScheduler.GetDue(new[] { item }, new ReminderState(), Now, Grace);
        Assert.Equal(DisplayStyle.FullScreen, Assert.Single(due).Style);
    }
}
