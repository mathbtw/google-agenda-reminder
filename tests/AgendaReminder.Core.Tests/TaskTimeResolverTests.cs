using AgendaReminder.Core.Scheduling;
using Xunit;

namespace AgendaReminder.Core.Tests;

public class TaskTimeResolverTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris");

    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");

    [Fact]
    public void DateOnlyDue_UsesDefaultTime_OnSameCalendarDay()
    {
        var r = TaskTimeResolver.Resolve("2026-11-20T00:00:00.000Z", "Acheter du pain", null, "09:00", Paris)!.Value;

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(1)), r.When);
        Assert.False(r.IsExplicit);
        Assert.Equal("default", r.Origin);
    }

    [Fact]
    public void DateOnlyDue_DoesNotShiftDay_InNegativeOffsetZone()
    {
        // Minuit UTC = la veille au soir à New York : la date ne doit PAS reculer d'un jour.
        var r = TaskTimeResolver.Resolve("2026-11-20T00:00:00.000Z", "x", null, "08:00", NewYork)!.Value;

        Assert.Equal(new DateOnly(2026, 11, 20), DateOnly.FromDateTime(r.When.DateTime));
        Assert.Equal(8, r.When.Hour);
    }

    [Theory]
    [InlineData(null, "⏰ 14h30", 14, 30)]
    [InlineData(null, "rdv @14:30 avec Paul", 14, 30)]
    [InlineData("Appeler maman à 18h", null, 18, 0)]
    [InlineData("Appeler maman a 9h", null, 9, 0)]
    [InlineData("Sortir le chien", "heure: 7h15", 7, 15)]
    [InlineData("Réunion 10:45", null, 10, 45)]
    [InlineData("Payer facture", "Rappel 21h05\nautre ligne", 21, 5)]
    public void TimeWrittenInTitleOrNotes_IsUsed(string? title, string? notes, int h, int m)
    {
        var r = TaskTimeResolver.Resolve("2026-11-20T00:00:00.000Z", title, notes, "09:00", Paris)!.Value;

        Assert.True(r.IsExplicit);
        Assert.Equal("text", r.Origin);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, h, m, 0, TimeSpan.FromHours(1)), r.When);
    }

    [Theory]
    [InlineData("Faire 2h de sport")]      // durée, pas une heure
    [InlineData("Lire 30 pages")]
    [InlineData("Acheter 3 x 25:99")]      // 25h n'existe pas
    public void AmbiguousText_FallsBackToDefault(string title)
    {
        var r = TaskTimeResolver.Resolve("2026-11-20T00:00:00.000Z", title, null, "09:00", Paris)!.Value;

        Assert.False(r.IsExplicit);
        Assert.Equal(9, r.When.Hour);
    }

    [Fact]
    public void PrefixedTime_WinsOverPlainTime()
    {
        var t = TaskTimeResolver.FindTime("note 10:00", "⏰ 16h");
        Assert.Equal(new TimeOnly(16, 0), t);
    }

    [Fact]
    public void RealTimeFromApi_IsUsed_IfGoogleEverProvidesIt()
    {
        var r = TaskTimeResolver.Resolve("2026-11-20T13:30:00.000Z", "x", "⏰ 8h", "09:00", Paris)!.Value;

        Assert.True(r.IsExplicit);
        Assert.Equal("api", r.Origin);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 14, 30, 0, TimeSpan.FromHours(1)), r.When);
    }

    [Fact]
    public void NoDue_ReturnsNull()
    {
        Assert.Null(TaskTimeResolver.Resolve(null, "x", null, "09:00", Paris));
    }
}
