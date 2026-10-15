namespace Hublot.Modules.NowPlaying;

/// <summary>Pochette décodée : pixels BGRA prémultipliés, carrée.</summary>
internal sealed record Cover(byte[] Pixels, int Size)
{
    private uint? _seed;

    /// <summary>Couleur dominante (<see cref="CoverColors.Seed"/>), calculée au premier besoin.</summary>
    public uint Seed => _seed ??= CoverColors.Seed(Pixels);
}

internal enum MediaCommand { Previous, TogglePlayPause, Next }

/// <summary>
/// Position dans le titre, telle que l'application l'a communiquée à <paramref name="UpdatedUtc"/> ;
/// en lecture, la position courante s'en déduit sans redemander à l'application.
/// </summary>
/// <param name="Start">Début du titre dans l'échelle de l'application (pour demander une position).</param>
internal sealed record MediaTimeline(TimeSpan Position, TimeSpan Duration, DateTime UpdatedUtc, TimeSpan Start = default)
{
    public TimeSpan Current(bool playing)
    {
        var position = playing ? Position + (DateTime.UtcNow - UpdatedUtc) : Position;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
    }

    /// <summary>« 3:07 », « 1:02:45 ».</summary>
    public static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}

/// <summary>Ce que la carte affiche d'une session média (une par application qui joue).</summary>
/// <param name="AppIcon">Icône de l'application source (dossier « Applications » du shell), si disponible.</param>
/// <param name="NotMusic">Raison pour laquelle ce n'est pas de la musique (vidéo, film…), ou null.</param>
/// <param name="Timeline">Progression du titre (option « progression »), si l'application la communique.</param>
/// <param name="CanSeek">L'application accepte qu'on change la position (clic sur la progression).</param>
internal sealed record MediaState(string AppId, string AppName, string Title, string Artist, bool Playing,
    bool CanPrevious, bool CanNext, bool CanPlayPause, Cover? Cover, Cover? AppIcon = null, string? NotMusic = null,
    MediaTimeline? Timeline = null, bool CanSeek = false)
{
    public string Key => "media:" + AppId;
}
