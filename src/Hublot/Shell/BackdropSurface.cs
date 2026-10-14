using Hublot.Common;
using Hublot.Interop;

namespace Hublot.Shell;

/// <summary>
/// Matériaux « Acrylique » et « Mica » : le fond est rendu par le compositeur de Windows
/// (DWMWA_SYSTEMBACKDROP_TYPE, Windows 11 22H2+). Coins arrondis, contour et ombre sont ceux du système.
///
/// Deux subtilités :
/// - un matériau système retombe sur une couleur unie quand la fenêtre est inactive ; or le popup ne
///   prend jamais le focus. On le fait donc paraître actif (WM_NCACTIVATE) sans l'activer réellement ;
/// - la fenêtre est aussi layered (opacité globale) pour les fondus : le matériau reste appliqué.
/// Le contenu est un bitmap 32 bits prémultiplié : ses zones transparentes laissent voir le matériau.
/// </summary>
internal sealed unsafe class BackdropSurface : PopupSurface
{
    private const uint WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014, WM_NCACTIVATE = 0x0086;

    private Canvas? _canvas;
    private Theme? _theme;
    private Placement _placement;
    private uint _tint;
    private bool _visible;

    public BackdropSurface()
    {
        CreateHandle("Hublot.Backdrop",
            User32.WS_EX_LAYERED | User32.WS_EX_TOOLWINDOW | User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE,
            User32.WS_POPUP, User32.CS_DROPSHADOW);
        var margins = stackalloc int[4] { -1, -1, -1, -1 };
        Dwm.DwmExtendFrameIntoClientArea(Handle, margins);
        Dwm.SetInt(Handle, Dwm.DWMWA_WINDOW_CORNER_PREFERENCE, Dwm.DWMWCP_ROUND);
    }

    public override void Prepare(Theme theme, Settings settings, Placement placement)
    {
        _canvas?.Dispose();
        _theme = theme;
        _placement = placement;
        _canvas = new Canvas(placement.Width, placement.Height);
        _tint = PopupRenderer.WithAlpha(theme.Background, settings.TintOpacity / 100f);

        Dwm.SetInt(Handle, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, theme.Dark ? 1 : 0);
        Dwm.SetInt(Handle, Dwm.DWMWA_SYSTEMBACKDROP_TYPE,
            settings.Material == PopupMaterial.Mica ? Dwm.DWMSBT_MAINWINDOW : Dwm.DWMSBT_TRANSIENTWINDOW);
        User32.SetWindowPos(Handle, 0, placement.X, placement.Y, placement.Width, placement.Height,
            User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
    }

    public override void Render(CardDrawer draw, double opacity, int offsetX, int offsetY)
    {
        if (_canvas is null || _theme is null) return;
        _canvas.Fill(_tint);
        var card = new RectF(0, 0, _placement.Width, _placement.Height);
        draw(_canvas, card, _theme, _placement.Scale);
        _canvas.Flush();

        User32.SetWindowPos(Handle, 0, _placement.X + offsetX, _placement.Y + offsetY, 0, 0,
            User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        User32.SetLayeredWindowAttributes(Handle, 0, LayeredSurface.ToByte(opacity), User32.LWA_ALPHA);
        if (!_visible)
        {
            User32.ShowWindow(Handle, User32.SW_SHOWNOACTIVATE);
            BringToTop();
            // Apparence « active » : le matériau est rendu au lieu de sa couleur de repli.
            User32.DefWindowProcW(Handle, WM_NCACTIVATE, 1, 0);
            _visible = true;
        }
        Blit();
    }

    public override void Conceal()
    {
        User32.ShowWindow(Handle, User32.SW_HIDE);
        _visible = false;
        _canvas?.Dispose();
        _canvas = null;
    }

    private void Blit()
    {
        if (_canvas is null) return;
        var dc = User32.GetDC(Handle);
        Gdi32.BitBlt(dc, 0, 0, _canvas.Width, _canvas.Height, _canvas.Hdc, 0, 0, Gdi32.SRCCOPY);
        User32.ReleaseDC(Handle, dc);
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_PAINT:
                var paint = stackalloc byte[128];
                User32.BeginPaint(Handle, paint);
                Blit();
                User32.EndPaint(Handle, paint);
                return 0;

            case WM_ERASEBKGND:
                return 1;

            case WM_NCACTIVATE when wParam == 0:
                return 1; // ignorer la désactivation : garder le matériau
        }
        return base.WndProc(msg, wParam, lParam);
    }
}
