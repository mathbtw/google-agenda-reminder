using AgendaReminder.Core.Policy;
using AgendaReminder.Core.Settings;
using Xunit;

namespace AgendaReminder.Core.Tests;

public class DisplayPolicyTests
{
    private static AppSettings Settings() => new()
    {
        Blacklist =
        {
            new BlacklistEntry { Process = "Valorant.exe", Action = BlacklistAction.Postpone },
            new BlacklistEntry { Process = "teams", Action = BlacklistAction.ToastOnly },
            new BlacklistEntry { Process = "vlc.exe", Action = BlacklistAction.Silent },
        },
        AutoDetectFullscreenApps = true,
        AutoDetectAction = BlacklistAction.Postpone,
        MaxPostponeMinutes = 60,
    };

    [Fact]
    public void NoBlacklistedApp_ShowsWantedStyle()
    {
        var d = DisplayPolicy.Decide(DisplayStyle.FullScreen, new ForegroundInfo("chrome.exe", false), Settings(), TimeSpan.Zero);
        Assert.Equal(DisplayDecision.FullScreen, d);
    }

    [Theory]
    [InlineData("VALORANT.EXE", DisplayDecision.Postpone)]
    [InlineData("valorant", DisplayDecision.Postpone)]
    [InlineData("Teams.exe", DisplayDecision.Toast)]
    [InlineData("vlc.exe", DisplayDecision.Drop)]
    public void BlacklistedApp_AppliesItsAction(string process, DisplayDecision expected)
    {
        var d = DisplayPolicy.Decide(DisplayStyle.FullScreen, new ForegroundInfo(process, false), Settings(), TimeSpan.Zero);
        Assert.Equal(expected, d);
    }

    [Fact]
    public void Postpone_FallsBackToToast_AfterMaxDelay()
    {
        var d = DisplayPolicy.Decide(DisplayStyle.FullScreen, new ForegroundInfo("valorant.exe", true), Settings(), TimeSpan.FromMinutes(61));
        Assert.Equal(DisplayDecision.Toast, d);
    }

    [Fact]
    public void UnknownFullscreenApp_IsAutoDetected()
    {
        var d = DisplayPolicy.Decide(DisplayStyle.FullScreen, new ForegroundInfo("somegame.exe", true), Settings(), TimeSpan.Zero);
        Assert.Equal(DisplayDecision.Postpone, d);
    }

    [Fact]
    public void AutoDetect_CanBeDisabled()
    {
        var s = Settings();
        s.AutoDetectFullscreenApps = false;
        var d = DisplayPolicy.Decide(DisplayStyle.Toast, new ForegroundInfo("somegame.exe", true), s, TimeSpan.Zero);
        Assert.Equal(DisplayDecision.Toast, d);
    }

    [Fact]
    public void StyleNone_IsDropped()
    {
        var d = DisplayPolicy.Decide(DisplayStyle.None, new ForegroundInfo(null, false), Settings(), TimeSpan.Zero);
        Assert.Equal(DisplayDecision.Drop, d);
    }
}
