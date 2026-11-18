using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FitsAnalyse.Core {

    /// <summary>Une mesure de bias : caméra, réglages et résultat.</summary>
    public sealed class BiasEntry {
        public string Camera { get; set; } = "";
        public string Gain { get; set; } = "";
        public string Offset { get; set; } = "";
        public int Binning { get; set; } = 1;
        public double ReadNoiseAdu { get; set; }
        public double BiasLevel { get; set; }
        public DateTime Measured { get; set; }
        public string Method { get; set; } = "";
        public string Files { get; set; } = "";

        /// <summary>Libellé pour l'affichage : « SVBONY SV605CC · gain 101 · offset 7 · 1x1 ».</summary>
        [JsonIgnore]
        public string Label {
            get {
                var parts = new List<string> { string.IsNullOrEmpty(Camera) ? "caméra inconnue" : Camera };
                if (!string.IsNullOrEmpty(Gain)) parts.Add($"gain {Gain}");
                if (!string.IsNullOrEmpty(Offset)) parts.Add($"offset {Offset}");
                parts.Add($"{Binning}x{Binning}");
                return string.Join(" · ", parts);
            }
        }

        [JsonIgnore]
        public string Summary =>
            $"bruit de lecture {ReadNoiseAdu.ToString("0.0", CultureInfo.GetCultureInfo("fr-FR"))} ADU · bias {BiasLevel.ToString("0", CultureInfo.GetCultureInfo("fr-FR"))} ADU · " +
            $"mesuré le {Measured.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"))} ({Method})";
    }

    /// <summary>Degré de correspondance entre une image et une mesure de bias.</summary>
    public enum BiasMatch {
        None,
        /// <summary>Même caméra, même gain, même binning, offset différent : le bruit de lecture reste valable, pas le niveau du bias.</summary>
        OffsetDiffers,
        Exact
    }

    /// <summary>
    /// Mesures de bias mémorisées par caméra, gain, offset et binning, dans un fichier commun à tous les profils NINA :
    /// %LOCALAPPDATA%\NINA\FitsAnalyse\BiasLibrary.json. Chaque image utilise automatiquement la mesure de sa caméra.
    /// </summary>
    public sealed class BiasLibrary {

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "FitsAnalyse", "BiasLibrary.json");

        public List<BiasEntry> Entries { get; private set; } = new List<BiasEntry>();

        public static BiasLibrary Load() {
            var lib = new BiasLibrary();
            try {
                if (File.Exists(FilePath)) {
                    var list = JsonSerializer.Deserialize<List<BiasEntry>>(File.ReadAllText(FilePath));
                    if (list != null) lib.Entries = list;
                }
            } catch {
                // fichier abîmé : on repart d'une bibliothèque vide plutôt que d'empêcher le chargement du plugin
            }
            return lib;
        }

        public void Save() {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var json = JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }

        /// <summary>Ajoute une mesure ; elle remplace celle qui avait exactement les mêmes caméra, gain, offset et binning.</summary>
        public void AddOrReplace(BiasEntry e) {
            Normalize(e);
            Entries.RemoveAll(x => Same(x.Camera, e.Camera) && x.Gain == e.Gain && x.Offset == e.Offset && x.Binning == e.Binning);
            Entries.Add(e);
            Entries = Entries.OrderBy(x => x.Camera, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(x => NumberKey(x.Gain)).ThenBy(x => NumberKey(x.Offset)).ThenBy(x => x.Binning).ToList();
        }

        public void Remove(BiasEntry e) => Entries.Remove(e);

        /// <summary>Mesure à utiliser pour une image : d'abord la correspondance exacte, sinon même caméra, gain et binning.</summary>
        public (BiasEntry entry, BiasMatch match) Find(string camera, string gain, string offset, int binning) {
            camera = Clean(camera);
            gain = NormalizeNumber(gain);
            offset = NormalizeNumber(offset);
            if (binning < 1) binning = 1;
            var same = Entries.Where(x => Same(x.Camera, camera) && x.Gain == gain && x.Binning == binning).ToList();
            var exact = same.FirstOrDefault(x => x.Offset == offset);
            if (exact != null) return (exact, BiasMatch.Exact);
            if (same.Count > 0) return (same.OrderByDescending(x => x.Measured).First(), BiasMatch.OffsetDiffers);
            return (null, BiasMatch.None);
        }

        public static void Normalize(BiasEntry e) {
            e.Camera = Clean(e.Camera);
            e.Gain = NormalizeNumber(e.Gain);
            e.Offset = NormalizeNumber(e.Offset);
            if (e.Binning < 1) e.Binning = 1;
        }

        private static string Clean(string s) => (s ?? "").Trim();

        private static bool Same(string a, string b) => string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>« 101 », « 101.0 » et « 101,0 » donnent « 101 » ; une valeur négative (réglage absent, caméra CCD) donne « ».</summary>
        public static string NormalizeNumber(string s) {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().Replace(',', '.');
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) {
                return v < 0 ? "" : v.ToString("0.##", CultureInfo.InvariantCulture);
            }
            return s;
        }

        private static double NumberKey(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.MaxValue;
    }
}
