using System.Runtime.InteropServices;
using Hublot.Common;
using Hublot.Interop;
using Hublot.Shell;

namespace Hublot.Modules.Power;

/// <summary>
/// Module « Alimentation » : popup quand le PC est branché sur secteur ou débranché, avec son nom et
/// la charge de la batterie ; en option, quand la charge atteint un niveau (en charge) ou descend sous
/// un seuil (sur batterie). Purement événementiel : abonnement aux paramètres d'alimentation
/// (PowerSettingRegisterNotification, rappel sans fenêtre), aucun sondage. Sans batterie (PC fixe),
/// le module ne fait rien.
///
/// Anti-rebond : la source est relue <see cref="SettleMs"/> après le dernier changement ; un câble
/// mal enfoncé qui débranche et rebranche aussitôt ne produit rien. Windows envoie l'état courant dès
/// l'abonnement : il sert de référence, pas d'événement.
/// </summary>
internal sealed unsafe class PowerModule : Module
{
    private const int SettleMs = 800;

    /// <summary>Destinataire des rappels du système (une seule instance du module).</summary>
    private static PowerModule? s_active;

    private IHost? _host;
    private nint _sourceHandle, _percentHandle;
    private bool? _plugged;
    private int? _lastPercent;
    private Timer? _settle;
    private string _name = Environment.MachineName;

    public override string Id => "power";
    public override string Name => "Alimentation";

    private IHost Host => _host ?? throw new InvalidOperationException("Module non démarré");

    public override bool IsEnabled(Settings settings) => settings.ModulePower;

    public override void Start(IHost host)
    {
        _host = host;
        _name = ComputerName();
        var state = Read();
        _plugged = state?.Plugged;
        _lastPercent = state?.Percent;
        if (state is null)
        {
            Log.Write("Alimentation : pas de batterie, rien à surveiller");
            return;
        }
        s_active = this;
        _sourceHandle = Register(PowerApi.AcDcPowerSource);
        _percentHandle = Register(PowerApi.BatteryPercentage);
        Log.Write($"Alimentation : {state.StatusText}, {state.Percent} %");
    }

    public override void Stop()
    {
        if (_sourceHandle != 0) PowerApi.PowerSettingUnregisterNotification(_sourceHandle);
        if (_percentHandle != 0) PowerApi.PowerSettingUnregisterNotification(_percentHandle);
        _sourceHandle = _percentHandle = 0;
        s_active = null;
        _settle?.Dispose();
        _settle = null;
        _host = null;
    }

    private static nint Register(Guid setting)
    {
        var recipient = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { Callback = &OnPowerSetting };
        uint error = PowerApi.PowerSettingRegisterNotification(&setting, PowerApi.DEVICE_NOTIFY_CALLBACK, &recipient, out var handle);
        if (error != 0) Log.Write($"Alimentation : abonnement {setting} impossible (erreur {error})");
        return error == 0 ? handle : 0;
    }

    /// <summary>Rappel du système (thread quelconque) : POWERBROADCAST_SETTING = GUID, longueur, données.</summary>
    [UnmanagedCallersOnly]
    private static uint OnPowerSetting(nint context, uint type, nint setting)
    {
        if (type != PowerApi.PBT_POWERSETTINGCHANGE || setting == 0 || s_active is not { _host: { } host } module) return 0;
        var guid = *(Guid*)setting;
        if (guid == PowerApi.AcDcPowerSource) host.Post(module.ScheduleSourceCheck);
        else if (guid == PowerApi.BatteryPercentage) host.Post(module.OnPercentChanged);
        return 0;
    }

    private void ScheduleSourceCheck()
    {
        _settle ??= new Timer(_ => _host?.Post(CheckSource));
        _settle.Change(SettleMs, Timeout.Infinite);
    }

    private void CheckSource()
    {
        if (_host is null || Read() is not { } state || state.Plugged == _plugged) return;
        _plugged = state.Plugged;
        Log.Write($"Alimentation : {state.StatusText}, {state.Percent} %");
        if (state.Plugged ? Host.Settings.PowerNotifyPlugged : Host.Settings.PowerNotifyUnplugged)
            Host.Notify(new PowerCard(state));
    }

    /// <summary>
    /// Nouveau pourcentage : popup si un seuil est franchi (niveau de charge visé en montant sur secteur,
    /// batterie faible en descendant sur batterie), sinon mise à jour de la carte si elle est affichée.
    /// Franchir le seuil (et non l'atteindre) évite une seconde alerte tant qu'on reste au-dessus / en dessous.
    /// </summary>
    private void OnPercentChanged()
    {
        if (_host is null || Read() is not { Percent: int percent } state) return;
        int? before = _lastPercent;
        _lastPercent = percent;
        var s = Host.Settings;
        if (before is int b && b != percent)
        {
            if (s.PowerNotifyLevel && state.Plugged && b < s.PowerLevel && percent >= s.PowerLevel)
            {
                Log.Write($"Alimentation : niveau de charge {percent} % atteint");
                Host.Notify(new PowerCard(state with { Reason = PowerReason.LevelReached }));
                return;
            }
            if (s.PowerNotifyLow && !state.Plugged && b > s.PowerLowLevel && percent <= s.PowerLowLevel)
            {
                Log.Write($"Alimentation : batterie faible ({percent} %)");
                Host.Notify(new PowerCard(state with { Reason = PowerReason.Low }));
                return;
            }
        }
        if (Host.CurrentNotification is PowerCard) Host.Update(new PowerCard(state));
    }

    /// <summary>État courant, ou null sans batterie.</summary>
    private PowerState? Read()
    {
        if (!PowerApi.GetSystemPowerStatus(out var s) || s.BatteryFlag == 255 || (s.BatteryFlag & 128) != 0) return null;
        int? percent = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
        int? left = s.BatteryLifeTime > 0 ? s.BatteryLifeTime : null;
        return new PowerState(_name, s.ACLineStatus == 1, percent, (s.BatteryFlag & 8) != 0, left);
    }

    /// <summary>Nom de l'appareil tel que dans les Paramètres (casse conservée), sinon le nom NetBIOS.</summary>
    private static string ComputerName()
    {
        char* buffer = stackalloc char[256];
        uint size = 256;
        return PowerApi.GetComputerNameExW(PowerApi.ComputerNameDnsHostname, buffer, ref size) && size > 0
            ? new string(buffer, 0, (int)size)
            : Environment.MachineName;
    }

    public override IEnumerable<CardContent> PreviewCards() =>
    [
        new PowerCard(new PowerState(ComputerName(), true, 76, true, null)),
    ];
}
