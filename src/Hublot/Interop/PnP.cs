using System.Runtime.InteropServices;

namespace Hublot.Interop;

/// <summary>Gestionnaire de configuration (cfgmgr32) : état d'un nœud de périphérique.</summary>
internal static partial class PnP
{
    private const uint CR_SUCCESS = 0, DN_HAS_PROBLEM = 0x400, CM_PROB_HELD_FOR_EJECT = 47;

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

    /// <summary>
    /// Périphérique « retiré en toute sécurité » mais encore branché : le nœud reste présent, arrêté avec
    /// le problème 47 (CM_PROB_HELD_FOR_EJECT) jusqu'au débranchement physique. Débranché, il n'est plus
    /// présent du tout (CM_Locate_DevNode échoue en mode normal).
    /// </summary>
    public static bool IsHeldForEject(string instanceId)
    {
        if (CM_Locate_DevNode(out uint node, instanceId, 0) != CR_SUCCESS) return false;
        return CM_Get_DevNode_Status(out uint status, out uint problem, node, 0) == CR_SUCCESS
            && (status & DN_HAS_PROBLEM) != 0 && problem == CM_PROB_HELD_FOR_EJECT;
    }
}
