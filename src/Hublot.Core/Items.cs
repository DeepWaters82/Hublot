using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Hublot.Common;

namespace HublotCore;

/// <summary>Glyphes Segoe Fluent Icons, mêmes correspondances que le service.</summary>
internal static class KindGlyphs
{
    public static string For(string kind) => kind switch
    {
        "Headphones" => "",
        "Headset" => "",
        "Speaker" => "",
        "Keyboard" => "",
        "Mouse" => "",
        "Gamepad" => "",
        "Phone" => "",
        "Computer" => "",
        "Pen" => "",
        "Presenter" => "",
        "Dongle" => "",
        _ => "",
    };

    public static string Ago(DateTime t)
    {
        var span = DateTime.Now - t;
        if (span.TotalMinutes < 1) return L.T("Ago.Now");
        if (span.TotalHours < 1) return L.F("Ago.Minutes", (int)span.TotalMinutes);
        string time = t.ToString(L.T("Ago.TimeFormat"), L.Culture);
        if (t.Date == DateTime.Today) return L.F("Ago.Today", time);
        if (t.Date == DateTime.Today.AddDays(-1)) return L.F("Ago.Yesterday", time);
        return t.ToString(L.T("Ago.DateFormat"), L.Culture);
    }
}

/// <summary>Appareil connu, avec son interrupteur « notifications » (sourdine).</summary>
public sealed class DeviceItem(KnownDevice device, bool notify, Action<string, bool> setNotify) : INotifyPropertyChanged
{
    private bool _notify = notify;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => device.Name;
    public string Glyph => KindGlyphs.For(device.Kind);

    public string Details
    {
        get
        {
            string state = device.Connected ? L.T("Device.Connected") : L.F("Device.Seen", KindGlyphs.Ago(device.LastSeen));
            return device.Battery is int b ? L.F("Device.WithBattery", state, L.Percent(b)) : state;
        }
    }

    public bool Notify
    {
        get => _notify;
        set
        {
            _notify = value;
            setNotify(device.Key, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notify)));
        }
    }
}

/// <summary>Raccourci clavier global : texte affiché à partir des modificateurs RegisterHotKey et du code de touche.</summary>
internal static class Hotkeys
{
    public static string Describe(int modifiers, int key)
    {
        if (key == 0) return L.T("Key.None");
        var parts = new List<string>();
        if ((modifiers & Settings.ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & Settings.ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & Settings.ModShift) != 0) parts.Add(L.T("Key.Shift"));
        if ((modifiers & Settings.ModWin) != 0) parts.Add("Win");
        var k = KeyInterop.KeyFromVirtualKey(key);
        parts.Add(k switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => L.F("Key.Numpad", k - Key.NumPad0),
            Key.Space => L.T("Key.Space"),
            _ => k.ToString(),
        });
        return string.Join(" + ", parts);
    }
}

/// <summary>
/// Application média déjà vue, avec son mode : 0 = par défaut (suit « Musique uniquement »),
/// 1 = tout afficher (radio web dans un navigateur…), 2 = masquer.
/// </summary>
public sealed class MediaAppItem(MediaApp app, int mode, Action<string, int> setMode) : INotifyPropertyChanged
{
    private int _mode = mode;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => app.Name;
    public string Details => L.F("Media.LastPlayed", KindGlyphs.Ago(app.LastSeen));

    public int Mode
    {
        get => _mode;
        set
        {
            if (value is < 0 or > 2 || value == _mode) return;
            _mode = value;
            setMode(app.Id, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
        }
    }
}
