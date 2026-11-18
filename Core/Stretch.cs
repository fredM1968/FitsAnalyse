using System;
using System.Threading.Tasks;

namespace FitsAnalyse.Core {

    /// <summary>Paramètres de la courbe (façon Photoshop / Siril) : point noir, tons moyens, point blanc.</summary>
    public struct CurveParams {
        public double Shadows;      // ADU
        public double Midtone;      // 0..1 : position relative du curseur « moyen » entre le noir et le blanc
        public double Highlights;   // ADU

        public static CurveParams Identity(double fullScale) => new CurveParams { Shadows = 0, Midtone = 0.5, Highlights = fullScale };

        public bool IsIdentity(double fullScale) =>
            Shadows <= 0 && Math.Abs(Midtone - 0.5) < 1e-6 && Highlights >= fullScale;
    }

    /// <summary>Balance des blancs : valeur corrigée = Gain × valeur + Offset, par canal (index Channel.R/G/B).</summary>
    public sealed class WhiteBalance {
        public double[] Gain = { 1, 1, 1, 1 };
        public double[] Offset = { 0, 0, 0, 0 };

        public double Apply(int ch, double v) => Gain[ch] * v + Offset[ch];
    }

    /// <summary>Étirement de l'image pour l'affichage : balance des blancs puis fonction de transfert des tons moyens (MTF).</summary>
    public static class ImageStretch {

        /// <summary>Fonction de transfert des tons moyens de PixInsight / Siril : MTF(m, 0) = 0, MTF(m, m) = 0,5, MTF(m, 1) = 1.</summary>
        public static double Mtf(double m, double x) {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            if (Math.Abs(m - 0.5) < 1e-12) return x;
            return (m - 1) * x / ((2 * m - 1) * x - m);
        }

        /// <summary>Valeur de sortie (0..1) de la courbe pour une valeur d'entrée en ADU.</summary>
        public static double Curve(CurveParams c, double adu) {
            double range = c.Highlights - c.Shadows;
            if (range <= 0) return adu >= c.Highlights ? 1 : 0;
            return Mtf(c.Midtone, (adu - c.Shadows) / range);
        }

        /// <summary>
        /// Étirement automatique, comme le bouton Autostretch de NINA : le noir est placé à « médiane + BlackClipping × σ »
        /// (σ = 1,4826 × MAD, BlackClipping = −2,8 par défaut) et les tons moyens amènent la médiane au niveau visé
        /// (AutoStretchFactor, 0,2 par défaut). Le blanc reste à la pleine échelle.
        /// </summary>
        public static CurveParams Auto(ChannelStats s, double fullScale, double targetBackground, double blackClipping) {
            if (s == null || fullScale <= 0) return CurveParams.Identity(fullScale);
            if (!(targetBackground > 0 && targetBackground < 1)) targetBackground = 0.2;
            if (double.IsNaN(blackClipping)) blackClipping = -2.8;
            double shadows = Math.Max(0, s.Median + blackClipping * s.RobustSigma);
            if (shadows >= s.Median) shadows = Math.Max(0, s.Median - Math.Max(1, s.RobustSigma));
            double x0 = (s.Median - shadows) / (fullScale - shadows);
            // la valeur m telle que MTF(m, x0) = cible vaut MTF(cible, x0)
            double m = Mtf(targetBackground, x0);
            m = Math.Max(1e-4, Math.Min(0.9999, m));
            return new CurveParams { Shadows = shadows, Midtone = m, Highlights = fullScale };
        }

        /// <summary>
        /// Balance des blancs automatique : aligne les canaux R et B sur le vert.
        /// Si le niveau du bias est connu, c'est un vrai gain (le fond de ciel devient neutre, le noir reste au bias) ;
        /// sinon on aligne les pics et leurs largeurs (médiane et σ robuste).
        /// </summary>
        public static WhiteBalance AutoWhiteBalance(AnalysisResult res, double biasLevel) {
            var wb = new WhiteBalance();
            if (res == null || !res.IsColor) return wb;
            var g = res.Stats[Channel.G];
            foreach (var c in new[] { Channel.R, Channel.B }) {
                var s = res.Stats[c];
                if (s == null || g == null) continue;
                if (biasLevel > 0 && s.Median - biasLevel > 5 && g.Median - biasLevel > 5) {
                    double gain = (g.Median - biasLevel) / (s.Median - biasLevel);
                    wb.Gain[c] = gain;
                    wb.Offset[c] = biasLevel * (1 - gain);
                } else if (s.RobustSigma > 0 && g.RobustSigma > 0) {
                    double gain = g.RobustSigma / s.RobustSigma;
                    wb.Gain[c] = gain;
                    wb.Offset[c] = g.Median - gain * s.Median;
                }
            }
            return wb;
        }

