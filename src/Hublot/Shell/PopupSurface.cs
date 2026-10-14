using Hublot.Common;
using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>Emplacement calculé d'une carte : rectangle à l'écran et échelle de rendu.</summary>
internal readonly record struct Placement(int X, int Y, int Width, int Height, float Scale);

internal enum SurfaceInput { HoverStart, HoverEnd, Move, Click }

/// <summary>Dessine le contenu d'une carte (fond déjà en place) dans le rectangle <c>card</c>.</summary>
internal delegate void CardDrawer(Canvas canvas, RectF card, Theme theme, float scale);

/// <summary>
/// Fenêtre qui affiche une carte. Ne prend jamais le focus ; remonte la souris au contrôleur
/// (<see cref="Popup"/>), en pixels relatifs à la carte, et laisse le contrôleur cadencer les animations.
/// </summary>
internal abstract unsafe class PopupSurface : NativeWindow
{
    private bool _hover;

    public event Action<PopupSurface, SurfaceInput, int, int>? Input;

    /// <summary>Prépare la fenêtre et les caches de fond (thème, matériau, position).</summary>
    public abstract void Prepare(Theme theme, Settings settings, Placement placement);

    /// <summary>Dessine une image : contenu, opacité globale, décalage de glissement (pixels).</summary>
    public abstract void Render(CardDrawer draw, double opacity, int offsetX, int offsetY);

    /// <summary>Masque la fenêtre et libère les ressources graphiques.</summary>
    public abstract void Conceal();

    /// <summary>Distance entre le bord de la fenêtre et celui de la carte (marge d'ombre).</summary>
    protected virtual int CardInset => 0;

    /// <summary>
    /// Au sommet des fenêtres « toujours visibles » : une fenêtre masquée garde sa place dans l'ordre Z,
    /// et un navigateur, un lecteur en incrustation ou une fenêtre plein écran a pu passer devant depuis.
    /// </summary>
    protected void BringToTop() => User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0,
        User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);

    public bool IsHovered => _hover;

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        int x = LoWord(lParam) - CardInset, y = HiWord(lParam) - CardInset;
        switch (msg)
        {
            case Msg.WM_MOUSEACTIVATE:
                return User32.MA_NOACTIVATE;

            case Msg.WM_MOUSEMOVE:
                if (!_hover)
                {
                    _hover = true;
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = User32.TME_LEAVE, hwndTrack = Handle };
                    User32.TrackMouseEvent(&tme);
                    Input?.Invoke(this, SurfaceInput.HoverStart, x, y);
                }
                Input?.Invoke(this, SurfaceInput.Move, x, y);
                return 0;

            case Msg.WM_MOUSELEAVE:
                _hover = false;
                Input?.Invoke(this, SurfaceInput.HoverEnd, -1, -1);
                return 0;

            case Msg.WM_LBUTTONUP:
            case Msg.WM_RBUTTONUP:
                Input?.Invoke(this, SurfaceInput.Click, x, y);
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }
}

/// <summary>
/// Matériau « Uni » : fenêtre layered à alpha par pixel (UpdateLayeredWindow), ombre dessinée
/// par l'application, fond plus ou moins transparent. Fonctionne sur Windows 10 et 11.
/// </summary>
internal sealed unsafe class LayeredSurface : PopupSurface
{
    private Canvas? _canvas;
    private uint[]? _background;
    private Theme? _theme;
    private RectF _card;
    private Placement _placement;
    private int _margin;
    private bool _visible;

    public LayeredSurface()
    {
        CreateHandle("Hublot.Layered",
            User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW | User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE,
            User32.WS_POPUP);
    }

    protected override int CardInset => _margin;

    public override void Prepare(Theme theme, Settings settings, Placement placement)
    {
        _canvas?.Dispose();
        _theme = theme;
        _placement = placement;
        _margin = PopupRenderer.ShadowMargin(placement.Scale);
        _canvas = new Canvas(placement.Width + 2 * _margin, placement.Height + 2 * _margin);
        _card = new RectF(_margin, _margin, placement.Width, placement.Height);
        PopupRenderer.DrawCard(_canvas, _card, theme, placement.Scale, settings.SolidOpacity);
        _background = _canvas.CopyPixels();
    }

    public override void Render(CardDrawer draw, double opacity, int offsetX, int offsetY)
    {
        if (_canvas is null || _background is null || _theme is null) return;
        _canvas.RestorePixels(_background);
        draw(_canvas, _card, _theme, _placement.Scale);
        _canvas.Flush();

        var dst = new POINT(_placement.X - _margin + offsetX, _placement.Y - _margin + offsetY);
        var size = new SIZE(_canvas.Width, _canvas.Height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { AlphaFormat = 1, SourceConstantAlpha = ToByte(opacity) };
        User32.UpdateLayeredWindow(Handle, 0, &dst, &size, _canvas.Hdc, &src, 0, &blend, User32.ULW_ALPHA);
        if (!_visible)
        {
            User32.ShowWindow(Handle, User32.SW_SHOWNOACTIVATE);
            BringToTop();
            _visible = true;
        }
    }

    public override void Conceal()
    {
        User32.ShowWindow(Handle, User32.SW_HIDE);
        _visible = false;
        _canvas?.Dispose();
        _canvas = null;
        _background = null;
    }

    internal static byte ToByte(double opacity) => (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
}
