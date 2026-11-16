using System.Text;
using AgendaReminder.Core.Google;
using AgendaReminder.Core.Scheduling;
using Xunit;

namespace AgendaReminder.Core.Tests;

public class GoogleAccountClientTests
{
    [Fact]
    public void ComposeNotes_PutsTimeMarkerFirst()
    {
        Assert.Equal("⏰ 14:30\nApporter le dossier", GoogleAccountClient.ComposeNotes("Apporter le dossier", new TimeOnly(14, 30)));
        Assert.Equal("⏰ 09:05", GoogleAccountClient.ComposeNotes(null, new TimeOnly(9, 5)));
        Assert.Equal("Juste une note", GoogleAccountClient.ComposeNotes("  Juste une note ", null));
        Assert.Null(GoogleAccountClient.ComposeNotes("  ", null));
    }

    [Fact]
    public void TaskCreatedWithTime_IsReadBackAtThatTime()
    {
        // Une tâche créée depuis l'app avec une heure doit être rappelée à cette heure-là.
        var notes = GoogleAccountClient.ComposeNotes("Note 10:00 sans importance", new TimeOnly(17, 15));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris");

        var r = TaskTimeResolver.Resolve("2026-11-20T00:00:00.000Z", "Rendre le rapport", notes, "09:00", zone)!.Value;

        Assert.Equal(17, r.When.Hour);
        Assert.Equal(15, r.When.Minute);
    }

    [Fact]
    public void EmailFromIdToken_ReadsPayload()
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{B64("{\"alg\":\"RS256\"}")}.{B64("{\"email\":\"jean.dupont@example.com\",\"sub\":\"1\"}")}.sig";

        Assert.Equal("jean.dupont@example.com", GoogleAccountClient.EmailFromIdToken(token));
        Assert.Null(GoogleAccountClient.EmailFromIdToken("pas-un-jwt"));
        Assert.Null(GoogleAccountClient.EmailFromIdToken(null));
    }
}
