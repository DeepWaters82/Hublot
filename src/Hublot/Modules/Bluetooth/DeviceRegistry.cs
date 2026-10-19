using Hublot.Common;

namespace Hublot.Modules.Bluetooth;

/// <summary>
/// Appareils déjà vus (devices.tsv) : détection d'un « nouvel appareil », type connu dès la
/// connexion (avant même la lecture WinRT), liste des appareils de l'utilitaire de paramètres.
/// Thread UI uniquement.
/// </summary>
internal sealed class DeviceRegistry
{
    private readonly Dictionary<string, KnownDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;

    public DeviceRegistry()
    {
        // L'état « connecté » enregistré date de la session précédente.
        foreach (var d in DeviceStore.LoadDevices()) _devices[d.Key] = d with { Connected = false };
        _dirty = _devices.Count > 0;
    }

    public bool IsKnown(string key) => _devices.ContainsKey(key);

    /// <summary>Type mémorisé pour un appareil dont le type n'est pas encore résolu.</summary>
    public DeviceKind KindOf(string key)
        => _devices.TryGetValue(key, out var d) && Enum.TryParse<DeviceKind>(d.Kind, out var kind) ? kind : DeviceKind.Generic;

    /// <summary>Enregistre l'état ; vrai si l'appareil n'avait jamais été vu.</summary>
    public bool Observe(DeviceState s)
    {
        var now = DateTime.Now;
        if (!_devices.TryGetValue(s.Key, out var known))
        {
            _devices[s.Key] = new KnownDevice(s.Key, s.Name, s.Kind.ToString(), s.Link, s.Battery, s.Connected, now, now);
            _dirty = true;
            return true;
        }
        var updated = known with
        {
            Name = s.Name,
            Kind = s.Kind is DeviceKind.Generic or DeviceKind.Dongle && known.Kind != nameof(DeviceKind.Generic) ? known.Kind : s.Kind.ToString(),
            Link = s.Link,
            Battery = s.Battery ?? known.Battery,
            Connected = s.Connected,
            LastSeen = s.Connected || known.Connected ? now : known.LastSeen,
        };
        if (updated != known)
        {
            _devices[s.Key] = updated;
            _dirty = true;
        }
        return false;
    }

    private static bool IsBluetooth(string key) => key.StartsWith("bt:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Appareil Bluetooth appairé de nouveau (supprimé puis remis dans Windows) : il change de conteneur, donc de
    /// clé. L'entrée de l'ancien appairage — même nom, plus appairée — devient la sienne (type, première
    /// apparition conservés) au lieu de rester en double. Renvoie l'ancienne clé (sourdine à reporter), ou null.
    /// </summary>
    public string? Supersede(DeviceState s, ISet<string> paired)
    {
        if (!IsBluetooth(s.Key) || _devices.ContainsKey(s.Key)) return null;
        var stale = _devices.Values.FirstOrDefault(d => IsBluetooth(d.Key) && !paired.Contains(d.Key)
            && string.Equals(d.Name, s.Name, StringComparison.OrdinalIgnoreCase));
        if (stale is null) return null;
        _devices.Remove(stale.Key);
        _devices[s.Key] = stale with { Key = s.Key, Connected = s.Connected, LastSeen = DateTime.Now };
        _dirty = true;
        return stale.Key;
    }

    /// <summary>
    /// Doublons déjà enregistrés : entrées Bluetooth plus appairées dont le nom est celui d'un appareil appairé.
    /// Renvoie (ancienne clé, clé actuelle) pour reporter la sourdine.
    /// </summary>
    public List<(string Old, string Current)> PruneStale(IReadOnlyCollection<DeviceState> paired)
    {
        var keys = paired.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paired.Where(p => IsBluetooth(p.Key))) byName.TryAdd(p.Name, p.Key);
        var stale = _devices.Values
            .Where(d => IsBluetooth(d.Key) && !keys.Contains(d.Key) && byName.ContainsKey(d.Name))
            .Select(d => (d.Key, byName[d.Name]))
            .ToList();
        foreach (var (old, _) in stale) _devices.Remove(old);
        if (stale.Count > 0) _dirty = true;
        return stale;
    }

    /// <summary>Derniers appareils vus, déconnectés, hors clés et noms exclus (déjà affichés).</summary>
    public IEnumerable<DeviceState> RecentlyDisconnected(int count, ISet<string> excluded)
    {
        return _devices.Values
            .Where(d => !d.Connected && !excluded.Contains(d.Key) && !excluded.Contains(d.Name))
            .OrderByDescending(d => d.LastSeen)
            .Take(count)
            .Select(d => DeviceState.Disconnected(d.Key, d.Name,
                Enum.TryParse<DeviceKind>(d.Kind, out var kind) ? kind : DeviceKind.Generic, d.Battery, d.Link));
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            DeviceStore.SaveDevices(_devices.Values.OrderByDescending(d => d.LastSeen));
            _dirty = false;
        }
        catch (Exception ex)
        {
            Log.Error("Enregistrement des appareils", ex);
        }
    }
}
