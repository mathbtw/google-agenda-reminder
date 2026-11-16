using System;
using System.IO;
using AgendaReminder.Core;

namespace AgendaReminder.App.Services;

internal static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} : {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                var info = new FileInfo(AppPaths.Log);
                if (info.Exists && info.Length > 1_000_000)
                    File.Move(AppPaths.Log, AppPaths.Log + ".old", overwrite: true);
                File.AppendAllText(AppPaths.Log, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Le log ne doit jamais faire planter l'app.
        }
    }
}
