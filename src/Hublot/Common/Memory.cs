using Hublot.Interop;

namespace Hublot.Common;

internal static class Memory
{
    /// <summary>
    /// Au repos : compacte le tas géré, rend la mémoire libre au système et vide l'ensemble de travail.
    /// Les pages réellement utiles reviennent d'elles-mêmes au prochain événement.
    /// </summary>
    public static void Trim()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Kernel32.SetProcessWorkingSetSizeEx(Kernel32.GetCurrentProcess(), -1, -1, 0);
    }
}
