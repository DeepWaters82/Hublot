using System.Runtime.InteropServices;

namespace Hublot.Interop;

/// <summary>
/// Points de terminaison audio (MMDevice API) : abonnement aux changements d'état, nom, sens et adaptateur.
/// COM appelé par la table de méthodes ; l'objet de rappel IMMNotificationClient est construit à la main
/// (table de pointeurs de fonction [UnmanagedCallersOnly]) : pas de génération de code, compatible trimming.
/// Les rappels arrivent sur des threads du service audio : ils ne font que transmettre.
/// </summary>
internal static unsafe partial class CoreAudio
{
    public const uint StateActive = 1, StateUnplugged = 4;
    private const uint ClsctxAll = 0x17, CoinitMultithreaded = 0, StateMaskAll = 0xF;
    private const int DataFlowAll = 2;
    private const ushort VtLpwstr = 31;

    private static readonly Guid ClsidEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IidNotificationClient = new("7991EEC9-7E89-4D85-8390-6C703CEC60C0");
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidEndpoint = new("1BE09788-6894-4089-8586-9A2A6C265AC5");

    /// <summary>PKEY_Device_DeviceDesc (2 : « Casque », « Haut-parleurs ») et PKEY_Device_FriendlyName (14).</summary>
    private static readonly Guid DeviceFormat = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    /// <summary>PKEY_DeviceInterface_FriendlyName : l'adaptateur (« Realtek(R) Audio »).</summary>
    private static readonly Guid InterfaceFormat = new("026e516e-b814-414b-83cd-856d6fef4822");
    /// <summary>Chemin PnP de l'adaptateur : « {1}.HDAUDIO… », « {1}.USB… », « {1}.BTHENUM… ».</summary>
    private static readonly Guid AdapterFormat = new("b3f8fa53-0004-438e-9003-51a46e139bfc");

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coinit);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* instance);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(void* value);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint memory);

    /// <summary>Point de terminaison : description, adaptateur, sortie (vrai) ou entrée, chemin PnP de l'adaptateur.</summary>
    public readonly record struct Endpoint(string Name, string Adapter, bool Render, string Bus);

    private static Action<string, uint>? s_stateChanged;
    private static nint s_enumerator, s_client;

    private static nint* Methods(nint unknown) => *(nint**)unknown;

    private static void Release(nint unknown) => ((delegate* unmanaged[Stdcall]<nint, uint>)Methods(unknown)[2])(unknown);

    /// <summary>Abonnement aux changements d'état (à appeler hors du thread UI : COM multithread) ; faux si l'API est indisponible.</summary>
    public static bool Subscribe(Action<string, uint> stateChanged)
    {
        s_stateChanged = stateChanged;
        if (s_enumerator != 0) return true;
        CoInitializeEx(0, CoinitMultithreaded); // déjà initialisé : sans effet
        nint enumerator;
        var clsid = ClsidEnumerator;
        var iid = IidEnumerator;
        if (CoCreateInstance(&clsid, 0, ClsctxAll, &iid, &enumerator) != 0 || enumerator == 0) return false;
        if (s_client == 0) s_client = CreateClient();
        // IMMDeviceEnumerator::RegisterEndpointNotificationCallback (méthode 6)
        if (((delegate* unmanaged[Stdcall]<nint, nint, int>)Methods(enumerator)[6])(enumerator, s_client) != 0)
        {
            Release(enumerator);
            return false;
        }
        s_enumerator = enumerator;
        return true;
    }

    public static void Unsubscribe()
    {
        s_stateChanged = null;
        var enumerator = s_enumerator;
        if (enumerator == 0) return;
        s_enumerator = 0;
        // IMMDeviceEnumerator::UnregisterEndpointNotificationCallback (méthode 7). L'objet de rappel reste alloué :
        // il est réutilisé au prochain abonnement.
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)Methods(enumerator)[7])(enumerator, s_client);
        Release(enumerator);
    }

    /// <summary>État de chaque point de terminaison (actif, débranché, désactivé, absent), par identifiant.</summary>
    public static Dictionary<string, uint> States()
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var enumerator = s_enumerator;
        if (enumerator == 0) return result;
        nint collection;
        // IMMDeviceEnumerator::EnumAudioEndpoints (méthode 3)
        if (((delegate* unmanaged[Stdcall]<nint, int, uint, nint*, int>)Methods(enumerator)[3])(enumerator, DataFlowAll, StateMaskAll, &collection) != 0) return result;
        try
        {
            uint count;
            // IMMDeviceCollection::GetCount (3), Item (4)
            if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Methods(collection)[3])(collection, &count) != 0) return result;
            for (uint i = 0; i < count; i++)
            {
                nint device;
                if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Methods(collection)[4])(collection, i, &device) != 0) continue;
                try
                {
                    char* id;
                    uint state;
                    // IMMDevice::GetId (5), GetState (6)
                    if (((delegate* unmanaged[Stdcall]<nint, char**, int>)Methods(device)[5])(device, &id) != 0) continue;
                    string key = new(id);
                    CoTaskMemFree((nint)id);
                    if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Methods(device)[6])(device, &state) == 0) result[key] = state;
                }
                finally
                {
                    Release(device);
                }
            }
        }
        finally
        {
            Release(collection);
        }
        return result;
    }

    /// <summary>Description d'un point de terminaison, ou null s'il a disparu.</summary>
    public static Endpoint? Describe(string id)
    {
        var enumerator = s_enumerator;
        if (enumerator == 0) return null;
        nint device;
        fixed (char* p = id)
        {
            // IMMDeviceEnumerator::GetDevice (méthode 5)
            if (((delegate* unmanaged[Stdcall]<nint, char*, nint*, int>)Methods(enumerator)[5])(enumerator, p, &device) != 0) return null;
        }
        try
        {
            bool render = true;
            nint endpoint;
            var iid = IidEndpoint;
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Methods(device)[0])(device, &iid, &endpoint) == 0)
            {
                int flow;
                // IMMEndpoint::GetDataFlow (méthode 3) : 0 = sortie, 1 = entrée
                if (((delegate* unmanaged[Stdcall]<nint, int*, int>)Methods(endpoint)[3])(endpoint, &flow) == 0) render = flow == 0;
                Release(endpoint);
            }
            nint store;
            // IMMDevice::OpenPropertyStore (méthode 4), STGM_READ = 0
            if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Methods(device)[4])(device, 0, &store) != 0) return null;
            try
            {
                return new Endpoint(
                    ReadString(store, DeviceFormat, 2) ?? ReadString(store, DeviceFormat, 14) ?? "",
                    ReadString(store, InterfaceFormat, 2) ?? "",
                    render,
                    ReadString(store, AdapterFormat, 2) ?? "");
            }
            finally
            {
                Release(store);
            }
        }
        finally
        {
            Release(device);
        }
    }

    private static string? ReadString(nint store, Guid format, uint pid)
    {
        // PROPERTYKEY { GUID fmtid; DWORD pid; } ; PROPVARIANT : 24 octets en 64 bits, valeur à l'offset 8.
        var key = stackalloc byte[20];
        *(Guid*)key = format;
        *(uint*)(key + 16) = pid;
        var value = stackalloc byte[24];
        new Span<byte>(value, 24).Clear();
        // IPropertyStore::GetValue (méthode 5)
        if (((delegate* unmanaged[Stdcall]<nint, void*, void*, int>)Methods(store)[5])(store, key, value) != 0) return null;
        string? text = *(ushort*)value == VtLpwstr && *(nint*)(value + 8) != 0 ? new string(*(char**)(value + 8)) : null;
        PropVariantClear(value);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    // ── Objet de rappel IMMNotificationClient ──────────────────────────────────────────────

    private static nint CreateClient()
    {
        var vtable = (nint*)NativeMemory.Alloc((nuint)(8 * sizeof(nint)));
        vtable[0] = (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QueryInterface;
        vtable[1] = (nint)(delegate* unmanaged<nint, uint>)&AddRef;
        vtable[2] = (nint)(delegate* unmanaged<nint, uint>)&ReleaseClient;
        vtable[3] = (nint)(delegate* unmanaged<nint, char*, uint, int>)&OnDeviceStateChanged;
        vtable[4] = (nint)(delegate* unmanaged<nint, char*, int>)&OnDeviceAddedOrRemoved;
        vtable[5] = (nint)(delegate* unmanaged<nint, char*, int>)&OnDeviceAddedOrRemoved;
        vtable[6] = (nint)(delegate* unmanaged<nint, int, int, char*, int>)&OnDefaultDeviceChanged;
        // PROPERTYKEY (20 octets) passé par valeur : par adresse en x64 et ARM64.
        vtable[7] = (nint)(delegate* unmanaged<nint, char*, nint, int>)&OnPropertyValueChanged;
        var client = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
        *client = (nint)vtable;
        return (nint)client;
    }

    [UnmanagedCallersOnly]
    private static int QueryInterface(nint self, Guid* iid, nint* result)
    {
        if (*iid == IidUnknown || *iid == IidNotificationClient)
        {
            *result = self;
            return 0;
        }
        *result = 0;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    // Objet statique, jamais libéré : le compte de références est sans objet.
    [UnmanagedCallersOnly]
    private static uint AddRef(nint self) => 2;

    [UnmanagedCallersOnly]
    private static uint ReleaseClient(nint self) => 1;

    [UnmanagedCallersOnly]
    private static int OnDeviceStateChanged(nint self, char* id, uint state)
    {
        try
        {
            if (id != null) s_stateChanged?.Invoke(new string(id), state);
        }
        catch (Exception)
        {
            // Aucune exception ne doit remonter dans le service audio.
        }
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnDeviceAddedOrRemoved(nint self, char* id) => 0;

    [UnmanagedCallersOnly]
    private static int OnDefaultDeviceChanged(nint self, int flow, int role, char* id) => 0;

    [UnmanagedCallersOnly]
    private static int OnPropertyValueChanged(nint self, char* id, nint key) => 0;
}
