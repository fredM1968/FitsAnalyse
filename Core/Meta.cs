using System;
using System.Globalization;
using System.Reflection;

namespace FitsAnalyse.Core {

    /// <summary>
    /// Lecture tolérante des métadonnées NINA par réflexion : si un champ change de nom d'une version de NINA
    /// à l'autre, on obtient simplement « inconnu » au lieu d'une erreur de chargement du plugin.
    /// </summary>
    internal static class Meta {

        /// <summary>Suit un chemin de propriétés ("MetaData.Camera.Gain"). Renvoie null si un maillon manque.</summary>
        public static object Get(object root, string path) {
            object cur = root;
            foreach (var name in path.Split('.')) {
                if (cur == null) return null;
                try {
                    var t = cur.GetType();
                    var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (p == null) {
                        // propriété implémentée explicitement : on la cherche dans les interfaces
                        foreach (var i in t.GetInterfaces()) {
                            p = i.GetProperty(name);
                            if (p != null) break;
                        }
                    }
                    if (p == null) return null;
                    cur = p.GetValue(cur);
                } catch {
                    return null;
                }
            }
            return cur;
        }

        public static double Double(object root, string path) {
            var v = Get(root, path);
            if (v == null) return double.NaN;
            try {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return d;
            } catch {
                return double.NaN;
            }
        }

        public static string String(object root, string path) {
            var v = Get(root, path);
            var s = v?.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        public static DateTime? Date(object root, string path) {
            var v = Get(root, path);
            if (v is DateTime d && d > DateTime.MinValue.AddYears(1)) return d;
            return null;
        }
    }
}
