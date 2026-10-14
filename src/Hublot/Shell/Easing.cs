namespace Hublot.Shell;

/// <summary>Courbes d'animation Fluent (mêmes définitions que les animations de Windows 11 / WinUI).</summary>
internal static class Easing
{
    /// <summary>Entrée : démarrage vif, arrivée très douce (« Fluent decelerate »).</summary>
    public static double Decelerate(double t) => CubicBezier(0.1, 0.9, 0.2, 1.0, t);

    /// <summary>Sortie : départ doux, fin rapide (« Fluent accelerate »).</summary>
    public static double Accelerate(double t) => CubicBezier(0.7, 0.0, 1.0, 0.5, t);

    /// <summary>Léger dépassement puis retour, pour l'apparition de l'icône.</summary>
    public static double OutBack(double t, double overshoot = 1.5)
    {
        double u = t - 1;
        return 1 + (overshoot + 1) * u * u * u + overshoot * u * u;
    }

    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    /// <summary>Courbe de Bézier cubique CSS (P0 = 0,0 ; P3 = 1,1), résolue en x par Newton puis dichotomie.</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        static double Sample(double a1, double a2, double t) => ((1 - 3 * a2 + 3 * a1) * t + (3 * a2 - 6 * a1)) * t * t + 3 * a1 * t;
        static double Slope(double a1, double a2, double t) => 3 * (1 - 3 * a2 + 3 * a1) * t * t + 2 * (3 * a2 - 6 * a1) * t + 3 * a1;

        double s = x;
        for (int i = 0; i < 8; i++)
        {
            double err = Sample(x1, x2, s) - x;
            if (Math.Abs(err) < 1e-5) return Sample(y1, y2, s);
            double d = Slope(x1, x2, s);
            if (Math.Abs(d) < 1e-6) break;
            s -= err / d;
        }

        double lo = 0, hi = 1;
        s = x;
        for (int i = 0; i < 30; i++)
        {
            double v = Sample(x1, x2, s);
            if (Math.Abs(v - x) < 1e-5) break;
            if (v < x) lo = s; else hi = s;
            s = (lo + hi) / 2;
        }
        return Sample(y1, y2, s);
    }
}