        /// <summary>
        /// Histogrammes après balance des blancs (pour voir les canaux alignés).
        /// Un gain non entier (×1,87 par exemple) écarte les valeurs de façon irrégulière : en recopiant simplement
        /// chaque valeur à sa nouvelle place, on obtient un histogramme en dents de scie. On répartit donc les pixels
        /// de chaque tranche d'entrée [v − pas/2, v + pas/2] uniformément sur l'intervalle de sortie correspondant.
        /// </summary>
        public static HistogramSet ApplyToHistograms(HistogramSet src, WhiteBalance wb) {
            if (src == null || wb == null || !src.IsColor) return src;
            var dst = new HistogramSet { FullScale = src.FullScale, Step = src.Step };
            int step = Math.Max(1, src.Step);
            var total = new double[HistogramSet.Size];
            for (int c = Channel.R; c <= Channel.B; c++) {
                var from = src.Counts[c];
                var acc = new double[HistogramSet.Size];
                double gain = wb.Gain[c], offset = wb.Offset[c];
                for (int v = 0; v < from.Length; v++) {
                    int n = from[v];
                    if (n == 0) continue;
                    double lo = gain * (v - step / 2.0) + offset, hi = gain * (v + step / 2.0) + offset;
                    Spread(acc, lo, hi, n);
                }
                dst.Counts[c] = ToCounts(acc);
                for (int i = 0; i < acc.Length; i++) total[i] += acc[i];
            }
            dst.Counts[Channel.L] = ToCounts(total);
            return dst;
        }

        /// <summary>Répartit n pixels uniformément sur l'intervalle [lo, hi[ (en ADU), tranche de 1 ADU par tranche de 1 ADU.</summary>
        private static void Spread(double[] acc, double lo, double hi, double n) {
            int max = acc.Length - 1;
            if (hi <= lo) {
                int i = (int)Math.Max(0, Math.Min(max, Math.Floor(lo)));
                acc[i] += n;
                return;
            }
            if (hi <= 0) { acc[0] += n; return; }
            if (lo >= max) { acc[max] += n; return; }
            double w = hi - lo;
            int i0 = (int)Math.Floor(Math.Max(0, lo)), i1 = (int)Math.Min(max, Math.Floor(hi));
            // ce qui sort de la plage est compté au bord (pixels écrêtés)
            if (lo < 0) acc[0] += n * (-lo) / w;
            if (hi > max + 1) acc[max] += n * (hi - max - 1) / w;
            for (int i = i0; i <= i1; i++) {
                double ov = Math.Min(hi, i + 1) - Math.Max(lo, i);
                if (ov > 0) acc[i] += n * ov / w;
            }
        }

        /// <summary>Arrondit des comptes fractionnaires en reportant l'erreur, pour conserver le nombre total de pixels.</summary>
        private static int[] ToCounts(double[] acc) {
            var o = new int[acc.Length];
            double carry = 0;
            for (int i = 0; i < acc.Length; i++) {
                double v = acc[i] + carry;
                int r = (int)Math.Round(v);
                if (r < 0) r = 0;
                o[i] = r;
                carry = v - r;
            }
            return o;
        }

        /// <summary>
        /// Construit l'image étirée (16 bits) à partir des pixels bruts : une table de correspondance par position
        /// de la matrice de Bayer (ou une seule en monochrome), appliquée en parallèle.
        /// </summary>
        /// <param name="curves">Courbe par canal, indexée par Channel (L pour une image monochrome).</param>
        public static ushort[] Render(ushort[] src, int width, int height, string bayerPattern, WhiteBalance wb, CurveParams[] curves) {
            var lut = new ushort[4][];
            int[] colors = { Channel.L, Channel.L, Channel.L, Channel.L };
            var pattern = ImageAnalysis.NormalizePattern(bayerPattern);
            if (pattern != null) {
                for (int i = 0; i < 4; i++) colors[i] = pattern[i] switch { 'R' => Channel.R, 'G' => Channel.G, _ => Channel.B };
            }
            var cache = new ushort[4][];
            for (int i = 0; i < 4; i++) {
                int ch = colors[i];
                if (cache[ch] == null) {
                    var t = new ushort[HistogramSet.Size];
                    for (int v = 0; v < t.Length; v++) {
                        double x = wb != null && ch != Channel.L ? wb.Apply(ch, v) : v;
                        double y = Curve(curves[ch], x);
                        t[v] = (ushort)Math.Round(Math.Max(0, Math.Min(1, y)) * 65535);
                    }
                    cache[ch] = t;
                }
                lut[i] = cache[ch];
            }

            var dst = new ushort[(long)width * height];
            Parallel.For(0, height, y => {
                long row = (long)y * width;
                var l0 = lut[(y & 1) * 2];
                var l1 = lut[(y & 1) * 2 + 1];
                for (int x = 0; x < width; x++) {
                    ushort v = src[row + x];
                    dst[row + x] = (x & 1) == 0 ? l0[v] : l1[v];
                }
            });
            return dst;
        }
    }
}
