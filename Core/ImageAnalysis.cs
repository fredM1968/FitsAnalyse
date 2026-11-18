using System;

namespace FitsAnalyse.Core {

    /// <summary>Statistiques d'un canal (valeurs en ADU du fichier).</summary>
    public sealed class ChannelStats {
        public long Count;
        public double Mean, StdDev, Median, Mad;
        public int Min, Max;
        public long MinCount, MaxCount;
        public long SaturatedCount;

        /// <summary>
        /// Bruit d'un pixel du fond de ciel (ADU), mesuré par différences de pixels voisins de même couleur :
        /// insensible aux gradients et aux nébuleuses, et robuste vis-à-vis des étoiles.
        /// </summary>
        public double PixelNoise = double.NaN;

        /// <summary>Écart-type robuste déduit de la MAD (1,4826 × MAD).</summary>
        public double RobustSigma => 1.4826 * Mad;

        public double SaturatedFraction => Count > 0 ? (double)SaturatedCount / Count : 0;
    }

    /// <summary>Résultat de l'analyse d'une image.</summary>
    public sealed class AnalysisResult {
        public int Width, Height;
        public string BayerPattern;            // "RGGB", "BGGR"… ou null en monochrome
        public bool IsColor => BayerPattern != null;
        public HistogramSet Histograms;
        public ChannelStats[] Stats = new ChannelStats[4];
    }

    /// <summary>Calcul des histogrammes et des statistiques sur les pixels bruts (non dématricés).</summary>
    public static class ImageAnalysis {

        private static readonly int[] FullScales = { 255, 1023, 4095, 16383, 65535 };

        /// <summary>
        /// Analyse une image brute. Pour une caméra couleur, les canaux R, V et B sont séparés selon la matrice de Bayer
        /// (sans dématriçage : chaque pixel garde sa vraie valeur).
        /// </summary>
        public static AnalysisResult Analyze(ushort[] pixels, int width, int height, string bayerPattern) {
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length < (long)width * height)
                throw new ArgumentException("image vide ou incohérente");

            var pattern = NormalizePattern(bayerPattern);
            var map = ColorMap(pattern);   // couleur de chaque position (x & 1, y & 1), ou null en mono

            var res = new AnalysisResult { Width = width, Height = height, BayerPattern = pattern };
            var hs = new HistogramSet();
            hs.Counts[Channel.L] = new int[HistogramSet.Size];
            if (map != null) {
                hs.Counts[Channel.R] = new int[HistogramSet.Size];
                hs.Counts[Channel.G] = new int[HistogramSet.Size];
                hs.Counts[Channel.B] = new int[HistogramSet.Size];
            }

            // 1. Histogrammes
            var hl = hs.Counts[Channel.L];
            if (map == null) {
                for (long i = 0; i < (long)width * height; i++) hl[pixels[i]]++;
            } else {
                for (int y = 0; y < height; y++) {
                    long row = (long)y * width;
                    var c0 = hs.Counts[map[(y & 1) * 2]];
                    var c1 = hs.Counts[map[(y & 1) * 2 + 1]];
                    for (int x = 0; x < width; x++) {
                        ushort v = pixels[row + x];
                        hl[v]++;
                        if ((x & 1) == 0) c0[v]++; else c1[v]++;
                    }
                }
            }

            // 2. Pas réel des valeurs (4 pour un capteur 14 bits, 16 pour 12 bits…) et pleine échelle.
            //    Quelques pixels hors du pas (corrections du pilote, pixels chauds) ne doivent pas l'annuler :
            //    on garde le plus grand pas que respectent au moins 99,9 % des pixels.
            int maxValue = 0;
            long total = 0;
            for (int v = 1; v < HistogramSet.Size; v++) {
                if (hl[v] == 0) continue;
                maxValue = v;
                total += hl[v];
            }
            int step = 1;
            for (int sp = 64; sp >= 2 && total > 0; sp /= 2) {
                long off = 0;
                for (int v = 1; v < HistogramSet.Size; v++) {
                    if ((v & (sp - 1)) != 0) off += hl[v];
                }
                if (off <= total / 1000) { step = sp; break; }
            }
            // Si les valeurs ne tombent pas exactement sur une grille (gain du pilote non entier, par exemple ×4,0002),
            // le test précédent échoue alors que l'histogramme est quand même « en peigne ».
            // On regarde donc aussi l'occupation réelle : dans le cœur de l'histogramme (5 % à 95 % des pixels),
            // on prend le plus petit pas pour lequel presque toutes les tranches contiennent des pixels.
            step = Math.Max(step, OccupancyStep(hl, total));
            hs.Step = step;
            hs.FullScale = 65535;
            foreach (var fs in FullScales) {
                if (maxValue <= fs) { hs.FullScale = fs; break; }
            }
            int saturation = Math.Max(1, hs.FullScale - 2 * hs.Step + 1);

            // 3. Statistiques par canal
            for (int c = 0; c < 4; c++) {
                if (hs.Counts[c] != null) res.Stats[c] = StatsFromHistogram(hs.Counts[c], saturation);
            }

