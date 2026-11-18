using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FitsAnalyse.Core {

    /// <summary>Verdict sur la pose analysée.</summary>
    public enum ExposureVerdict { Unknown, MuchTooShort, Short, Good, Long }

    public sealed class ExposureAdvice {
        public bool Valid;
        public string Message;                 // explication si le calcul est impossible

        public double CurrentExposure;         // s
        public double PixelNoise;              // ADU, bruit total mesuré du fond
        public double ReadNoise;               // ADU
        public double SkyNoise;                // ADU, part du ciel (et du courant d'obscurité)
        public double Ratio;                   // (bruit du ciel / bruit de lecture)² : le « k » de la pose actuelle
        public double CurrentPenalty;          // perte de rapport signal/bruit due au bruit de lecture, en fraction
        public double OptimalExposure;         // s, pour atteindre le k visé
        public double SuggestedStep;           // s, palier de la bibliothèque de darks le plus proche
        public double StepPenalty;             // perte au palier conseillé, en fraction
        public ExposureVerdict Verdict;
    }

    /// <summary>
    /// Temps de pose conseillé, d'après une seule image.
    ///
    /// Le bruit d'un pixel du fond vaut σ² = σ_ciel² + RN² (ADU²). Le ciel (et le courant d'obscurité) suivent une loi de Poisson :
    /// leur variance croît comme la durée de pose. La pose « optimale » est celle où σ_ciel² = k · RN² ;
    /// avec k = 10, le bruit de lecture n'ajoute plus qu'environ 5 % de bruit.
    ///   t_opt = t · k · RN² / (σ² − RN²)
    /// Tout est en ADU : le gain de la caméra n'intervient pas, il suffit de connaître le bruit de lecture dans les mêmes unités
    /// (mesuré sur un bias pris avec les mêmes gain, offset et mode de lecture).
    /// </summary>
    public static class ExposureAdvisor {

        public static ExposureAdvice Compute(double exposure, double pixelNoise, double readNoiseAdu, double targetK, IReadOnlyList<double> darkSteps) {
            var a = new ExposureAdvice { CurrentExposure = exposure, PixelNoise = pixelNoise, ReadNoise = readNoiseAdu };

            if (!(exposure > 0)) { a.Message = "durée de pose inconnue"; return a; }
            if (!(pixelNoise > 0)) { a.Message = "bruit du fond non mesurable"; return a; }
            if (!(readNoiseAdu > 0)) { a.Message = "bruit de lecture non réglé : mesure-le sur un bias (bouton « Mesurer un bias »)"; return a; }
            if (!(targetK > 0)) targetK = 10;

            double sky2 = pixelNoise * pixelNoise - readNoiseAdu * readNoiseAdu;
            double rn2 = readNoiseAdu * readNoiseAdu;
            if (sky2 <= 0.05 * rn2) {
                // le bruit mesuré n'excède pas le bruit de lecture : pose beaucoup trop courte (ou bias inadapté)
                a.Valid = true;
                a.SkyNoise = Math.Sqrt(Math.Max(0, sky2));
                a.Ratio = Math.Max(0, sky2) / rn2;
                a.CurrentPenalty = double.PositiveInfinity;
                a.Verdict = ExposureVerdict.MuchTooShort;
                a.OptimalExposure = double.NaN;
                a.Message = "le fond de ciel est noyé dans le bruit de lecture : allonge nettement la pose (ou vérifie que le bias a les mêmes gain et offset)";
                return a;
            }

            a.Valid = true;
            a.SkyNoise = Math.Sqrt(sky2);
            a.Ratio = sky2 / rn2;
            a.CurrentPenalty = Penalty(a.Ratio);
            a.OptimalExposure = exposure * targetK / a.Ratio;

            a.Verdict = a.Ratio < 3 ? ExposureVerdict.MuchTooShort
                      : a.Ratio < 0.6 * targetK ? ExposureVerdict.Short
                      : a.Ratio <= 3 * targetK ? ExposureVerdict.Good
                      : ExposureVerdict.Long;

            if (darkSteps != null && darkSteps.Count > 0) {
                // palier le plus proche en échelle logarithmique (0,5× et 2× ont des effets comparables)
                a.SuggestedStep = darkSteps.OrderBy(s => Math.Abs(Math.Log(s / a.OptimalExposure))).First();
                a.StepPenalty = Penalty(a.Ratio * a.SuggestedStep / exposure);
            } else {
                a.SuggestedStep = a.OptimalExposure;
                a.StepPenalty = Penalty(targetK);
            }
            return a;
        }

        /// <summary>Perte de rapport signal/bruit due au bruit de lecture, pour un rapport ciel/RN² donné.</summary>
        public static double Penalty(double ratio) => ratio > 0 ? Math.Sqrt(1 + 1 / ratio) - 1 : double.PositiveInfinity;

        /// <summary>Lit une liste de paliers "30; 60; 120…" (secondes).</summary>
        public static List<double> ParseSteps(string text) {
            var list = new List<double>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(new[] { ';', ' ', ',', '/', '|' }, StringSplitOptions.RemoveEmptyEntries)) {
                var t = part.Trim().TrimEnd('s', 'S');
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0) list.Add(v);
            }
            list.Sort();
            return list.Distinct().ToList();
        }
    }
}
