using System.Runtime.InteropServices;

namespace Hublot.Modules.Usb;

/// <summary>Volumes montés : lettres de lecteur, nom, espace total / libre (API Win32 des volumes).</summary>
internal static unsafe partial class Volumes
{
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetVolumeNameForVolumeMountPointW(string mountPoint, char* volumeName, uint length);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetVolumePathNamesForVolumeNameW(string volumeName, char* paths, uint length, uint* returned);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetDiskFreeSpaceExW(string directory, ulong* freeToCaller, ulong* total, ulong* totalFree);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetVolumeInformationW(string root, char* name, uint nameLength, uint* serial, uint* maxComponent,
        uint* flags, char* fileSystem, uint fileSystemLength);

    /// <summary>
    /// Lettres de lecteur d'une interface de volume (« \?\STORAGE#Volume#… »), ou vide si le volume
    /// n'est pas (encore) monté.
    /// </summary>
    public static List<string> RootsOf(string volumeInterface)
    {
        var roots = new List<string>();
        var name = stackalloc char[64];
        if (GetVolumeNameForVolumeMountPointW(volumeInterface.TrimEnd('\\') + "\\", name, 64) == 0) return roots;
        string volume = new(name);
        var buffer = stackalloc char[512];
        uint returned;
        if (GetVolumePathNamesForVolumeNameW(volume, buffer, 512, &returned) == 0) return roots;
        // Liste de chaînes terminées par un zéro, la dernière vide.
        int start = 0;
        for (int i = 0; i < returned; i++)
        {
            if (buffer[i] != '\0') continue;
            if (i > start) roots.Add(new string(buffer, start, i - start));
            start = i + 1;
        }
        return roots;
    }

    /// <summary>Espace total / libre et nom d'un volume monté (« E:\ »).</summary>
    public static (ulong Total, ulong Free, string Label)? Describe(string root)
    {
        ulong free, total, totalFree;
        if (GetDiskFreeSpaceExW(root, &free, &total, &totalFree) == 0) return null;
        var label = stackalloc char[261];
        uint serial, max, flags;
        string name = GetVolumeInformationW(root, label, 261, &serial, &max, &flags, null, 0) != 0 ? new string(label) : "";
        return (total, totalFree, name);
    }
}
