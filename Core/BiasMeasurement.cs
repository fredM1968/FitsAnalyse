using System;
using System.Collections.Generic;
using System.IO;

namespace FitsAnalyse.Core {

    public sealed class BiasResult {
        public double Level;        // ADU, médiane du bias
        public double ReadNoise;    // ADU
        public int Files;
        public string Gain, Offset; // valeurs de l'en-tête
        public string Camera;       // INSTRUME de l'en-tête (nom de la caméra écrit par NINA)
        public int Binning = 1;
        public string Method;
    }

    /// <summary>
    /// Mesure du niveau et du bruit de lecture sur un ou deux bias (ou darks très courts).
    /// Avec deux fichiers, le bruit vient de leur différence pixel à pixel (le motif fixe s'annule) ;
    /// avec un seul, des différences entre pixels voisins de même couleur.
    /// </summary>
    public static class BiasMeasurement {

        public static BiasResult Measure(IReadOnlyList<string> paths) {
            if (paths == null || paths.Count == 0) throw new ArgumentException("aucun fichier");
            foreach (var p in paths) {
                if (!FitsFile.IsFits(p)) throw new InvalidDataException($"{Path.GetFileName(p)} : seul le format FITS est accepté pour les bias");
            }

            var a = FitsFile.Read(paths[0], true);
            var res = new BiasResult {
                Files = Math.Min(paths.Count, 2),
                Gain = a.GetString("GAIN"),
                Offset = a.GetString("OFFSET"),
                Camera = a.GetString("INSTRUME"),
                Binning = (int)Math.Max(1, a.GetDouble("XBINNING", 1))
            };
            string bayer = a.GetString("BAYERPAT");
            var an = ImageAnalysis.Analyze(a.Pixels, a.Width, a.Height, bayer);
            res.Level = an.Stats[Channel.L].Median;

            if (paths.Count >= 2) {
                var b = FitsFile.Read(paths[1], true);
                if (b.Width != a.Width || b.Height != a.Height) throw new InvalidDataException("les deux bias n'ont pas la même taille");
                var h = new int[HistogramSet.Size];
                var pa = a.Pixels;
                var pb = b.Pixels;
                // on retire la différence de niveau moyen éventuelle entre les deux poses
                var bb = ImageAnalysis.Analyze(pb, b.Width, b.Height, bayer);
                int shift = (int)Math.Round(an.Stats[Channel.L].Median - bb.Stats[Channel.L].Median);
                for (long i = 0; i < pa.Length; i++) {
                    int d = Math.Abs(pa[i] - pb[i] - shift);
                    if (d < h.Length) h[d]++;
                }
                res.ReadNoise = ImageAnalysis.SigmaFromAbsDiff(h) / Math.Sqrt(2);
                res.Method = "différence de deux bias";
            } else {
                res.ReadNoise = an.Stats[Channel.L].PixelNoise;
                res.Method = "un seul bias (pixels voisins)";
            }
            if (!(res.ReadNoise > 0)) throw new InvalidDataException("bruit de lecture non mesurable sur ce fichier");
            return res;
        }
    }
}
