namespace FitsAnalyse.Core {

    /// <summary>Indices des canaux : 0 = tous les pixels (mono), 1 = R, 2 = V, 3 = B.</summary>
    public static class Channel {
        public const int L = 0, R = 1, G = 2, B = 3;
        public static readonly string[] Names = { "L", "R", "V", "B" };
    }

    /// <summary>
    /// Histogrammes d'une image, un compteur par valeur ADU (0 à 65535) et par canal.
    /// Step est le pas réel des valeurs (16 pour une caméra 12 bits dont les valeurs sont décalées sur 16 bits) :
    /// l'affichage regroupe les valeurs par ce pas pour éviter un histogramme « en peigne ».
    /// </summary>
    public sealed class HistogramSet {

        public const int Size = 65536;

        /// <summary>Compteurs par canal ; Counts[R], [G], [B] sont null pour une image monochrome.</summary>
        public int[][] Counts { get; } = new int[4][];

        public bool IsColor => Counts[Channel.R] != null;

        /// <summary>Pas entre deux valeurs possibles (1, 2, 4, 8 ou 16).</summary>
        public int Step { get; set; } = 1;

        /// <summary>Pleine échelle des données (4095, 16383 ou 65535).</summary>
        public int FullScale { get; set; } = 65535;

        /// <summary>Copie (pour garder un histogramme de référence).</summary>
        public HistogramSet Clone() {
            var c = new HistogramSet { Step = Step, FullScale = FullScale };
            for (int i = 0; i < 4; i++) c.Counts[i] = (int[])Counts[i]?.Clone();
            return c;
        }
    }
}
