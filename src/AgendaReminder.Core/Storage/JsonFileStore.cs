using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgendaReminder.Core.Storage;

/// <summary>Lecture/écriture atomique d'un objet en JSON.</summary>
public static class JsonFileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T LoadOrCreate<T>(string path, Func<T> factory, bool writeIfMissing = true)
    {
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var value = JsonSerializer.Deserialize<T>(json, Options);
                if (value is not null)
                    return value;
            }
            catch (JsonException)
            {
                // Fichier corrompu : on le garde de côté et on repart des valeurs par défaut.
                File.Copy(path, path + ".bak", overwrite: true);
            }
        }

        var created = factory();
        if (writeIfMissing)
            Save(path, created);
        return created;
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
