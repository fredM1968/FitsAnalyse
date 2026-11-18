using FitsAnalyse.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FitsAnalyse.Views {

    /// <summary>
    /// Histogramme interactif :
    ///  - molette : zoom horizontal autour du pointeur ;
    ///  - glisser avec le bouton gauche : déplacement ;
    ///  - clic : repère vertical avec les valeurs (x ; y) de chaque courbe ;
    ///  - double clic : toute la plage ; clic droit : efface le repère.
    /// La plage affichée (ViewMin, ViewMax) est liée au ViewModel, ce qui permet les boutons +/− et le cadrage automatique.
    /// </summary>
    public class HistogramControl : FrameworkElement {

        #region Propriétés

        public static readonly DependencyProperty HistogramsProperty = Dp(nameof(Histograms), typeof(HistogramSet), null);
        public static readonly DependencyProperty ReferenceProperty = Dp(nameof(Reference), typeof(HistogramSet), null);
        public static readonly DependencyProperty ReferenceLabelProperty = Dp(nameof(ReferenceLabel), typeof(string), null);
        public static readonly DependencyProperty ShowColorProperty = Dp(nameof(ShowColor), typeof(bool), false);
        public static readonly DependencyProperty ShowRProperty = Dp(nameof(ShowR), typeof(bool), true);
        public static readonly DependencyProperty ShowGProperty = Dp(nameof(ShowG), typeof(bool), true);
        public static readonly DependencyProperty ShowBProperty = Dp(nameof(ShowB), typeof(bool), true);
        public static readonly DependencyProperty LogScaleProperty = Dp(nameof(LogScale), typeof(bool), false);
        public static readonly DependencyProperty SmoothProperty = Dp(nameof(Smooth), typeof(bool), true);
        public static readonly DependencyProperty ShowCurveProperty = Dp(nameof(ShowCurve), typeof(bool), true);
        public static readonly DependencyProperty ShowOutputProperty = Dp(nameof(ShowOutput), typeof(bool), true);
        public static readonly DependencyProperty ChannelCurvesProperty = Dp(nameof(ChannelCurves), typeof(CurveParams[]), null);
        public static readonly DependencyProperty CurveCommittedCommandProperty = Dp(nameof(CurveCommittedCommand), typeof(ICommand), null);
        public static readonly DependencyProperty CurveChangingCommandProperty = Dp(nameof(CurveChangingCommand), typeof(ICommand), null);

        public static readonly DependencyProperty ShadowsProperty = DependencyProperty.Register(nameof(Shadows), typeof(double), typeof(HistogramControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty MidtoneProperty = DependencyProperty.Register(nameof(Midtone), typeof(double), typeof(HistogramControl),
            new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty HighlightsProperty = DependencyProperty.Register(nameof(Highlights), typeof(double), typeof(HistogramControl),
            new FrameworkPropertyMetadata(65535.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ViewMinProperty = DependencyProperty.Register(nameof(ViewMin), typeof(double), typeof(HistogramControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ViewMaxProperty = DependencyProperty.Register(nameof(ViewMax), typeof(double), typeof(HistogramControl),
            new FrameworkPropertyMetadata(65536.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        private static DependencyProperty Dp(string name, Type type, object def) =>
            DependencyProperty.Register(name, type, typeof(HistogramControl), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

        public HistogramSet Histograms { get => (HistogramSet)GetValue(HistogramsProperty); set => SetValue(HistogramsProperty, value); }
        public HistogramSet Reference { get => (HistogramSet)GetValue(ReferenceProperty); set => SetValue(ReferenceProperty, value); }
        public string ReferenceLabel { get => (string)GetValue(ReferenceLabelProperty); set => SetValue(ReferenceLabelProperty, value); }
        public bool ShowColor { get => (bool)GetValue(ShowColorProperty); set => SetValue(ShowColorProperty, value); }
        public bool ShowR { get => (bool)GetValue(ShowRProperty); set => SetValue(ShowRProperty, value); }
        public bool ShowG { get => (bool)GetValue(ShowGProperty); set => SetValue(ShowGProperty, value); }
        public bool ShowB { get => (bool)GetValue(ShowBProperty); set => SetValue(ShowBProperty, value); }
        public bool LogScale { get => (bool)GetValue(LogScaleProperty); set => SetValue(LogScaleProperty, value); }
        public bool Smooth { get => (bool)GetValue(SmoothProperty); set => SetValue(SmoothProperty, value); }
        public bool ShowCurve { get => (bool)GetValue(ShowCurveProperty); set => SetValue(ShowCurveProperty, value); }

        /// <summary>Affiche au-dessus l'histogramme de l'image étirée (sortie de la courbe), recalculé en direct.</summary>
        public bool ShowOutput { get => (bool)GetValue(ShowOutputProperty); set => SetValue(ShowOutputProperty, value); }

        /// <summary>Courbes par canal (étirement séparé) : dessinées en couleur ; null = une seule courbe orange.</summary>
        public CurveParams[] ChannelCurves { get => (CurveParams[])GetValue(ChannelCurvesProperty); set => SetValue(ChannelCurvesProperty, value); }

        /// <summary>Exécutée quand on relâche un curseur de la courbe : le ViewModel applique alors l'étirement à l'image.</summary>
        public ICommand CurveCommittedCommand { get => (ICommand)GetValue(CurveCommittedCommandProperty); set => SetValue(CurveCommittedCommandProperty, value); }

        /// <summary>Exécutée pendant le glissement d'un curseur (au plus toutes les 100 ms) : aperçu de l'image en direct.</summary>
        public ICommand CurveChangingCommand { get => (ICommand)GetValue(CurveChangingCommandProperty); set => SetValue(CurveChangingCommandProperty, value); }

        /// <summary>Point noir (ADU).</summary>
        public double Shadows { get => (double)GetValue(ShadowsProperty); set => SetValue(ShadowsProperty, value); }
        /// <summary>Tons moyens : position relative (0..1) du curseur central entre le noir et le blanc.</summary>
        public double Midtone { get => (double)GetValue(MidtoneProperty); set => SetValue(MidtoneProperty, value); }
        /// <summary>Point blanc (ADU).</summary>
        public double Highlights { get => (double)GetValue(HighlightsProperty); set => SetValue(HighlightsProperty, value); }
        public double ViewMin { get => (double)GetValue(ViewMinProperty); set => SetValue(ViewMinProperty, value); }
        public double ViewMax { get => (double)GetValue(ViewMaxProperty); set => SetValue(ViewMaxProperty, value); }

        #endregion

        #region Apparence

        private static readonly Brush BackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)));
        private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0x34, 0x34, 0x34), 1);
        private static readonly Pen AxisPen = FrozenPen(Color.FromRgb(0x60, 0x60, 0x60), 1);
        private static readonly Pen RefPen = FrozenPen(Color.FromArgb(0xC8, 0xA0, 0xA0, 0xA0), 1.4);
        private static readonly Pen MarkerPen = FrozenPen(Color.FromRgb(0xFF, 0xD0, 0x40), 1, new DashStyle(new double[] { 4, 3 }, 0));
        private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)));
        private static readonly Brush LabelBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x24, 0x24, 0x24)));

        private static readonly Color[] SeriesColors = {
            Color.FromRgb(0xE8, 0xE8, 0xE8),   // L
            Color.FromRgb(0xF0, 0x50, 0x50),   // R
            Color.FromRgb(0x50, 0xD0, 0x50),   // V
            Color.FromRgb(0x50, 0x9A, 0xFF)    // B
        };

        private static readonly Pen[] SeriesPens = {
            FrozenPen(SeriesColors[0], 1.4), FrozenPen(SeriesColors[1], 1.4), FrozenPen(SeriesColors[2], 1.4), FrozenPen(SeriesColors[3], 1.4)
        };

        private static readonly Brush MonoFill = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xE8, 0xE8, 0xE8)));

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        private static Pen FrozenPen(Color c, double w, DashStyle dash = null) {
            var p = new Pen(new SolidColorBrush(c), w) { LineJoin = PenLineJoin.Round };
            if (dash != null) p.DashStyle = dash;
            p.Freeze();
            return p;
        }

        private const double MarginLeft = 6, MarginRight = 6, MarginTop = 6, MarginBottom = 34;

        /// <summary>Bande des curseurs bas / moyen / haut, sous le tracé.</summary>
        private const double StripTop = 3, StripHeight = 12;

        #endregion

        public HistogramControl() {
            Focusable = true;
            ClipToBounds = true;
            Cursor = Cursors.Cross;
            MinHeight = 120;
        }

        /// <summary>Valeur (ADU) du repère posé au clic, NaN s'il n'y en a pas.</summary>
        private double markerValue = double.NaN;

        #region Données agrégées par pas de valeurs

        /// <summary>Compteurs regroupés par tranche de Step ADU (mis en cache pour chaque tableau de compteurs).</summary>
        private readonly ConditionalWeakTable<int[], long[]> aggregated = new ConditionalWeakTable<int[], long[]>();

        private long[] Aggregate(int[] counts, int step) {
            if (aggregated.TryGetValue(counts, out var a) && a.Length == (HistogramSet.Size + step - 1) / step) return a;
            int n = (HistogramSet.Size + step - 1) / step;
            a = new long[n];
            for (int v = 0; v < counts.Length; v++) a[v / step] += counts[v];
            aggregated.AddOrUpdate(counts, a);
            return a;
        }

        /// <summary>
        /// Lissage adaptatif, pour l'affichage seulement : chaque tranche est moyennée avec ses voisines jusqu'à réunir
        /// au moins 400 pixels (bruit de comptage d'environ 5 %), dans la limite de ±32 tranches.
        /// Le pic, très peuplé, n'est donc pas déformé ; seules les queues clairsemées sont adoucies.
        /// Les tranches 0 et saturée ne sont jamais mélangées aux autres. Les valeurs données au clic restent brutes.
        /// </summary>
        private readonly ConditionalWeakTable<int[], long[]> smoothed = new ConditionalWeakTable<int[], long[]>();

        private const long SmoothTarget = 400;
        private const int SmoothMaxHalf = 32;

        private long[] Smoothed(int[] counts, int step, int satBin) {
            var raw = Aggregate(counts, step);
            if (smoothed.TryGetValue(counts, out var a) && a.Length == raw.Length) return a;
            int n = raw.Length;
            var prefix = new long[n + 1];
            for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + raw[i];
            a = new long[n];
            int lo = 1, hi = Math.Min(n - 1, satBin - 1);   // plage lissable : hors tranche 0 et saturation
            for (int i = 0; i < n; i++) {
                if (i < lo || i > hi || raw[i] >= SmoothTarget) { a[i] = raw[i]; continue; }
                int half = 0;
                long sum = raw[i];
                while (sum < SmoothTarget && half < SmoothMaxHalf) {
                    half++;
                    int l = Math.Max(lo, i - half), r = Math.Min(hi, i + half);
                    sum = prefix[r + 1] - prefix[l];
                    if (l == lo && r == hi) break;
                }
                int left = Math.Max(lo, i - half), right = Math.Min(hi, i + half);
                a[i] = (long)Math.Round((double)(prefix[right + 1] - prefix[left]) / (right - left + 1));
            }
            smoothed.AddOrUpdate(counts, a);
            return a;
        }

        private IEnumerable<int> VisibleChannels(HistogramSet h) {
            if (h == null) yield break;
            if (ShowColor && h.IsColor) {
                if (ShowR) yield return Channel.R;
                if (ShowG) yield return Channel.G;
                if (ShowB) yield return Channel.B;
            } else if (h.Counts[Channel.L] != null) {
                yield return Channel.L;
            }
        }

        #endregion

        #region Dessin

        /// <summary>Hauteur du volet « après étirement » (0 s'il est masqué).</summary>
        private double OutputHeight => ShowOutput && ShowCurve && Histograms != null
            ? Math.Max(0, Math.Floor((ActualHeight - MarginTop - MarginBottom) * 0.32))
            : 0;

        private const double OutputGap = 8;

        /// <summary>Tracé de l'histogramme d'origine (avec la courbe et les curseurs).</summary>
        private Rect PlotRect {
            get {
                double oh = OutputHeight;
                double top = MarginTop + (oh > 0 ? oh + OutputGap : 0);
                return new Rect(MarginLeft, top,
                                Math.Max(1, ActualWidth - MarginLeft - MarginRight),
                                Math.Max(1, ActualHeight - top - MarginBottom));
            }
        }

        /// <summary>Volet de l'histogramme après étirement, au-dessus.</summary>
        private Rect OutputRect => new Rect(MarginLeft, MarginTop, Math.Max(1, ActualWidth - MarginLeft - MarginRight), Math.Max(1, OutputHeight));

        protected override void OnRender(DrawingContext dc) {
            var full = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(BackBrush, null, full);
            var plot = PlotRect;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var h = Histograms;
            double vmin = ViewMin, vmax = ViewMax;
            if (!(vmax > vmin)) { vmin = 0; vmax = 65536; }

            DrawGridAndAxis(dc, plot, vmin, vmax, dpi);

            if (h == null) {
                DrawText(dc, "Aucune image : prends une pose ou ouvre un fichier.", new Point(plot.Left + 8, plot.Top + 8), dpi, 12);
                return;
            }

            int cols = Math.Max(2, (int)plot.Width);
            var channels = new List<int>(VisibleChannels(h));
            var refChannels = new List<int>(VisibleChannels(Reference));

            // valeurs par colonne, pour l'image et la référence
            var series = new List<(int ch, double[] y, bool isRef)>();
            foreach (var ch in refChannels) series.Add((ch, Columns(Reference, ch, vmin, vmax, cols, out _), true));
            double yMax = 0;
            foreach (var ch in channels) {
                var y = Columns(h, ch, vmin, vmax, cols, out double m);
                yMax = Math.Max(yMax, m);
                series.Add((ch, y, false));
            }
            foreach (var s in series) {
                if (!s.isRef) continue;
                ColumnsMax(Reference, s.ch, vmin, vmax, cols, ref yMax);
            }
            if (yMax <= 0) yMax = 1;

            dc.PushClip(new RectangleGeometry(plot));
            foreach (var s in series) {
                bool mono = !s.isRef && s.ch == Channel.L;
                var geo = BuildGeometry(s.y, plot, yMax, cols, closed: mono);
                if (s.isRef) {
                    dc.DrawGeometry(null, RefPen, geo);
                } else {
                    dc.DrawGeometry(mono ? MonoFill : null, SeriesPens[s.ch], geo);
                }
            }
            dc.Pop();

            // légende et échelle verticale
            string yInfo = LogScale ? "log" : $"max {Fmt(yMax)} px";
            if (h.Step > 1) yInfo += $" · pas de {h.Step} ADU";
            DrawText(dc, yInfo, new Point(plot.Left + 4, plot.Top + 2), dpi, 10);
            if (Reference != null) {
                var t = Text($"gris : {ReferenceLabel ?? "référence"}", dpi, 10, RefPen.Brush);
                dc.DrawText(t, new Point(plot.Right - t.Width - 4, plot.Top + 2));
            }

            if (ShowCurve) DrawCurve(dc, plot, vmin, vmax);
            if (OutputHeight > 20) DrawOutput(dc, OutputRect, dpi, h);

            DrawMarker(dc, plot, vmin, vmax, dpi, h, channels);

            // version du plugin, discrète, pour vérifier que NINA a bien chargé la dernière compilation
            var ver = Text("FitsAnalyse " + Version, dpi, 9, VersionBrush);
            dc.DrawText(ver, new Point(plot.Right - ver.Width - 4, plot.Bottom - ver.Height - 2));
        }

        private static readonly string Version = typeof(HistogramControl).Assembly.GetName().Version?.ToString(3) ?? "";
        private static readonly Brush VersionBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0x90, 0x90, 0x90)));

        #region Courbe et curseurs

        private static readonly Pen CurvePen = FrozenPen(Color.FromRgb(0xFF, 0x8C, 0x50), 1.3);
        private static readonly Pen[] ChannelCurvePens = {
            CurvePen,
            FrozenPen(Color.FromRgb(0xFF, 0x90, 0x90), 1.3, new DashStyle(new double[] { 5, 2 }, 0)),
            FrozenPen(Color.FromRgb(0x90, 0xF0, 0x90), 1.3, new DashStyle(new double[] { 5, 2 }, 0)),
            FrozenPen(Color.FromRgb(0x90, 0xC0, 0xFF), 1.3, new DashStyle(new double[] { 5, 2 }, 0))
        };
        private static readonly Pen HandlePen = FrozenPen(Color.FromRgb(0x90, 0x90, 0x90), 1);

        private CurveParams CurrentCurve => new CurveParams { Shadows = Shadows, Midtone = Midtone, Highlights = Highlights };

        private double MidPosition => Shadows + Midtone * (Highlights - Shadows);

        private double XOf(Rect plot, double v, double vmin, double vmax) => plot.Left + (v - vmin) / (vmax - vmin) * plot.Width;

        /// <summary>Courbe de transfert (en orange, comme Siril), bande de dégradé et curseurs triangulaires.</summary>
        private void DrawCurve(DrawingContext dc, Rect plot, double vmin, double vmax) {
            var c = CurrentCurve;
            int cols = Math.Max(2, (int)plot.Width);

            // courbe(s) : sortie 0..1 sur toute la hauteur du tracé
            dc.PushClip(new RectangleGeometry(plot));
            var perChannel = ChannelCurves;
            if (perChannel != null && perChannel.Length == 4) {
                foreach (int ch in new[] { Channel.R, Channel.G, Channel.B }) {
                    if (ShowColor && !((ch == Channel.R && ShowR) || (ch == Channel.G && ShowG) || (ch == Channel.B && ShowB))) continue;
                    // pendant le glissement, les canaux réglés suivent directement les curseurs
                    var cc = dragHandle >= 0 ? c : perChannel[ch];
                    dc.DrawGeometry(null, ChannelCurvePens[ch], CurveGeometry(cc, plot, vmin, vmax, cols));
                }
            } else {
                dc.DrawGeometry(null, CurvePen, CurveGeometry(c, plot, vmin, vmax, cols));
            }
            dc.Pop();

            // bande : niveau de gris de sortie pour chaque valeur d'entrée
            double top = plot.Bottom + StripTop;
            const int band = 3;
            for (int x = 0; x < (int)plot.Width; x += band) {
                double v = vmin + (vmax - vmin) * (x + band / 2.0) / plot.Width;
                byte gray = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, ImageStretch.Curve(c, v))));
                var b = new SolidColorBrush(Color.FromRgb(gray, gray, gray));
                b.Freeze();
                dc.DrawRectangle(b, null, new Rect(plot.Left + x, top, Math.Min(band, plot.Width - x), StripHeight));
            }

            DrawHandle(dc, plot, XOf(plot, Shadows, vmin, vmax), Brushes.Black);
            DrawHandle(dc, plot, XOf(plot, MidPosition, vmin, vmax), Brushes.Gray);
            DrawHandle(dc, plot, XOf(plot, Highlights, vmin, vmax), Brushes.White);
        }

        /// <summary>Courbe appliquée à un canal : pendant le glissement, celle des curseurs pour les canaux affichés.</summary>
        private CurveParams CurveFor(int ch) {
            var per = ChannelCurves;
            if (per == null || per.Length != 4) return CurrentCurve;
            // pendant le glissement, seuls les canaux réglés (ceux affichés en dessous) suivent les curseurs
            if (dragHandle >= 0 && IsEdited(ch)) return CurrentCurve;
            return per[ch];
        }

        /// <summary>Canal modifié par les curseurs : en mode RVB, les canaux affichés ; en mode Mono, tous.</summary>
        private bool IsEdited(int ch) =>
            !ShowColor || (ch == Channel.R && ShowR) || (ch == Channel.G && ShowG) || (ch == Channel.B && ShowB);

        /// <summary>
        /// Histogramme de l'image étirée, sur la plage de sortie [lo, hi] (0..1) découpée en cols colonnes.
        /// Chaque tranche d'entrée (Step ADU) couvre l'intervalle de sortie [courbe(v − Step/2), courbe(v + Step/2)] :
        /// ses pixels y sont répartis uniformément. On obtient une densité continue, sans « marches »
        /// même quand la courbe étire beaucoup (une tranche d'entrée peut alors couvrir plusieurs colonnes).
        /// </summary>
        private double[] OutputCounts(HistogramSet h, int ch, double lo, double hi, int cols) {
            var o = new double[cols];
            int step = Math.Max(1, h.Step);
            double span = hi - lo;
            if (span <= 0) return o;

            void Spread(double ya, double yb, double n) {
                double fa = (ya - lo) / span * cols, fb = (yb - lo) / span * cols;
                if (fb < 0 || fa > cols) return;
                if (fb - fa < 1e-9) {
                    int i = (int)Math.Floor(fa);
                    if (i == cols) i = cols - 1;
                    if (i >= 0 && i < cols) o[i] += n;
                    return;
                }
                int i0 = Math.Max(0, (int)Math.Floor(fa)), i1 = Math.Min(cols - 1, (int)Math.Floor(fb));
                double w = fb - fa;
                for (int i = i0; i <= i1; i++) {
                    double ov = Math.Min(fb, i + 1) - Math.Max(fa, i);
                    if (ov > 0) o[i] += n * ov / w;
                }
            }

            void Add(int src, CurveParams c) {
                var counts = h.Counts[src];
                if (counts == null) return;
                for (int v0 = 0; v0 < counts.Length; v0 += step) {
                    long n = 0;
                    int end = Math.Min(counts.Length, v0 + step);
                    for (int v = v0; v < end; v++) n += counts[v];
                    if (n == 0) continue;
                    double ya = ImageStretch.Curve(c, v0 - step / 2.0), yb = ImageStretch.Curve(c, v0 + step / 2.0);
                    Spread(ya, yb, n);
                }
            }

            if (ch == Channel.L && h.IsColor) {
                // tous les pixels d'une image couleur : chaque canal avec sa propre courbe
                Add(Channel.R, CurveFor(Channel.R));
                Add(Channel.G, CurveFor(Channel.G));
                Add(Channel.B, CurveFor(Channel.B));
            } else {
                Add(ch, CurveFor(ch));
            }
            return o;
        }

        private void DrawOutput(DrawingContext dc, Rect r, double dpi, HistogramSet h) {
            dc.DrawLine(AxisPen, new Point(r.Left, r.Bottom - 12), new Point(r.Right, r.Bottom - 12));

            // toujours les trois canaux pour une image couleur, même si on n'en règle qu'un seul en dessous :
            // on voit ainsi l'équilibre des couleurs pendant le réglage
            var outChannels = h.IsColor ? new[] { Channel.R, Channel.G, Channel.B } : new[] { Channel.L };

            // 1. plage horizontale automatique (passe grossière sur 0..1) : de 0,01 % à 99,9 % des pixels,
            //    pour que la forme reste lisible même sans étirement
            const int coarse = 1024;
            var total = new double[coarse];
            foreach (var ch in outChannels) {
                var y = OutputCounts(h, ch, 0, 1, coarse);
                for (int i = 0; i < coarse; i++) total[i] += y[i];
            }
            int b0 = BinAt(total, 0.0001), b1 = BinAt(total, 0.999);
            double lo = (double)b0 / coarse, hi = (double)(b1 + 1) / coarse;
            double pad = 0.05 * (hi - lo);
            lo = Math.Max(0, lo - pad);
            hi = Math.Min(1, hi + pad);
            if (hi - lo < 0.02) { double mid = (lo + hi) / 2; lo = Math.Max(0, mid - 0.01); hi = Math.Min(1, lo + 0.02); }
            // pendant le glissement, la plage ne fait que s'élargir : la courbe ne « saute » pas sous la souris
            if (dragHandle >= 0 && outRange.hi > outRange.lo) {
                lo = Math.Min(lo, outRange.lo);
                hi = Math.Max(hi, outRange.hi);
            }
            outRange = (lo, hi);

            // 2. passe fine : une valeur par pixel d'écran, puis léger lissage (5 colonnes) si « Lisser » est actif
            int cols = Math.Max(2, (int)r.Width);
            var series = new List<(int ch, double[] y)>();
            double yMax = 0;
            foreach (var ch in outChannels) {
                var y = OutputCounts(h, ch, lo, hi, cols);
                if (Smooth) y = SmoothColumns(y, 2);
                // noir et blanc purs (pixels écrêtés) exclus de l'échelle
                int first = lo <= 0 ? 2 : 0, last = hi >= 1 ? cols - 3 : cols - 1;
                for (int i = first; i <= last; i++) yMax = Math.Max(yMax, y[i]);
                series.Add((ch, y));
            }
            if (yMax <= 0) yMax = 1;
            double logMax = Math.Log10(1 + yMax);

            // repères en % de la pleine échelle de sortie
            double tick = NicePercentStep((hi - lo) * 100 / 4);
            for (double t = Math.Ceiling(lo * 100 / tick) * tick; t <= hi * 100 + 1e-9; t += tick) {
                double x = r.Left + (t / 100 - lo) / (hi - lo) * r.Width;
                dc.DrawLine(GridPen, new Point(x, r.Top), new Point(x, r.Bottom - 12));
                var tt = Text(t.ToString(tick < 1 ? "0.0" : "0", Fr) + " %", dpi, 9, TextBrush);
                dc.DrawText(tt, new Point(Math.Max(r.Left, Math.Min(r.Right - tt.Width, x - tt.Width / 2)), r.Bottom - tt.Height));
            }

            dc.PushClip(new RectangleGeometry(new Rect(r.Left, r.Top, r.Width, Math.Max(1, r.Height - 12))));
            foreach (var (ch, y) in series) {
                bool mono = ch == Channel.L;
                var g = new StreamGeometry();
                using (var ctx = g.Open()) {
                    Point P(int i) {
                        double f = LogScale ? Math.Log10(1 + y[i]) / logMax : y[i] / yMax;
                        f = Math.Max(0, Math.Min(1, f)) * 0.85;
                        return new Point(r.Left + (i + 0.5) * r.Width / cols, r.Bottom - 12 - f * (r.Height - 12));
                    }
                    if (mono) {
                        ctx.BeginFigure(new Point(r.Left, r.Bottom - 12), true, true);
                        for (int i = 0; i < cols; i++) ctx.LineTo(P(i), true, true);
                        ctx.LineTo(new Point(r.Right, r.Bottom - 12), false, true);
                    } else {
                        ctx.BeginFigure(P(0), false, false);
                        for (int i = 1; i < cols; i++) ctx.LineTo(P(i), true, true);
                    }
                }
                g.Freeze();
                dc.DrawGeometry(mono ? MonoFill : null, SeriesPens[ch], g);
            }
            dc.Pop();

            DrawText(dc, "après étirement (% de la pleine échelle)", new Point(r.Left + 4, r.Top + 2), dpi, 10);
        }

        /// <summary>Moyenne glissante sur ±half colonnes.</summary>
        private static double[] SmoothColumns(double[] y, int half) {
            var o = new double[y.Length];
            for (int i = 0; i < y.Length; i++) {
                double s = 0;
                int n = 0;
                for (int k = Math.Max(0, i - half); k <= Math.Min(y.Length - 1, i + half); k++) { s += y[k]; n++; }
                o[i] = s / n;
            }
            return o;
        }

        private (double lo, double hi) outRange;

        private static int BinAt(double[] h, double q) {
            double n = 0;
            foreach (var v in h) n += v;
            if (n <= 0) return 0;
            double target = q * n, acc = 0;
            for (int i = 0; i < h.Length; i++) {
                acc += h[i];
                if (acc >= target) return i;
            }
            return h.Length - 1;
        }

        private static double NicePercentStep(double raw) {
            foreach (var st in new[] { 0.1, 0.2, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 25.0 }) if (st >= raw) return st;
            return 25;
        }

        private static StreamGeometry CurveGeometry(CurveParams c, Rect plot, double vmin, double vmax, int cols) {
            var g = new StreamGeometry();
            using (var ctx = g.Open()) {
                for (int i = 0; i <= cols; i++) {
                    double v = vmin + (vmax - vmin) * i / cols;
                    var p = new Point(plot.Left + plot.Width * i / cols, plot.Bottom - ImageStretch.Curve(c, v) * plot.Height * 0.92);
                    if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
                }
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Curseur triangulaire. S'il est hors de la plage zoomée, il reste visible au bord, en forme de flèche
        /// pointant vers sa vraie position : on peut l'attraper et le ramener dans la vue.
        /// </summary>
        private void DrawHandle(DrawingContext dc, Rect plot, double x, Brush fill) {
            double top = plot.Bottom + StripTop;
            var g = new StreamGeometry();
            using (var ctx = g.Open()) {
                if (x < plot.Left) {
                    double e = plot.Left;
                    ctx.BeginFigure(new Point(e, top + StripHeight / 2), true, true);
                    ctx.LineTo(new Point(e + 10, top), true, true);
                    ctx.LineTo(new Point(e + 10, top + StripHeight), true, true);
                } else if (x > plot.Right) {
                    double e = plot.Right;
                    ctx.BeginFigure(new Point(e, top + StripHeight / 2), true, true);
                    ctx.LineTo(new Point(e - 10, top), true, true);
                    ctx.LineTo(new Point(e - 10, top + StripHeight), true, true);
                } else {
                    ctx.BeginFigure(new Point(x, top), true, true);
                    ctx.LineTo(new Point(x - 6, top + StripHeight), true, true);
                    ctx.LineTo(new Point(x + 6, top + StripHeight), true, true);
                }
            }
            g.Freeze();
            dc.DrawGeometry(fill, HandlePen, g);
        }

        /// <summary>Position affichée d'un curseur : sa vraie position, ou le bord de la vue s'il en sort.</summary>
        private static double PinnedX(Rect plot, double x) => Math.Max(plot.Left + 5, Math.Min(plot.Right - 5, x));

        /// <summary>Curseur sous le pointeur : 0 = noir, 1 = moyen, 2 = blanc, -1 = aucun.</summary>
        private int HitHandle(Point p) {
            if (!ShowCurve || Histograms == null) return -1;
            var plot = PlotRect;
            if (p.Y < plot.Bottom || p.Y > plot.Bottom + StripTop + StripHeight + 4) return -1;
            double[] xs = {
                PinnedX(plot, XOf(plot, Shadows, ViewMin, ViewMax)),
                PinnedX(plot, XOf(plot, MidPosition, ViewMin, ViewMax)),
                PinnedX(plot, XOf(plot, Highlights, ViewMin, ViewMax))
            };
            int best = -1;
            double bestD = 8;
            // en cas d'égalité (curseurs superposés), le curseur moyen passe en dernier pour rester attrapable
            foreach (int i in new[] { 0, 2, 1 }) {
                double d = Math.Abs(p.X - xs[i]);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private void DragHandle(int handle, double v) {
            double step = Histograms != null ? Math.Max(1, Histograms.Step) : 1;
            double full = Histograms != null ? Histograms.FullScale : 65535;
            double gap = 2 * step;
            switch (handle) {
                case 0:
                    Shadows = Math.Max(0, Math.Min(Highlights - gap, v));
                    break;
                case 1:
                    double range = Highlights - Shadows;
                    if (range > 0) Midtone = Math.Max(0.001, Math.Min(0.999, (v - Shadows) / range));
                    break;
                case 2:
                    Highlights = Math.Min(full, Math.Max(Shadows + gap, v));
                    break;
            }
        }

        #endregion

        /// <summary>Hauteur de chaque colonne : nombre moyen de pixels par tranche de Step ADU.</summary>
        private double[] Columns(HistogramSet h, int ch, double vmin, double vmax, int cols, out double yMax) {
            var y = new double[cols];
            yMax = 0;
            var counts = h?.Counts[ch];
            if (counts == null) return y;
            int step = Math.Max(1, h.Step);
            int firstBin = 0, satBin = Math.Max(1, (h.FullScale - step + 1) / step);
            var agg = Smooth ? Smoothed(counts, step, satBin) : Aggregate(counts, step);
            double w = (vmax - vmin) / cols;
            for (int c = 0; c < cols; c++) {
                double a = vmin + c * w, b = a + w;
                int i0 = (int)Math.Ceiling(a / step), i1 = (int)Math.Ceiling(b / step) - 1;
                double v;
                bool counted;
                if (i1 >= i0) {
                    i0 = Math.Max(0, i0);
                    i1 = Math.Min(agg.Length - 1, i1);
                    double sum = 0, sumIn = 0;
                    int n = 0, nIn = 0;
                    for (int i = i0; i <= i1; i++) {
                        sum += agg[i];
                        n++;
                        if (i == firstBin || i >= satBin) continue;
                        sumIn += agg[i];
                        nIn++;
                    }
                    v = n > 0 ? sum / n : 0;
                    counted = nIn > 0;
                    if (counted) yMax = Math.Max(yMax, sumIn / nIn);
                } else {
                    int i = (int)Math.Floor((a + b) / 2 / step);
                    v = i >= 0 && i < agg.Length ? agg[i] : 0;
                    counted = !(i == firstBin || i >= satBin);
                    if (counted) yMax = Math.Max(yMax, v);
                }
                y[c] = v;
            }
            return y;
        }

        private void ColumnsMax(HistogramSet h, int ch, double vmin, double vmax, int cols, ref double yMax) {
            Columns(h, ch, vmin, vmax, cols, out double m);
            yMax = Math.Max(yMax, m);
        }

        private StreamGeometry BuildGeometry(double[] y, Rect plot, double yMax, int cols, bool closed) {
            var g = new StreamGeometry();
            double logMax = Math.Log10(1 + yMax);
            using (var ctx = g.Open()) {
                Point P(int c) {
                    double f = LogScale ? Math.Log10(1 + y[c]) / logMax : y[c] / yMax;
                    // le sommet occupe 92 % de la hauteur : la courbe ne touche jamais le bord ni les légendes
                    f = Math.Max(0, Math.Min(1, f)) * 0.92;
                    return new Point(plot.Left + (c + 0.5) * plot.Width / cols, plot.Bottom - f * plot.Height);
                }
                if (closed) {
                    ctx.BeginFigure(new Point(plot.Left, plot.Bottom), true, true);
                    for (int c = 0; c < cols; c++) ctx.LineTo(P(c), true, true);
                    ctx.LineTo(new Point(plot.Right, plot.Bottom), false, true);
                } else {
                    ctx.BeginFigure(P(0), false, false);
                    for (int c = 1; c < cols; c++) ctx.LineTo(P(c), true, true);
                }
            }
            g.Freeze();
            return g;
        }

        private void DrawGridAndAxis(DrawingContext dc, Rect plot, double vmin, double vmax, double dpi) {
            double range = vmax - vmin;
            double tick = NiceStep(range / Math.Max(2, plot.Width / 90));
            for (double v = Math.Ceiling(vmin / tick) * tick; v <= vmax; v += tick) {
                double x = plot.Left + (v - vmin) / range * plot.Width;
                dc.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var t = Text(Fmt(v), dpi, 10, TextBrush);
                double tx = Math.Max(0, Math.Min(ActualWidth - t.Width, x - t.Width / 2));
                dc.DrawText(t, new Point(tx, plot.Bottom + StripTop + StripHeight + 4));
            }
            for (int i = 1; i < 4; i++) {
                double y = plot.Top + plot.Height * i / 4;
                dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            }
            dc.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        }

        private void DrawMarker(DrawingContext dc, Rect plot, double vmin, double vmax, double dpi, HistogramSet h, List<int> channels) {
            if (double.IsNaN(markerValue) || markerValue < vmin || markerValue > vmax) return;
            int step = Math.Max(1, h.Step);
            int bin = (int)Math.Floor(markerValue / step);
            double x = plot.Left + (bin * step + step / 2.0 - vmin) / (vmax - vmin) * plot.Width;
            dc.DrawLine(MarkerPen, new Point(x, plot.Top), new Point(x, plot.Bottom));

            var lines = new List<(string text, Brush brush)>();
            string xs = step > 1 ? $"x = {Fmt(bin * step)} à {Fmt(bin * step + step - 1)} ADU" : $"x = {Fmt(bin * step)} ADU";
            lines.Add((xs, TextBrush));
            foreach (var ch in channels) {
                var agg = Aggregate(h.Counts[ch], step);
                long n = bin >= 0 && bin < agg.Length ? agg[bin] : 0;
                lines.Add(($"y{(ch == Channel.L ? "" : " " + Channel.Names[ch])} = {Fmt(n)} px", SeriesPens[ch].Brush));
            }
            if (Reference != null) {
                foreach (var ch in VisibleChannels(Reference)) {
                    int rstep = Math.Max(1, Reference.Step);
                    var agg = Aggregate(Reference.Counts[ch], rstep);
                    int rb = (int)Math.Floor(markerValue / rstep);
                    long n = rb >= 0 && rb < agg.Length ? agg[rb] : 0;
                    lines.Add(($"réf.{(ch == Channel.L ? "" : " " + Channel.Names[ch])} = {Fmt(n)} px", RefPen.Brush));
                }
            }

            var texts = new List<FormattedText>();
            double wMax = 0, hTot = 0;
            foreach (var l in lines) {
                var t = Text(l.text, dpi, 11, l.brush);
                texts.Add(t);
                wMax = Math.Max(wMax, t.Width);
                hTot += t.Height;
            }
            double bx = x + 8;
            if (bx + wMax + 10 > plot.Right) bx = x - wMax - 18;
            double by = plot.Top + 16;
            dc.DrawRoundedRectangle(LabelBackground, MarkerPen, new Rect(bx, by, wMax + 10, hTot + 6), 3, 3);
            double ty = by + 3;
            foreach (var t in texts) {
                dc.DrawText(t, new Point(bx + 5, ty));
                ty += t.Height;
            }
        }

        private FormattedText Text(string s, double dpi, double size, Brush brush) =>
            new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi);

        private void DrawText(DrawingContext dc, string s, Point p, double dpi, double size) => dc.DrawText(Text(s, dpi, size, TextBrush), p);

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private static string Fmt(double v) => v.ToString("#,0", Fr);

        private static double NiceStep(double raw) {
            if (!(raw > 0)) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw / mag;
            double nice = f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10;
            return Math.Max(1, nice * mag);
        }

        #endregion

        #region Souris

        private bool dragging, pressed;
        private int dragHandle = -1;
        private DateTime lastChanging = DateTime.MinValue;
        private Point pressPoint;
        private double pressMin, pressMax;

        private double DataLimit => Histograms != null ? Histograms.FullScale + Math.Max(1, Histograms.Step) : 65536;

        private double MinSpan => Histograms != null ? Math.Max(16, 8 * Histograms.Step) : 16;

        private double ValueAt(double x) {
            var plot = PlotRect;
            return ViewMin + (x - plot.Left) / plot.Width * (ViewMax - ViewMin);
        }

        /// <summary>Applique une plage en la gardant dans les données et au-dessus de la largeur minimale.</summary>
        private void SetView(double min, double max) {
            double limit = DataLimit;
            double w = Math.Max(MinSpan, max - min);
            if (w >= limit) { min = 0; max = limit; } else {
                if (min < 0) min = 0;
                max = min + w;
                if (max > limit) { max = limit; min = limit - w; }
            }
            ViewMin = min;
            ViewMax = max;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e) {
            base.OnMouseWheel(e);
            double v = ValueAt(e.GetPosition(this).X);
            double f = e.Delta > 0 ? 0.8 : 1.25;
            SetView(v - (v - ViewMin) * f, v + (ViewMax - v) * f);
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
            base.OnMouseLeftButtonDown(e);
            Focus();
            if (e.ClickCount == 2) {
                SetView(0, DataLimit);
                e.Handled = true;
                return;
            }
            dragHandle = HitHandle(e.GetPosition(this));
            if (dragHandle >= 0) {
                CaptureMouse();
                e.Handled = true;
                return;
            }
            pressed = true;
            dragging = false;
            pressPoint = e.GetPosition(this);
            pressMin = ViewMin;
            pressMax = ViewMax;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (dragHandle >= 0) {
                DragHandle(dragHandle, ValueAt(e.GetPosition(this).X));
                InvalidateVisual();
                var now = DateTime.UtcNow;
                if ((now - lastChanging).TotalMilliseconds >= 100) {
                    lastChanging = now;
                    var cmd = CurveChangingCommand;
                    if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
                }
                return;
            }
            if (!pressed) {
                Cursor = HitHandle(e.GetPosition(this)) >= 0 ? Cursors.SizeWE : Cursors.Cross;
                return;
            }
            var p = e.GetPosition(this);
            double dx = p.X - pressPoint.X;
            if (!dragging && Math.Abs(dx) > 3) {
                dragging = true;
                Cursor = Cursors.SizeWE;
            }
            if (dragging) {
                double shift = -dx / PlotRect.Width * (pressMax - pressMin);
                SetView(pressMin + shift, pressMax + shift);
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
            base.OnMouseLeftButtonUp(e);
            if (dragHandle >= 0) {
                dragHandle = -1;
                ReleaseMouseCapture();
                var cmd = CurveCommittedCommand;
                if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
                e.Handled = true;
                return;
            }
            if (!pressed) return;
            pressed = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Cross;
            if (!dragging) {
                markerValue = ValueAt(e.GetPosition(this).X);
                InvalidateVisual();
            }
            dragging = false;
            e.Handled = true;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) {
            base.OnMouseRightButtonUp(e);
            markerValue = double.NaN;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e) {
            base.OnLostMouseCapture(e);
            if (dragHandle >= 0) {
                dragHandle = -1;
                var cmd = CurveCommittedCommand;
                if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
            }
            pressed = false;
            dragging = false;
            Cursor = Cursors.Cross;
        }

        #endregion
    }
}
