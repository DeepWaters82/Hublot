using Hublot.Shell;

namespace Hublot.Modules.Bluetooth;

/// <summary>Icône Segoe Fluent selon le type d'appareil.</summary>
internal static class DeviceGlyphs
{
    public static string For(DeviceKind kind) => kind switch
    {
        DeviceKind.Headphones => "",
        DeviceKind.Headset => "",
        DeviceKind.Speaker => "",
        DeviceKind.Keyboard => "",
        DeviceKind.Mouse => "",
        DeviceKind.Gamepad => "",
        DeviceKind.Phone => "",
        DeviceKind.Computer => "",
        DeviceKind.Pen => "",
        DeviceKind.Presenter => "",
        DeviceKind.Dongle => "",
        _ => Glyphs.Bluetooth,
    };
}
