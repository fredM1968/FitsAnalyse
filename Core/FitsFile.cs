using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FitsAnalyse.Core {

    /// <summary>
    /// Lecteur FITS minimal (image principale en 2D) : sert à lire les en-têtes des fichiers ouverts
    /// et les pixels des bias pour mesurer le bruit de lecture.
    /// </summary>
    public sealed class FitsFile {

        public Dictionary<string, string> Header { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int BitPix { get; private set; }

        /// <summary>Pixels ramenés en 16 bits non signés (null si seul l'en-tête a été lu).</summary>
        public ushort[] Pixels { get; private set; }

        public static bool IsFits(string path) {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".fits" || ext == ".fit" || ext == ".fts";
        }

        public static FitsFile Read(string path, bool readPixels) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            var f = new FitsFile();
            f.ReadHeader(fs);
            if (readPixels) f.ReadData(fs);
            return f;
        }

        private void ReadHeader(Stream s) {
            var block = new byte[2880];
            bool end = false;
            int blocks = 0;
            while (!end) {
                if (ReadFully(s, block) < 2880) throw new InvalidDataException("en-tête FITS incomplet");
                if (++blocks > 1000) throw new InvalidDataException("en-tête FITS trop long");
                for (int i = 0; i < 36; i++) {
                    string card = Encoding.ASCII.GetString(block, i * 80, 80);
                    string key = card.Substring(0, 8).Trim();
                    if (key == "END") { end = true; break; }
                    if (card.Length < 10 || card[8] != '=') continue;
                    string val = card.Substring(10);
                    val = StripComment(val).Trim();
                    if (val.StartsWith("'")) val = val.Trim('\'').Trim();
                    if (!Header.ContainsKey(key)) Header[key] = val;
                }
            }
            if (blocks == 1 && !Header.ContainsKey("SIMPLE")) throw new InvalidDataException("ce n'est pas un fichier FITS");
            BitPix = (int)GetDouble("BITPIX", 16);
            Width = (int)GetDouble("NAXIS1", 0);
            Height = (int)GetDouble("NAXIS2", 0);
        }

        private static string StripComment(string v) {
            // la barre « / » commence un commentaire, sauf à l'intérieur d'une chaîne entre apostrophes
            bool inStr = false;
            for (int i = 0; i < v.Length; i++) {
                if (v[i] == '\'') inStr = !inStr;
                else if (v[i] == '/' && !inStr) return v.Substring(0, i);
            }
            return v;
        }

        private void ReadData(Stream s) {
            if (Width <= 0 || Height <= 0) throw new InvalidDataException("image FITS sans dimensions");
            if (GetDouble("NAXIS", 2) > 2 && GetDouble("NAXIS3", 1) > 1) throw new InvalidDataException("image FITS en couleur (3 plans) : un bias brut est attendu");
            int bytes = Math.Abs(BitPix) / 8;
            long n = (long)Width * Height;
            var raw = new byte[n * bytes];
            if (ReadFully(s, raw) < raw.Length) throw new InvalidDataException("données FITS incomplètes");

            double bzero = GetDouble("BZERO", 0), bscale = GetDouble("BSCALE", 1);
            var px = new ushort[n];
            double scaleFloat = 1;
            if (BitPix < 0) {
                // images en virgule flottante : souvent normalisées entre 0 et 1
                double max = 0;
                for (long i = 0; i < n; i++) max = Math.Max(max, ReadSample(raw, i, bzero, bscale));
                scaleFloat = max <= 1.5 ? 65535 : 1;
            }
            for (long i = 0; i < n; i++) {
                double v = ReadSample(raw, i, bzero, bscale) * scaleFloat;
                px[i] = (ushort)Math.Max(0, Math.Min(65535, Math.Round(v)));
            }
            Pixels = px;
        }

        private double ReadSample(byte[] b, long i, double bzero, double bscale) {
            double v;
            switch (BitPix) {
                case 8:
                    v = b[i];
                    break;
                case 16: {
                    long o = i * 2;
                    v = (short)((b[o] << 8) | b[o + 1]);
                    break;
                }
                case 32: {
                    long o = i * 4;
                    v = (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
                    break;
                }
                case -32: {
                    long o = i * 4;
                    var t = new byte[] { b[o + 3], b[o + 2], b[o + 1], b[o] };
                    v = BitConverter.ToSingle(t, 0);
                    break;
                }
                case -64: {
                    long o = i * 8;
                    var t = new byte[8];
                    for (int k = 0; k < 8; k++) t[k] = b[o + 7 - k];
                    v = BitConverter.ToDouble(t, 0);
                    break;
                }
                default:
                    throw new InvalidDataException($"BITPIX {BitPix} non pris en charge");
            }
            return double.IsNaN(v) ? 0 : bzero + bscale * v;
        }

        public double GetDouble(string key, double def) {
            if (Header.TryGetValue(key, out var s) &&
                double.TryParse(s.Replace('D', 'E'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            return def;
        }

        public string GetString(string key) => Header.TryGetValue(key, out var s) && s.Length > 0 ? s : null;

        private static int ReadFully(Stream s, byte[] buf) {
            int total = 0;
            while (total < buf.Length) {
                int r = s.Read(buf, total, buf.Length - total);
                if (r <= 0) break;
                total += r;
            }
            return total;
        }
    }
}
