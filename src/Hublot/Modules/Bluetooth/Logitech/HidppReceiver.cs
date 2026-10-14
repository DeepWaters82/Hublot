using Hublot.Common;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Hublot.Modules.Bluetooth.Logitech;

/// <summary>
/// Transport HID++ vers un récepteur Logitech (Unifying, Bolt, Lightspeed, Nano).
/// Windows expose le canal HID++ en deux collections HID « fournisseur » (page 0xFF00) :
/// usage 1 pour les rapports courts (ID 0x10, 7 octets), usage 2 pour les longs (ID 0x11, 20 octets).
/// Une requête courte peut recevoir une réponse longue : les deux collections sont donc écoutées.
/// Réception purement événementielle (InputReportReceived), aucun sondage.
/// </summary>
internal sealed class HidppReceiver : IDisposable
{
    public const byte ReceiverIndex = 0xFF;
    private const byte ShortId = 0x10, LongId = 0x11;
    private const int ShortLength = 7, LongLength = 20;
    /// <summary>Identifiant logiciel (4 bits) : distingue nos réponses de celles d'autres logiciels (Options+, G Hub).</summary>
    private const byte SoftwareId = 0x0B;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    private readonly HidDevice? _short;
    private readonly HidDevice _long;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private TaskCompletionSource<byte[]>? _pending;
    private (byte Device, byte A, byte B) _expected;

    /// <summary>Tout rapport HID++ qui n'est pas la réponse attendue (notifications, événements).</summary>
    public event Action<byte[]>? Notification;

    private HidppReceiver(HidDevice? shortChannel, HidDevice longChannel)
    {
        _short = shortChannel;
        _long = longChannel;
        if (_short is not null) _short.InputReportReceived += OnReport;
        _long.InputReportReceived += OnReport;
    }

    public bool HasShortChannel => _short is not null;

    public static async Task<HidppReceiver?> OpenAsync(string? shortId, string longId)
    {
        var longChannel = await HidDevice.FromIdAsync(longId, FileAccessMode.ReadWrite);
        if (longChannel is null) return null;
        var shortChannel = shortId is null ? null : await HidDevice.FromIdAsync(shortId, FileAccessMode.ReadWrite);
        return new HidppReceiver(shortChannel, longChannel);
    }

    /// <summary>HID++ 2.0 : fonction <paramref name="function"/> de la fonctionnalité d'index <paramref name="featureIndex"/>.</summary>
    public Task<byte[]?> CallAsync(byte device, byte featureIndex, byte function, params byte[] args)
    {
        byte b = (byte)((function << 4) | SoftwareId);
        var payload = new byte[3 + args.Length];
        payload[0] = device;
        payload[1] = featureIndex;
        payload[2] = b;
        args.CopyTo(payload, 3);
        return SendAsync(LongId, payload, (device, featureIndex, b));
    }

    /// <summary>HID++ 1.0 : lecture d'un registre court du récepteur.</summary>
    public Task<byte[]?> ReadRegisterAsync(byte register)
        => SendAsync(ShortId, [ReceiverIndex, 0x81, register], (ReceiverIndex, 0x81, register));

    /// <summary>HID++ 1.0 : écriture d'un registre court du récepteur (3 octets).</summary>
    public Task<byte[]?> WriteRegisterAsync(byte register, byte p0, byte p1, byte p2)
        => SendAsync(ShortId, [ReceiverIndex, 0x80, register, p0, p1, p2], (ReceiverIndex, 0x80, register));

    /// <summary>HID++ 1.0 : lecture d'un registre long du récepteur (réponse en rapport long).</summary>
    public Task<byte[]?> ReadLongRegisterAsync(byte register, byte p0, byte p1 = 0)
        => SendAsync(ShortId, [ReceiverIndex, 0x83, register, p0, p1], (ReceiverIndex, 0x83, register));

    private async Task<byte[]?> SendAsync(byte reportId, byte[] payload, (byte Device, byte A, byte B) expected)
    {
        // Sans collection courte, les requêtes courtes passent en rapport long.
        var channel = reportId == ShortId ? _short : _long;
        if (channel is null)
        {
            channel = _long;
            reportId = LongId;
        }

        await _gate.WaitAsync();
        try
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _pending = tcs;
                _expected = expected;
            }

            var bytes = new byte[reportId == ShortId ? ShortLength : LongLength];
            bytes[0] = reportId;
            payload.AsSpan(0, Math.Min(payload.Length, bytes.Length - 1)).CopyTo(bytes.AsSpan(1));
            var report = channel.CreateOutputReport(reportId);
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(bytes);
                report.Data = writer.DetachBuffer();
            }
            await channel.SendOutputReportAsync(report);

            var done = await Task.WhenAny(tcs.Task, Task.Delay(Timeout));
            lock (_lock)
            {
                if (_pending == tcs) _pending = null;
            }
            if (done != tcs.Task) return null;

            var response = tcs.Task.Result;
            return IsError(response) ? null : response;
        }
        catch (Exception ex)
        {
            Log.Error("HID++", ex);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnReport(HidDevice sender, HidInputReportReceivedEventArgs args)
    {
        byte[] data;
        try
        {
            using var reader = DataReader.FromBuffer(args.Report.Data);
            data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
        }
        catch (Exception ex)
        {
            Log.Error("HID++ lecture", ex);
            return;
        }
        if (data.Length < 5 || (data[0] != ShortId && data[0] != LongId)) return;

        TaskCompletionSource<byte[]>? completed = null;
        lock (_lock)
        {
            if (_pending is not null && Matches(data, _expected))
            {
                completed = _pending;
                _pending = null;
            }
        }
        if (completed is not null) completed.TrySetResult(data);
        else Notification?.Invoke(data);
    }

    /// <summary>Réponse : mêmes octets 1 à 3 que la requête ; erreur : 0xFF (2.0) / 0x8F (1.0) puis la requête décalée.</summary>
    private static bool Matches(byte[] r, (byte Device, byte A, byte B) e)
    {
        if (r[1] != e.Device) return false;
        if (r[2] == e.A && r[3] == e.B) return true;
        return IsError(r) && r[3] == e.A && r[4] == e.B;
    }

    private static bool IsError(byte[] r) => r[2] is 0xFF or 0x8F;

    public void Dispose()
    {
        if (_short is not null)
        {
            _short.InputReportReceived -= OnReport;
            _short.Dispose();
        }
        _long.InputReportReceived -= OnReport;
        _long.Dispose();
        _gate.Dispose();
    }
}
