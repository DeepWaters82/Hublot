using System.Runtime.InteropServices;

namespace Hublot.Interop;

/// <summary>
/// Maintien de l’éveil (module Caféine) : SetThreadExecutionState, la méthode de PowerToys Caféine. L’état vaut pour
/// le thread appelant (toujours le thread UI) jusqu’au prochain appel ; Windows l’annule si le processus se termine.
/// </summary>
internal static partial class Kernel32
{
    public const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001, ES_DISPLAY_REQUIRED = 0x00000002;

    /// <summary>Renvoie l’état précédent, ou 0 en cas d’échec.</summary>
    [LibraryImport("kernel32.dll")]
    public static partial uint SetThreadExecutionState(uint flags);
}

internal static unsafe partial class User32
{
    /// <summary>Entrée de menu qui ouvre un sous-menu : l’identifiant est le handle du sous-menu.</summary>
    public const uint MF_POPUP = 0x10;
}