            // 4. Bruit pixel à pixel (différences entre pixels de même couleur distants de 2 colonnes)
            var diff = new int[4][];
            diff[Channel.L] = new int[HistogramSet.Size];
            if (map != null) {
                diff[Channel.R] = new int[HistogramSet.Size];
                diff[Channel.G] = new int[HistogramSet.Size];
                diff[Channel.B] = new int[HistogramSet.Size];
            }
            for (int y = 0; y < height; y++) {
                long row = (long)y * width;
                int[] d0 = map != null ? diff[map[(y & 1) * 2]] : null;
                int[] d1 = map != null ? diff[map[(y & 1) * 2 + 1]] : null;
                for (int x = 0; x + 2 < width; x++) {
                    int a = pixels[row + x], b = pixels[row + x + 2];
                    if (a == 0 || b == 0 || a >= saturation || b >= saturation) continue;
                    int d = a > b ? a - b : b - a;
                    diff[Channel.L][d]++;
                    if (map != null) ((x & 1) == 0 ? d0 : d1)[d]++;
                }
            }
            for (int c = 0; c < 4; c++) {
                if (res.Stats[c] != null) res.Stats[c].PixelNoise = SigmaFromAbsDiff(diff[c]) / Math.Sqrt(2);
            }

            res.Histograms = hs;
            return res;
        }

        private static int OccupancyStep(int[] h, long total) {
            if (total <= 0) return 1;
            long acc = 0;
            int lo = -1, hi = -1;
            for (int v = 1; v < h.Length; v++) {
                acc += h[v];
                if (lo < 0 && acc >= total * 0.05) lo = v;
                if (acc >= total * 0.95) { hi = v; break; }
            }
            if (lo < 0 || hi <= lo) return 1;
            for (int sp = 1; sp <= 64; sp *= 2) {
                int first = lo / sp, last = hi / sp, bins = 0, empty = 0;
                for (int b = first; b <= last; b++) {
                    long c = 0;
                    int end = Math.Min(h.Length, (b + 1) * sp);
                    for (int v = b * sp; v < end; v++) c += h[v];
                    bins++;
                    if (c == 0) empty++;
                }
                // moins de 2 % de tranches vides et des tranches assez nombreuses pour dessiner la courbe
                if (bins < 16 || empty <= bins / 50) return sp;
            }
            return 64;
        }

        /// <summary>Moyenne, écart-type, médiane, MAD, min, max et saturation à partir d'un histogramme.</summary>
        public static ChannelStats StatsFromHistogram(int[] h, int saturation) {
            var s = new ChannelStats { Min = -1 };
            double sum = 0, sum2 = 0;
            long n = 0;
            for (int v = 0; v < h.Length; v++) {
                long c = h[v];
                if (c == 0) continue;
                if (s.Min < 0) { s.Min = v; s.MinCount = c; }
                s.Max = v; s.MaxCount = c;
                n += c;
                sum += (double)v * c;
                sum2 += (double)v * v * c;
                if (v >= saturation) s.SaturatedCount += c;
            }
            s.Count = n;
            if (n == 0) { s.Min = 0; return s; }
            s.Mean = sum / n;
            s.StdDev = Math.Sqrt(Math.Max(0, sum2 / n - s.Mean * s.Mean));

            int med = MedianIndex(h, n);
            s.Median = med;

            // MAD : médiane des écarts |v - médiane|
            var dev = new int[h.Length];
            for (int v = 0; v < h.Length; v++) {
                if (h[v] == 0) continue;
                int d = Math.Abs(v - med);
                dev[d] += h[v];
            }
            s.Mad = MedianIndex(dev, n);
            return s;
        }

        private static int MedianIndex(int[] h, long n) {
            long half = (n + 1) / 2, acc = 0;
            for (int v = 0; v < h.Length; v++) {
                acc += h[v];
                if (acc >= half) return v;
            }
            return h.Length - 1;
        }

        /// <summary>
        /// Écart-type d'une différence de deux pixels, à partir de l'histogramme des |différences| :
        /// moyenne quadratique avec rejet itératif au-delà de 4 sigma (étoiles, pixels chauds).
        /// Le calcul par variance, plutôt que par médiane, reste juste quand les valeurs sont quantifiées par pas de 16.
        /// </summary>
        public static double SigmaFromAbsDiff(int[] h) {
            long n = 0;
            double s2 = 0;
            for (int d = 0; d < h.Length; d++) {
                if (h[d] == 0) continue;
                n += h[d];
                s2 += (double)d * d * h[d];
            }
            if (n < 100) return double.NaN;
            double sigma = Math.Sqrt(s2 / n);

            for (int it = 0; it < 6; it++) {
                double clip = 4 * sigma;
                long m = 0;
                double q = 0;
                int lim = (int)Math.Min(h.Length - 1, Math.Floor(clip));
                for (int d = 0; d <= lim; d++) {
                    if (h[d] == 0) continue;
                    m += h[d];
                    q += (double)d * d * h[d];
                }
                if (m < 100) break;
                // correction de la troncature à 4 sigma d'une loi normale (variance × 0,99893)
                double next = Math.Sqrt(q / m / 0.99893);
                if (Math.Abs(next - sigma) < 1e-3 * sigma) { sigma = next; break; }
                sigma = next;
            }
            return sigma;
        }

        /// <summary>Met la matrice de Bayer sous la forme "RGGB" ; null si l'image est monochrome ou si le motif est inconnu.</summary>
        public static string NormalizePattern(string p) {
            if (string.IsNullOrWhiteSpace(p)) return null;
            p = p.Trim().ToUpperInvariant();
            switch (p) {
                case "RGGB": case "BGGR": case "GRBG": case "GBRG": return p;
                case "COLOR": case "COLOUR": return "RGGB";
                default: return null;
            }
        }

        /// <summary>Couleur (Channel.R/G/B) de chaque position du motif 2×2, dans l'ordre (0,0) (1,0) (0,1) (1,1).</summary>
        private static int[] ColorMap(string pattern) {
            if (pattern == null) return null;
            var m = new int[4];
            for (int i = 0; i < 4; i++) {
                m[i] = pattern[i] switch { 'R' => Channel.R, 'G' => Channel.G, _ => Channel.B };
            }
            return m;
        }
    }
}
