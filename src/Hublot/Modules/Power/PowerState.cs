namespace Hublot.Modules.Power;

/// <summary>Motif du popup d'alimentation.</summary>
internal enum PowerReason { None, LevelReached, Low }

/// <summary>Alimentation du PC à un instant donné.</summary>
/// <param name="Name">Nom du PC (celui des Paramètres Windows, « Nom de l'appareil »).</param>
/// <param name="Plugged">Sur secteur.</param>
/// <param name="Percent">Charge de la batterie, si connue.</param>
/// <param name="Charging">La batterie se charge.</param>
/// <param name="SecondsLeft">Autonomie estimée par Windows sur batterie, si connue.</param>
internal sealed record PowerState(string Name, bool Plugged, int? Percent, bool Charging, int? SecondsLeft,
    PowerReason Reason = PowerReason.None)
{
    public string StatusText
    {
        get
        {
            if (Reason == PowerReason.LevelReached) return L.T("Power.LevelReached");
            if (Reason == PowerReason.Low) return L.T(Plugged ? "Power.LowPlugged" : "Power.LowUnplugged");
            if (!Plugged)
                return SecondsLeft is int s && s > 0 ? L.F("Power.OnBatteryLeft", Duration(s)) : L.T("Power.OnBattery");
            if (Charging) return L.T("Power.Charging");
            return L.T(Percent >= 99 ? "Power.Full" : "Power.Plugged");
        }
    }

    /// <summary>« 3 h 20 », « 45 min ».</summary>
    private static string Duration(int seconds)
    {
        int minutes = (seconds + 59) / 60;
        return minutes < 60 ? L.F("Power.Minutes", minutes) : L.F("Power.Hours", minutes / 60, minutes % 60);
    }
}
