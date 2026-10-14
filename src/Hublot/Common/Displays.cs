using System.Runtime.InteropServices;

namespace Hublot.Common;

/// <summary>Écran actif, tel que Windows le décrit (Paramètres › Affichage).</summary>
/// <param name="Path">Identifiant stable du moniteur (chemin de périphérique) : suit l'écran d'un branchement à l'autre.</param>
/// <param name="GdiName">Nom GDI de la sortie (DISPLAY1, DISPLAY2…) : sert à retrouver le moniteur pour placer un popup.</param>
/// <param name="Name">Nom commercial lu dans l'EDID (« DELL U2720Q »), ou « Écran intégré ».</param>
/// <param name="Connector">Connectique : HDMI, DisplayPort, VGA, DVI, USB-C / station, sans fil, intégré…</param>
public sealed record DisplayInfo(string Path, string GdiName, string Name, string Connector, int Width, int Height,
    int RefreshHz, bool Internal, int Left, int Top)
{
    /// <summary>« 2560 × 1440 · 60 Hz ».</summary>
    public string Mode => RefreshHz > 0 ? $"{Width} × {Height} · {RefreshHz} Hz" : $"{Width} × {Height}";
}

/// <summary>
/// Écrans actifs via l'API Display Configuration (QueryDisplayConfig) : la seule qui donne le nom réel
/// de l'écran et le type de câble. Lectures par décalages dans des tampons non managés (structures
/// Windows documentées) : aucun marshalling particulier, compilé tel quel dans le service et dans Hublot Core.
/// </summary>
public static class Displays
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int PathSize = 72, ModeSize = 64;
    private const int GET_SOURCE_NAME = 1, GET_TARGET_NAME = 2, SourceNameSize = 84, TargetNameSize = 420;
    private const uint ModeTypeSource = 1;
    private const uint InvalidIndex = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr paths, ref uint modeCount, IntPtr modes, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(IntPtr request);

    /// <summary>Écrans actifs, de gauche à droite ; liste vide en cas d'échec.</summary>
    public static List<DisplayInfo> Active()
    {
        var list = new List<DisplayInfo>();
        IntPtr paths = IntPtr.Zero, modes = IntPtr.Zero;
        try
        {
            int status;
            uint pathCount, modeCount;
            do
            {
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0) return list;
                Marshal.FreeHGlobal(paths);
                Marshal.FreeHGlobal(modes);
                paths = Marshal.AllocHGlobal((int)(pathCount * PathSize));
                modes = Marshal.AllocHGlobal((int)(modeCount * ModeSize));
                status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            }
            while (status == 122); // ERROR_INSUFFICIENT_BUFFER : la configuration a changé entre-temps
            if (status != 0) return list;

            for (int i = 0; i < pathCount; i++)
            {
                IntPtr path = paths + i * PathSize;
                // sourceInfo : LUID (8), id (4), modeInfoIdx (4), statusFlags (4) ; targetInfo à +20.
                long sourceAdapter = Marshal.ReadInt64(path, 0);
                int sourceId = Marshal.ReadInt32(path, 8);
                uint sourceMode = (uint)Marshal.ReadInt32(path, 12);
                long targetAdapter = Marshal.ReadInt64(path, 20);
                int targetId = Marshal.ReadInt32(path, 28);
                uint technology = (uint)Marshal.ReadInt32(path, 36);
                uint refreshNum = (uint)Marshal.ReadInt32(path, 48), refreshDen = (uint)Marshal.ReadInt32(path, 52);

                int width = 0, height = 0, left = 0, top = 0;
                if (sourceMode != InvalidIndex && sourceMode < modeCount)
                {
                    IntPtr mode = modes + (int)sourceMode * ModeSize;
                    if ((uint)Marshal.ReadInt32(mode, 0) == ModeTypeSource)
                    {
                        // sourceMode à +16 : width, height, pixelFormat, position (x, y).
                        width = Marshal.ReadInt32(mode, 16);
                        height = Marshal.ReadInt32(mode, 20);
                        left = Marshal.ReadInt32(mode, 28);
                        top = Marshal.ReadInt32(mode, 32);
                    }
                }

                var (name, devicePath, technologyFromName) = TargetName(targetAdapter, targetId);
                if (technologyFromName != InvalidIndex) technology = technologyFromName;
                bool isInternal = technology is 6 or 11 or 13 or 0x80000000;
                int hz = refreshDen != 0 ? (int)Math.Round((double)refreshNum / refreshDen) : 0;
                list.Add(new DisplayInfo(
                    devicePath.Length > 0 ? devicePath : $"{targetAdapter:X}:{targetId}",
                    SourceName(sourceAdapter, sourceId),
                    name.Length > 0 ? name : L.T(isInternal ? "Display.Internal" : "Display.Generic"),
                    Connector(technology), width, height, hz, isInternal, left, top));
            }
        }
        catch (Exception)
        {
            // API indisponible (session distante…) : pas d'écran connu plutôt qu'une erreur.
        }
        finally
        {
            Marshal.FreeHGlobal(paths);
            Marshal.FreeHGlobal(modes);
        }
        return list.OrderBy(d => d.Left).ThenBy(d => d.Top).ToList();
    }

    /// <summary>Nom commercial, chemin du moniteur et connectique (DISPLAYCONFIG_TARGET_DEVICE_NAME).</summary>
    private static (string Name, string Path, uint Technology) TargetName(long adapter, int id)
    {
        IntPtr buffer = Marshal.AllocHGlobal(TargetNameSize);
        try
        {
            Clear(buffer, TargetNameSize);
            WriteHeader(buffer, GET_TARGET_NAME, TargetNameSize, adapter, id);
            if (DisplayConfigGetDeviceInfo(buffer) != 0) return ("", "", InvalidIndex);
            // en-tête (20), flags (4), outputTechnology (4), EDID (2 + 2), connectorInstance (4), nom (64 car.), chemin (128 car.)
            uint technology = (uint)Marshal.ReadInt32(buffer, 24);
            string name = (Marshal.PtrToStringUni(buffer + 36, 64) ?? "").TrimEnd('\0');
            string path = (Marshal.PtrToStringUni(buffer + 164, 128) ?? "").TrimEnd('\0');
            return (name, path, technology);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Nom GDI de la source (DISPLAYCONFIG_SOURCE_DEVICE_NAME), celui de MONITORINFOEX.szDevice.</summary>
    private static string SourceName(long adapter, int id)
    {
        IntPtr buffer = Marshal.AllocHGlobal(SourceNameSize);
        try
        {
            Clear(buffer, SourceNameSize);
            WriteHeader(buffer, GET_SOURCE_NAME, SourceNameSize, adapter, id);
            return DisplayConfigGetDeviceInfo(buffer) == 0 ? (Marshal.PtrToStringUni(buffer + 20, 32) ?? "").TrimEnd('\0') : "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void WriteHeader(IntPtr buffer, int type, int size, long adapter, int id)
    {
        Marshal.WriteInt32(buffer, 0, type);
        Marshal.WriteInt32(buffer, 4, size);
        Marshal.WriteInt64(buffer, 8, adapter);
        Marshal.WriteInt32(buffer, 16, id);
    }

    private static void Clear(IntPtr buffer, int size)
    {
        for (int i = 0; i < size; i += 4) Marshal.WriteInt32(buffer, i, 0);
    }

    /// <summary>DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY → libellé.</summary>
    private static string Connector(uint technology) => technology switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        6 or 11 or 13 or 0x80000000 => L.T("Display.Builtin"),
        10 => "DisplayPort",
        12 => "UDI",
        15 => L.T("Display.Wireless"),
        16 => L.T("Display.Dock"),
        17 => L.T("Display.Virtual"),
        _ => L.T("Display.Generic"),
    };
}
