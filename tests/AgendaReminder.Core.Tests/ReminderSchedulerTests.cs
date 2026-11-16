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
        Triggers = triggers,
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
        ReminderScheduler.AddSnooze(state, item, Now.AddMinutes(5));

        Assert.Empty(ReminderScheduler.GetDue(new[] { item }, state, Now, Grace));

        var later = Now.AddMinutes(6);
        var due = ReminderScheduler.GetDue(new[] { item }, state, later, Grace);
        Assert.Single(due);
        Assert.True(due[0].IsSnooze);

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

    [Fact]
    public void TimedEvent_WithoutGoogleReminder_UsesDefaults()
    {
        var s = new AppSettings { DefaultEventReminderMinutes = { 30 }, AlsoRemindAtEventStart = true };
        var start = Now.AddHours(2);

        var triggers = TriggerBuilder.ForTimedEvent(start, Array.Empty<int>(), s);

        Assert.Equal(new[] { start.AddMinutes(-30), start.AddMinutes(-10), start }, triggers);
    }

    [Fact]
    public void TimedEvent_WithGoogleReminders_UsesThem()
    {
        var s = new AppSettings();
        var start = Now.AddHours(2);

        var triggers = TriggerBuilder.ForTimedEvent(start, new[] { 15, 60 }, s);

        Assert.Equal(new[] { start.AddMinutes(-60), start.AddMinutes(-15) }, triggers);
    }
}
