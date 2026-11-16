using AgendaReminder.Core.Scheduling;
using AgendaReminder.Core.Web;
using Xunit;

namespace AgendaReminder.Core.Tests;

public class WebTaskTimeMatcherTests
{
    private static readonly DateOnly Day = new(2026, 11, 20);

    private static TaskToMatch T(string id, string title) => new(id, title, Day);

    [Theory]
    [InlineData("14:30, Envoyer le dossier, Tâche, Non terminée, 20 novembre 2026", 14, 30)]
    [InlineData("Tâche : Envoyer le dossier, 14 h 30", 14, 30)]
    [InlineData("Envoyer le dossier\n14:30", 14, 30)]
    [InlineData("2:30pm, Envoyer le dossier, Task, Not completed, November 20, 2026", 14, 30)]
    [InlineData("Task: Envoyer le dossier, 9am", 9, 0)]
    [InlineData("14 h, Envoyer le dossier, Tâche", 14, 0)]
    public void FindsTime_InVariousLabelFormats(string snippet, int h, int m)
    {
        var r = WebTaskTimeMatcher.Match(new[] { snippet }, new[] { T("a", "Envoyer le dossier") });
        Assert.Equal(new TimeOnly(h, m), r["a"]);
    }

    [Fact]
    public void AllDayTask_IsNotMatched()
    {
        var r = WebTaskTimeMatcher.Match(new[] { "Toute la journée, Envoyer le dossier, Tâche" }, new[] { T("a", "Envoyer le dossier") });
        Assert.Empty(r);
    }

    [Fact]
    public void ConflictingTimes_AreAmbiguous_AndSkipped()
    {
        var snippets = new[] { "10:00, Sport, Tâche", "18:00, Sport, Tâche" };
        Assert.Empty(WebTaskTimeMatcher.Match(snippets, new[] { T("a", "Sport") }));
    }

    [Fact]
    public void TaskLabel_WinsOverEventWithSameTitle()
    {
        var snippets = new[] { "09:00 – 10:00, Dentiste, Évènement", "16:15, Dentiste, Tâche" };
        var r = WebTaskTimeMatcher.Match(snippets, new[] { T("a", "Dentiste") });
        Assert.Equal(new TimeOnly(16, 15), r["a"]);
    }

    [Fact]
    public void DuplicateTitlesSameDay_AreSkipped()
    {
        var r = WebTaskTimeMatcher.Match(new[] { "10:00, Sport, Tâche" }, new[] { T("a", "Sport"), T("b", "Sport") });
        Assert.Empty(r);
    }

    [Fact]
    public void ShorterTitle_DoesNotStealLongerTitlesTime()
    {
        var snippets = new[] { "10:00, Appeler Paul, Tâche", "15:00, Appeler, Tâche" };
        var r = WebTaskTimeMatcher.Match(snippets, new[] { T("a", "Appeler"), T("b", "Appeler Paul") });
        Assert.Equal(new TimeOnly(15, 0), r["a"]);
        Assert.Equal(new TimeOnly(10, 0), r["b"]);
    }

    [Fact]
    public void TimeInsideTitle_IsNotMistakenForDueTime()
    {
        var r = WebTaskTimeMatcher.Match(new[] { "Réunion 10:45, Tâche, toute la journée" }, new[] { T("a", "Réunion 10:45") });
        Assert.Empty(r);
    }

    [Fact]
    public void AccentsAndCase_AreTolerated()
    {
        var r = WebTaskTimeMatcher.Match(new[] { "08:05, ACHETER DU PAIN, tache" }, new[] { T("a", "Acheter du pain") });
        Assert.Equal(new TimeOnly(8, 5), r["a"]);
    }

    [Fact]
    public void WordBoundaries_AreRespected()
    {
        var r = WebTaskTimeMatcher.Match(new[] { "10:00, Sportif, Tâche" }, new[] { T("a", "Sport") });
        Assert.Empty(r);
    }

    [Fact]
    public void Resolver_UsesWebTime_BeforeTextMarker()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris");
        var r = TaskTimeResolver.Resolve("2026-11-20T00:00:00.000Z", "x", "⏰ 8h", "09:00", zone, new TimeOnly(17, 45))!.Value;
        Assert.Equal("web", r.Origin);
        Assert.Equal(17, r.When.Hour);
        Assert.Equal(45, r.When.Minute);
    }
}
