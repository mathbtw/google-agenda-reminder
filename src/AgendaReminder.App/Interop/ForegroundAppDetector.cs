using System;
using System.Diagnostics;
using AgendaReminder.Core.Policy;

namespace AgendaReminder.App.Interop;

/// <summary>Détecte l'application au premier plan et si Windows est en mode plein écran / présentation.</summary>
internal static class ForegroundAppDetector
{
    private static readonly int OwnProcessId = Environment.ProcessId;

    public static ForegroundInfo Get()
    {
        string? processName = null;
        var ownWindow = false;

        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == OwnProcessId)
                {
                    ownWindow = true;
                }
                else if (pid != 0)
                {
                    using var process = Process.GetProcessById((int)pid);
                    processName = process.ProcessName + ".exe";
                }
            }
        }
        catch (Exception)
        {
            // Processus terminé entre-temps ou protégé : on considère "inconnu".
        }

        // Notre propre fenêtre plein écran ne doit pas déclencher l'auto-détection.
        var busy = !ownWindow && IsFullscreenOrBusy();
        return new ForegroundInfo(processName, busy);
    }

    public static bool IsFullscreenOrBusy()
    {
        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out var state) != 0)
                return false;
            return state is NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                or NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                or NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE
                or NativeMethods.QUERY_USER_NOTIFICATION_STATE.QUNS_APP;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
