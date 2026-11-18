using FitsAnalyse.Core;
using Microsoft.Win32;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FitsAnalyse.Dockables {

    /// <summary>Une ligne du tableau de statistiques : libellé et valeur par canal.</summary>
    public sealed class StatRow {
        public string Label { get; set; }
        public string L { get; set; }
        public string R { get; set; }
        public string G { get; set; }
        public string B { get; set; }
        public string ToolTip { get; set; }
    }

    /// <summary>Informations sur l'image analysée (métadonnées NINA ou en-tête du fichier).</summary>
    internal sealed class ImageInfo {
        public string Source;          // "Dernière pose" ou nom du fichier
        public bool FromFile;
        public double Exposure = double.NaN;
        public string Filter, Target, Gain, Offset, Bayer, Camera;
        public int Binning = 1;
        public double Temperature = double.NaN;
        public DateTime? Date;
    }

    /// <summary>
    /// Panneau « FitsAnalyse » de l'onglet Imagerie : statistiques de la dernière image (comme le panneau Statistiques),
    /// temps de pose conseillé et histogramme interactif.
    /// </summary>
    [Export(typeof(IDockableVM))]
    public class FitsAnalyseDockableVM : DockableVM {

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly IImagingMediator imaging;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IProfileService profile;

        [ImportingConstructor]
        public FitsAnalyseDockableVM(IProfileService profileService, IImagingMediator imagingMediator, IImageDataFactory imageDataFactory)
            : base(profileService) {
            Title = "FitsAnalyse";
            profile = profileService;
            try { isUnlinked = profileService.ActiveProfile.ImageSettings.UnlinkedStretch; } catch { isUnlinked = false; }
            imaging = imagingMediator;
            this.imageDataFactory = imageDataFactory;

            // Icône : trois barres d'histogramme et une courbe
            var g = Geometry.Parse("M1,15 H15 V16 H1 Z M2,10 H4.5 V14.5 H2 Z M5.5,4 H8 V14.5 H5.5 Z M9,7 H11.5 V14.5 H9 Z M12.5,11 H15 V14.5 H12.5 Z");
            g.Freeze();
            ImageGeometry = new GeometryGroup { Children = { g } };
            ImageGeometry.Freeze();

            OpenFileCommand = new SimpleCommand(async _ => await OpenFileAsync(), _ => !loadingFile);
            AutoFrameCommand = new SimpleCommand(_ => AutoFrame(), _ => current != null);
            ResetImageCommand = new SimpleCommand(_ => ResetImage(), _ => current != null);
            CurveCommittedCommand = new SimpleCommand(_ => OnCurveCommitted());
            CurveChangingCommand = new SimpleCommand(_ => OnCurveCommitted());
            ZoomInCommand = new SimpleCommand(_ => Zoom(1 / 1.5), _ => current != null);
            ZoomOutCommand = new SimpleCommand(_ => Zoom(1.5), _ => current != null);
            ResetZoomCommand = new SimpleCommand(_ => SetView(0, DataLimit), _ => current != null);
            SaveReferenceCommand = new SimpleCommand(_ => SaveReference(), _ => current != null);
            ClearReferenceCommand = new SimpleCommand(_ => { Reference = null; ReferenceLabel = null; }, _ => Reference != null);
            MeasureBiasCommand = new SimpleCommand(async _ => { if (FitsAnalysePlugin.Current != null) await FitsAnalysePlugin.Current.MeasureBiasAsync(); });

            if (imaging != null) imaging.ImagePrepared += OnImagePrepared;
            FitsAnalysePlugin.SettingsChanged += (s, e) => Dispatch(UpdateAdvice);
        }

        #region Réception des images

        private IImageData lastData;               // dernière image analysée (pour ne pas l'analyser deux fois)
        private IImageData pendingFileData;        // fichier ouvert, en attente de son passage dans l'onglet Image
        private ImageInfo pendingFileInfo;
        private int analysisId;

        private void OnImagePrepared(object sender, ImagePreparedEventArgs e) {
            try {
                var data = e?.RenderedImage?.RawImageData;
                if (data == null || ReferenceEquals(data, previewData)) return;   // image étirée par FitsAnalyse lui-même
                ImageInfo info = ReferenceEquals(data, pendingFileData) ? pendingFileInfo : InfoFromNina(data);
                StartAnalysis(data, info);
            } catch (Exception ex) {
                Logger.Error($"FitsAnalyse : image non analysée : {ex.Message}");
            }
        }

        private void StartAnalysis(IImageData data, ImageInfo info) {
            if (ReferenceEquals(data, lastData)) return;
            lastData = data;
            int id = Interlocked.Increment(ref analysisId);

            ushort[] pixels;
            int w, h;
            bool bayered;
            try {
                pixels = data.Data.FlatArray;
                w = data.Properties.Width;
                h = data.Properties.Height;
                bayered = data.Properties.IsBayered;
            } catch (Exception ex) {
                Logger.Error($"FitsAnalyse : pixels illisibles : {ex.Message}");
                return;
            }

            string pattern = null;
            if (bayered || info.Bayer != null) {
                pattern = ImageAnalysis.NormalizePattern(info.Bayer) ??
                          ImageAnalysis.NormalizePattern(Meta.String(data, "MetaData.Camera.SensorType")) ?? "RGGB";
            }

            Dispatch(() => { Busy = true; });
            Task.Run(() => {
                try {
                    var res = ImageAnalysis.Analyze(pixels, w, h, pattern);
                    // la détection d'étoiles de NINA peut se terminer après l'événement : on la lit après l'analyse
                    double hfr = Meta.Double(data, "StarDetectionAnalysis.HFR");
                    double stars = Meta.Double(data, "StarDetectionAnalysis.DetectedStars");
                    Dispatch(() => {
                        if (id != analysisId) return;
                        Show(res, info, hfr, stars, data);
                        Busy = false;
                    });
                } catch (Exception ex) {
                    Logger.Error($"FitsAnalyse : analyse impossible : {ex.Message}");
                    Dispatch(() => { Busy = false; StatusText = $"Analyse impossible : {ex.Message}"; });
                }
            });
        }

        private static ImageInfo InfoFromNina(IImageData d) {
            return new ImageInfo {
                Source = "Dernière pose",
                Exposure = Meta.Double(d, "MetaData.Image.ExposureTime"),
                Filter = Meta.String(d, "MetaData.FilterWheel.Filter"),
                Target = Meta.String(d, "MetaData.Target.Name"),
                Gain = IntText(Meta.Double(d, "MetaData.Camera.Gain")),
                Offset = IntText(Meta.Double(d, "MetaData.Camera.Offset")),
                Temperature = Meta.Double(d, "MetaData.Camera.Temperature"),
                Date = Meta.Date(d, "MetaData.Image.ExposureStart"),
                Camera = Meta.String(d, "MetaData.Camera.Name"),
                Binning = BinOf(Meta.Double(d, "MetaData.Camera.BinX"))
            };
        }

        private static int BinOf(double v) => double.IsNaN(v) || v < 1 ? 1 : (int)v;

        private static string IntText(double v) => double.IsNaN(v) || v < 0 ? null : v.ToString("0", CultureInfo.InvariantCulture);

        #endregion

        #region Ouverture d'un fichier

        private bool loadingFile;

        /// <summary>
        /// Ouvre un fichier déjà acquis et l'affiche dans l'onglet Image de NINA (comme une nouvelle pose).
        /// La prochaine image de la caméra le remplacera, dans l'onglet Image comme ici.
        /// </summary>
        private async Task OpenFileAsync() {
            var dlg = new OpenFileDialog {
                Title = "Ouvrir une image",
                Filter = "Images (*.fits;*.fit;*.fts;*.xisf;*.tif;*.tiff)|*.fits;*.fit;*.fts;*.xisf;*.tif;*.tiff|Tous les fichiers (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            string path = dlg.FileName;

            loadingFile = true;
            CommandManager.InvalidateRequerySuggested();
            StatusText = $"Ouverture de {Path.GetFileName(path)}…";
            try {
                var info = new ImageInfo { Source = Path.GetFileName(path), FromFile = true };
                if (FitsFile.IsFits(path)) {
                    var f = await Task.Run(() => FitsFile.Read(path, false));
                    info.Exposure = f.GetDouble("EXPTIME", f.GetDouble("EXPOSURE", double.NaN));
                    info.Filter = f.GetString("FILTER");
                    info.Target = f.GetString("OBJECT");
                    info.Gain = f.GetString("GAIN");
                    info.Offset = f.GetString("OFFSET");
                    info.Temperature = f.GetDouble("CCD-TEMP", double.NaN);
                    info.Bayer = f.GetString("BAYERPAT");
                    info.Camera = f.GetString("INSTRUME");
                    info.Binning = BinOf(f.GetDouble("XBINNING", 1));
                    if (DateTime.TryParse(f.GetString("DATE-LOC"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dl)) {
                        info.Date = dl;
                    } else if (DateTime.TryParse(f.GetString("DATE-OBS"), CultureInfo.InvariantCulture,
                                                 DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var du)) {
                        info.Date = du.ToLocalTime();
                    }
                }

                var p = profile.ActiveProfile;
                var data = await imageDataFactory.CreateFromFile(path, (int)p.CameraSettings.BitDepth, info.Bayer != null, p.CameraSettings.RawConverter);
                if (data == null) throw new InvalidDataException("fichier illisible");

                // métadonnées lues par NINA, pour les formats autres que FITS
                if (double.IsNaN(info.Exposure)) info.Exposure = Meta.Double(data, "MetaData.Image.ExposureTime");
                info.Filter ??= Meta.String(data, "MetaData.FilterWheel.Filter");
                info.Target ??= Meta.String(data, "MetaData.Target.Name");
                info.Camera ??= Meta.String(data, "MetaData.Camera.Name");

                pendingFileData = data;
                pendingFileInfo = info;
                try {
                    // affichage dans l'onglet Image : l'événement ImagePrepared déclenche l'analyse
                    await imaging.PrepareImage(data, new PrepareImageParameters(), CancellationToken.None);
                } catch (Exception ex) {
                    Logger.Warning($"FitsAnalyse : {Path.GetFileName(path)} non affiché dans l'onglet Image : {ex.Message}");
                }
                StartAnalysis(data, info);   // sans effet si l'événement l'a déjà fait
            } catch (Exception ex) {
                StatusText = $"Ouverture impossible : {ex.Message}";
                Logger.Error($"FitsAnalyse : ouverture de {path} impossible : {ex.Message}");
            } finally {
                loadingFile = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        #endregion

        #region Affichage des résultats

        private AnalysisResult current;
        private ImageInfo currentInfo;
        private double currentHfr = double.NaN, currentStars = double.NaN;

        private void Show(AnalysisResult res, ImageInfo info, double hfr, double stars, IImageData data) {
            bool first = current == null;
            current = res;
            currentData = data;
            previewShown = false;   // NINA affiche maintenant cette nouvelle image, telle quelle
            currentInfo = info;
            var plugin = FitsAnalysePlugin.Current;
            if (plugin != null) {
                // si un bias mesuré ensuite n'a pas de caméra dans son en-tête, il sera rangé sous celle-ci
                plugin.LastImageCamera = info.Camera;
                plugin.LastImageGain = info.Gain;
                plugin.LastImageOffset = info.Offset;
                plugin.LastImageBinning = info.Binning;
            }
            currentHfr = hfr;
            currentStars = stars;

            IsColorImage = res.IsColor;
            if (!res.IsColor) IsRgbMode = false;
            else if (first) IsRgbMode = true;
            Histograms = res.Histograms;

            var parts = new List<string> { info.Source };
            if (info.Date.HasValue) parts.Add(info.Date.Value.ToString("dd/MM HH:mm:ss", Fr));
            if (!double.IsNaN(info.Exposure)) parts.Add($"{info.Exposure.ToString("0.###", Fr)} s");
            if (info.Filter != null) parts.Add(info.Filter);
            SourceText = string.Join(" · ", parts);

            var details = new List<string> { $"{res.Width} × {res.Height}" };
            details.Add(res.IsColor ? $"couleur {res.BayerPattern}" : "mono");
            if (info.Gain != null) details.Add($"gain {info.Gain}");
            if (info.Offset != null) details.Add($"offset {info.Offset}");
            if (!double.IsNaN(info.Temperature)) details.Add($"{info.Temperature.ToString("0.0", Fr)} °C");
            if (res.Histograms.Step > 1) details.Add($"pas {res.Histograms.Step} ADU");
            if (hfr > 0) details.Add($"HFR {hfr.ToString("0.00", Fr)}");
            if (stars >= 0) details.Add($"{stars:0} étoiles");
            if (info.Target != null) details.Add(info.Target);
            DetailText = string.Join(" · ", details);

            if (first) SetView(0, DataLimit);
            UpdateAdvice();
            ApplyImageAdjustments();
            StatusText = "";
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Bruit de lecture et niveau du bias de la caméra de l'image affichée (bibliothèque de bias).</summary>
        private BiasResolution CurrentBias() {
            var plugin = FitsAnalysePlugin.Current;
            if (plugin == null || currentInfo == null) return new BiasResolution();
            return plugin.ResolveBias(currentInfo.Camera, currentInfo.Gain, currentInfo.Offset, currentInfo.Binning);
        }

        private void UpdateStatsRows() {
            if (current == null) { StatRows = Array.Empty<StatRow>(); return; }
            var st = current.Stats;
            var plugin = FitsAnalysePlugin.Current;
            var br = CurrentBias();
            double bias = br.BiasLevel;
            double rn = br.ReadNoise;
            double k = plugin?.TargetK ?? 10;

            StatRow Row(string label, Func<ChannelStats, string> f, string tip = null) => new StatRow {
                Label = label, ToolTip = tip,
                L = st[Channel.L] != null ? f(st[Channel.L]) : "",
                R = st[Channel.R] != null ? f(st[Channel.R]) : "",
                G = st[Channel.G] != null ? f(st[Channel.G]) : "",
                B = st[Channel.B] != null ? f(st[Channel.B]) : ""
            };

            var rows = new List<StatRow> {
                Row("Moyenne", s => N(s.Mean, 1)),
                Row("Médiane", s => N(s.Median, 0)),
                Row("Écart-type", s => N(s.StdDev, 1)),
                Row("MAD", s => N(s.Mad, 1), "Écart absolu médian (σ robuste ≈ 1,4826 × MAD)"),
                Row("Min", s => $"{N(s.Min, 0)} ({N(s.MinCount, 0)})", "Valeur minimale (nombre de pixels)"),
                Row("Max", s => $"{N(s.Max, 0)} ({N(s.MaxCount, 0)})", "Valeur maximale (nombre de pixels)"),
                Row("Saturés", s => (s.SaturatedFraction * 100).ToString("0.###", Fr) + " %", "Part des pixels à la pleine échelle"),
                Row("Bruit pixel", s => N(s.PixelNoise, 1), "Bruit d'un pixel du fond de ciel (ADU), mesuré par différences de pixels voisins de même couleur")
            };
            if (bias > 0) rows.Add(Row("Fond − bias", s => N(s.Median - bias, 0), "Médiane moins le niveau du bias mesuré : signal du fond de ciel en ADU"));
            if (rn > 0 && currentInfo != null && currentInfo.Exposure > 0) {
                rows.Add(Row("Pose optimale", s => {
                    var a = ExposureAdvisor.Compute(currentInfo.Exposure, s.PixelNoise, rn, k, null);
                    return a.Valid && a.OptimalExposure > 0 ? $"{N(a.OptimalExposure, 0)} s" : "—";
                }, $"Durée pour laquelle le bruit du ciel atteint {k:0} × le bruit de lecture (en variance), canal par canal"));
            }
            StatRows = rows;
        }

        private static string N(double v, int dec) =>
            double.IsNaN(v) || double.IsInfinity(v) ? "—" : v.ToString(dec == 0 ? "#,0" : "#,0." + new string('0', dec), Fr);

        private void UpdateAdvice() {
            UpdateStatsRows();
            var plugin = FitsAnalysePlugin.Current;
            if (current == null) {
                AdviceTitle = "Temps de pose conseillé";
                AdviceText = "En attente d'une image.";
                AdviceDetail = "";
                VerdictBrush = Brushes.Gray;
                NeedsReadNoise = false;
                return;
            }
            var br = CurrentBias();
            double rn = br.ReadNoise;
            double k = plugin?.TargetK ?? 10;
            NeedsReadNoise = rn <= 0;
            var noise = current.Stats[Channel.L].PixelNoise;
            var a = ExposureAdvisor.Compute(currentInfo?.Exposure ?? double.NaN, noise, rn, k, plugin?.DarkStepList);

            if (!a.Valid) {
                AdviceTitle = "Temps de pose conseillé : —";
                AdviceText = rn <= 0
                    ? $"Calcul impossible : {br.Description}. Mesure un ou deux bias de cette caméra avec ces réglages."
                    : $"Calcul impossible : {a.Message}.";
                AdviceDetail = $"Bruit du fond mesuré : {N(noise, 1)} ADU.";
                VerdictBrush = Brushes.Gray;
                return;
            }

            string Pct(double f) => double.IsInfinity(f) ? "très forte" : $"+{(f * 100).ToString("0.0", Fr)} %";

            if (double.IsNaN(a.OptimalExposure)) {
                AdviceTitle = "Temps de pose conseillé : nettement plus long";
                AdviceText = char.ToUpper(a.Message[0]) + a.Message.Substring(1) + ".";
            } else {
                bool stepDiffers = Math.Abs(a.SuggestedStep - a.OptimalExposure) > 0.5;
                AdviceTitle = $"Temps de pose conseillé : {N(a.SuggestedStep, 0)} s" +
                              (stepDiffers ? $"  (optimum {N(a.OptimalExposure, 0)} s)" : "");
                string verdict = a.Verdict switch {
                    ExposureVerdict.MuchTooShort => "trop courte",
                    ExposureVerdict.Short => "un peu courte",
                    ExposureVerdict.Good => "bonne",
                    ExposureVerdict.Long => "plus longue que nécessaire",
                    _ => ""
                };
                AdviceText = $"Pose actuelle {N(a.CurrentExposure, 0)} s : {verdict}. Ciel / lecture = {N(a.Ratio, 1)} " +
                             $"(visé {N(k, 0)}), bruit ajouté par la lecture {Pct(a.CurrentPenalty)}.";
                if (stepDiffers) AdviceText += $" Au palier de {N(a.SuggestedStep, 0)} s : {Pct(a.StepPenalty)}.";
            }
            AdviceDetail = $"Bruit du fond {N(a.PixelNoise, 1)} ADU = ciel {N(a.SkyNoise, 1)} ⊕ lecture {N(a.ReadNoise, 1)} ADU. " +
                           "Valable pour ce ciel et ce filtre : à refaire si la Lune, la hauteur de la cible ou le filtre changent. " +
                           char.ToUpper(br.Description[0]) + br.Description.Substring(1) + ".";
            VerdictBrush = a.Verdict switch {
                ExposureVerdict.Good => new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0x5C)),
                ExposureVerdict.Short => new SolidColorBrush(Color.FromRgb(0xE8, 0xB0, 0x40)),
                ExposureVerdict.Long => new SolidColorBrush(Color.FromRgb(0x6C, 0xA8, 0xF0)),
                ExposureVerdict.MuchTooShort => new SolidColorBrush(Color.FromRgb(0xF0, 0x60, 0x50)),
                _ => Brushes.Gray
            };
        }

        #endregion

        #region Histogramme : vue, cadrage automatique, référence

        private double DataLimit => current != null ? current.Histograms.FullScale + Math.Max(1, current.Histograms.Step) : 65536;

        private void SetView(double min, double max) {
            double limit = DataLimit;
            double minSpan = current != null ? Math.Max(16, 8 * current.Histograms.Step) : 16;
            double w = Math.Max(minSpan, max - min);
            if (w >= limit) { min = 0; max = limit; } else {
                if (min < 0) min = 0;
                max = min + w;
                if (max > limit) { max = limit; min = limit - w; }
            }
            ViewMin = min;
            ViewMax = max;
        }

        private void Zoom(double factor) {
            double c = (ViewMin + ViewMax) / 2, w = (ViewMax - ViewMin) * factor;
            SetView(c - w / 2, c + w / 2);
        }

        /// <summary>
        /// Cadre l'histogramme sur toute la dynamique utile des canaux affichés : du 0,01e au 99,9e centile
        /// (on écarte seulement les quelques pixels extrêmes : pixels morts, cœurs d'étoiles, saturation).
        /// </summary>
        private void AutoFrame() {
            if (current == null) return;
            var chans = new List<int>();
            if (ShowColorCurves) {
                if (ShowR) chans.Add(Channel.R);
                if (ShowG) chans.Add(Channel.G);
                if (ShowB) chans.Add(Channel.B);
            }
            if (chans.Count == 0) chans.Add(Channel.L);
            var hs = displayHistograms ?? current.Histograms;
            int saturation = Math.Max(1, hs.FullScale - 2 * hs.Step + 1);
            double lo = double.MaxValue, hi = 0;
            foreach (var c in chans) {
                var s = DisplayStats[c];
                var h = hs.Counts[c];
                if (s == null || h == null) continue;
                double sigma = Math.Max(Math.Max(s.RobustSigma, double.IsNaN(s.PixelNoise) ? 0 : s.PixelNoise), hs.Step);
                lo = Math.Min(lo, Percentile(h, 0.0001, saturation));
                // à droite, jamais plus étroit que le fond de ciel seul
                hi = Math.Max(hi, Math.Max(Percentile(h, 0.999, saturation), s.Median + 12 * sigma));
            }
            if (!(hi > lo)) return;
            double margin = 0.03 * (hi - lo);
            SetView(Math.Max(0, lo - margin), hi + margin);
        }

        /// <summary>Valeur sous laquelle se trouve la fraction q des pixels non saturés et non nuls.</summary>
        private static double Percentile(int[] h, double q, int saturation) {
            long n = 0;
            for (int v = 1; v < saturation && v < h.Length; v++) n += h[v];
            if (n == 0) return 0;
            long target = (long)Math.Ceiling(q * n), acc = 0;
            for (int v = 1; v < saturation && v < h.Length; v++) {
                acc += h[v];
                if (acc >= target) return v;
            }
            return saturation;
        }

        private void SaveReference() {
            if (current == null) return;
            Reference = (displayHistograms ?? current.Histograms).Clone();
            ReferenceLabel = currentInfo?.Source == "Dernière pose" && currentInfo.Date.HasValue
                ? $"pose de {currentInfo.Date.Value.ToString("HH:mm:ss", Fr)}"
                : currentInfo?.Source ?? "référence";
            CommandManager.InvalidateRequerySuggested();
        }

        #endregion

        #region Étirement de l'image affichée dans l'onglet Image

        private IImageData currentData;      // pixels bruts de l'image courante
        private IImageData previewData;      // dernière image étirée envoyée à NINA (ignorée par OnImagePrepared)
        private bool previewShown;           // l'onglet Image montre-t-il notre version étirée ?
        private bool manualCurve;            // courbe réglée à la main avec les curseurs
        private WhiteBalance whiteBalance;   // null = pas de balance des blancs
        private HistogramSet displayHistograms;
        private ChannelStats[] displayStats;

        /// <summary>Statistiques des histogrammes affichés (après balance des blancs éventuelle).</summary>
        private ChannelStats[] DisplayStats => displayStats ?? current?.Stats;

        private double FullScale => current?.Histograms.FullScale ?? 65535;

        /// <summary>
        /// Recalcule la balance des blancs, les histogrammes affichés et la courbe automatique,
        /// puis met à jour l'image de l'onglet Image. Appelé à chaque nouvelle image et à chaque bouton.
        /// </summary>
        private void ApplyImageAdjustments() {
            if (current == null) return;
            whiteBalance = IsAutoWB && current.IsColor
                ? ImageStretch.AutoWhiteBalance(current, CurrentBias().BiasLevel)
                : null;

            if (whiteBalance != null) {
                displayHistograms = ImageStretch.ApplyToHistograms(current.Histograms, whiteBalance);
                int sat = Math.Max(1, displayHistograms.FullScale - 2 * displayHistograms.Step + 1);
                displayStats = new ChannelStats[4];
                for (int c = 0; c < 4; c++) {
                    if (displayHistograms.Counts[c] != null) displayStats[c] = ImageAnalysis.StatsFromHistogram(displayHistograms.Counts[c], sat);
                }
            } else {
                displayHistograms = current.Histograms;
                displayStats = null;
            }
            Histograms = displayHistograms;

            if (IsAutoStretch) {
                var auto = ImageStretch.Auto(DisplayStats[Channel.L], FullScale, TargetBackground(), BlackClipping());
                if (IsUnlinkedActive) {
                    // étirement séparé : chaque canal a son propre noir et ses propres tons moyens
                    curves[Channel.L] = auto;
                    foreach (var c in new[] { Channel.R, Channel.G, Channel.B }) {
                        curves[c] = ImageStretch.Auto(DisplayStats[c], FullScale, TargetBackground(), BlackClipping());
                    }
                    SyncHandles();
                } else {
                    SetCurve(auto);
                }
            } else if (!manualCurve) {
                SetCurve(CurveParams.Identity(FullScale));
            }
            _ = RenderAsync();
        }

        private double TargetBackground() {
            try { return profile.ActiveProfile.ImageSettings.AutoStretchFactor; } catch { return 0.2; }
        }

        private double BlackClipping() {
            try { return profile.ActiveProfile.ImageSettings.BlackClipping; } catch { return -2.8; }
        }

        /// <summary>Courbes par canal (index Channel). En étirement lié, les quatre sont identiques.</summary>
        private readonly CurveParams[] curves = {
            CurveParams.Identity(65535), CurveParams.Identity(65535), CurveParams.Identity(65535), CurveParams.Identity(65535)
        };

        private bool syncingHandles;

        /// <summary>Même courbe pour tous les canaux.</summary>
        private void SetCurve(CurveParams c) {
            for (int i = 0; i < 4; i++) curves[i] = c;
            SyncHandles();
        }

        /// <summary>Étirement séparé actif : demandé, et image couleur.</summary>
        private bool IsUnlinkedActive => IsUnlinked && IsColorImage;

        /// <summary>
        /// Canaux modifiés par les curseurs : tous en étirement lié ; en étirement séparé, les canaux affichés
        /// en mode RVB (comme Siril : on coche les canaux à régler), ou R, V et B ensemble en mode Mono.
        /// </summary>
        private int[] EditedChannels() {
            if (!IsUnlinkedActive) return new[] { Channel.L, Channel.R, Channel.G, Channel.B };
            var list = new List<int>();
            if (ShowColorCurves) {
                if (ShowR) list.Add(Channel.R);
                if (ShowG) list.Add(Channel.G);
                if (ShowB) list.Add(Channel.B);
            }
            if (list.Count == 0) list.AddRange(new[] { Channel.R, Channel.G, Channel.B });
            return list.ToArray();
        }

        /// <summary>Place les curseurs sur la courbe du premier canal modifié.</summary>
        private void SyncHandles() {
            var c = curves[EditedChannels()[0]];
            syncingHandles = true;
            try {
                Shadows = c.Shadows;
                Midtone = c.Midtone;
                Highlights = c.Highlights;
            } finally {
                syncingHandles = false;
            }
            RaiseCurvesChanged();
        }

        /// <summary>Un curseur a bougé : sa courbe s'applique aux canaux modifiés.</summary>
        private void PushHandles() {
            if (syncingHandles) return;
            var c = new CurveParams { Shadows = shadows, Midtone = midtone, Highlights = highlights };
            foreach (var ch in EditedChannels()) curves[ch] = c;
            if (IsUnlinkedActive) curves[Channel.L] = c;
            RaiseCurvesChanged();
        }

        private void RaiseCurvesChanged() {
            RaisePropertyChanged(nameof(ChannelCurves));
            RaisePropertyChanged(nameof(CurveText));
        }

        /// <summary>Courbes R, V, B à dessiner en couleur (étirement séparé), null en étirement lié.</summary>
        public CurveParams[] ChannelCurves => IsUnlinkedActive ? (CurveParams[])curves.Clone() : null;

        private bool isUnlinked;

        /// <summary>Étirement séparé par canal (images couleur). Valeur initiale : réglage « étirement non lié » du profil NINA.</summary>
        public bool IsUnlinked {
            get => isUnlinked;
            set {
                if (isUnlinked == value) return;
                isUnlinked = value;
                RaisePropertyChanged();
                if (IsAutoStretch) {
                    ApplyImageAdjustments();
                } else {
                    // passage en lié : tous les canaux reprennent la courbe des curseurs
                    if (!value) SetCurve(new CurveParams { Shadows = shadows, Midtone = midtone, Highlights = highlights });
                    else SyncHandles();
                    _ = RenderAsync();
                }
            }
        }

        /// <summary>Un curseur a été relâché : la courbe devient manuelle et s'applique à l'image.</summary>
        private void OnCurveCommitted() {
            if (current == null) return;
            manualCurve = true;
            if (IsAutoStretch) {
                isAutoStretch = false;
                RaisePropertyChanged(nameof(IsAutoStretch));
            }
            _ = RenderAsync();
        }

        /// <summary>Revient à l'image d'origine : sans étirement ni balance des blancs.</summary>
        private void ResetImage() {
            isAutoStretch = false;
            isAutoWB = false;
            manualCurve = false;
            RaisePropertyChanged(nameof(IsAutoStretch));
            RaisePropertyChanged(nameof(IsAutoWB));
            ApplyImageAdjustments();
        }

        private bool rendering, renderPending;

        /// <summary>
        /// Envoie à l'onglet Image la version étirée de l'image brute (ou l'image d'origine si rien n'est appliqué).
        /// Les rendus demandés pendant un rendu en cours sont regroupés en un seul, avec les derniers réglages.
        /// </summary>
        private async Task RenderAsync() {
            if (current == null || currentData == null || imaging == null) return;
            if (rendering) { renderPending = true; return; }
            rendering = true;
            try {
                do {
                    renderPending = false;
                    var src = currentData;
                    var curveSet = (CurveParams[])curves.Clone();
                    var wb = whiteBalance;
                    bool needed = wb != null;
                    foreach (var c in curveSet) needed |= !c.IsIdentity(FullScale);

                    if (!needed) {
                        if (previewShown) {
                            previewShown = false;
                            previewData = null;
                            await imaging.PrepareImage(src, new PrepareImageParameters(), CancellationToken.None);
                        }
                        continue;
                    }

                    StatusText = "Étirement de l'image…";
                    ushort[] pixels = src.Data.FlatArray;
                    int w = src.Properties.Width, h = src.Properties.Height;
                    bool bayered = src.Properties.IsBayered;
                    string pattern = current.BayerPattern;
                    var stretched = await Task.Run(() => ImageStretch.Render(pixels, w, h, bayered ? pattern : null, wb, curveSet));
                    if (!ReferenceEquals(src, currentData)) continue;   // une nouvelle image est arrivée entre-temps

                    var preview = imageDataFactory.CreateBaseImageData(stretched, w, h, 16, bayered, src.MetaData);
                    previewData = preview;
                    previewShown = true;
                    await imaging.PrepareImage(preview, new PrepareImageParameters(autoStretch: false, detectStars: false), CancellationToken.None);
                    StatusText = "";
                } while (renderPending);
            } catch (Exception ex) {
                StatusText = $"Étirement impossible : {ex.Message}";
                Logger.Error($"FitsAnalyse : étirement impossible : {ex.Message}");
            } finally {
                rendering = false;
            }
        }

        private bool isAutoStretch;
        /// <summary>Étirement automatique de chaque image, comme le bouton Autostretch de l'onglet Image.</summary>
        public bool IsAutoStretch {
            get => isAutoStretch;
            set {
                if (isAutoStretch == value) return;
                isAutoStretch = value;
                manualCurve = false;
                RaisePropertyChanged();
                ApplyImageAdjustments();
            }
        }

        private bool isAutoWB;
        /// <summary>Balance des blancs automatique (images couleur) : les canaux R et B sont alignés sur le vert.</summary>
        public bool IsAutoWB {
            get => isAutoWB;
            set {
                if (isAutoWB == value) return;
                isAutoWB = value;
                RaisePropertyChanged();
                ApplyImageAdjustments();
            }
        }

        private double shadows, midtone = 0.5, highlights = 65535;

        public double Shadows {
            get => shadows;
            set { if (shadows == value) return; shadows = value; RaisePropertyChanged(); PushHandles(); }
        }

        public double Midtone {
            get => midtone;
            set { if (midtone == value) return; midtone = value; RaisePropertyChanged(); PushHandles(); }
        }

        public double Highlights {
            get => highlights;
            set { if (highlights == value) return; highlights = value; RaisePropertyChanged(); PushHandles(); }
        }

        private static string CurveSummary(CurveParams c) =>
            $"bas {N(c.Shadows, 0)} · moyen {c.Midtone.ToString("0.0000", Fr)} · haut {N(c.Highlights, 0)}";

        public string CurveText => current == null ? "" :
            (IsUnlinkedActive
                ? $"R : {CurveSummary(curves[Channel.R])}   V : {CurveSummary(curves[Channel.G])}   B : {CurveSummary(curves[Channel.B])} (ADU)"
                : $"Bas {N(Shadows, 0)} ADU · tons moyens {Midtone.ToString("0.0000", Fr)} · haut {N(Highlights, 0)} ADU") +
            (whiteBalance != null ? $" · balance R ×{whiteBalance.Gain[Channel.R].ToString("0.000", Fr)}, B ×{whiteBalance.Gain[Channel.B].ToString("0.000", Fr)}" : "");

        #endregion

        #region Propriétés liées à la vue

        private HistogramSet histograms;
        public HistogramSet Histograms { get => histograms; private set { histograms = value; RaisePropertyChanged(); } }

        private HistogramSet reference;
        public HistogramSet Reference { get => reference; private set { reference = value; RaisePropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }

        private string referenceLabel;
        public string ReferenceLabel { get => referenceLabel; private set { referenceLabel = value; RaisePropertyChanged(); } }

        private double viewMin;
        public double ViewMin { get => viewMin; set { if (viewMin == value) return; viewMin = value; RaisePropertyChanged(); } }

        private double viewMax = 65536;
        public double ViewMax { get => viewMax; set { if (viewMax == value) return; viewMax = value; RaisePropertyChanged(); } }

        private bool isColorImage;
        public bool IsColorImage { get => isColorImage; private set { isColorImage = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ShowColorCurves)); RaisePropertyChanged(nameof(ChannelCurves)); } }

        /// <summary>Histogramme et statistiques par canal R, V, B (sinon un seul canal, tous pixels confondus).</summary>
        private bool isRgbMode;
        public bool IsRgbMode {
            get => isRgbMode;
            set {
                isRgbMode = value && isColorImage;
                RaisePropertyChanged(); RaisePropertyChanged(nameof(IsMonoMode)); RaisePropertyChanged(nameof(ShowColorCurves));
                if (IsUnlinkedActive) SyncHandles();
            }
        }

        public bool IsMonoMode { get => !IsRgbMode; set => IsRgbMode = !value; }

        public bool ShowColorCurves => IsRgbMode && IsColorImage;

        private bool logScale;
        public bool LogScale { get => logScale; set { logScale = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(LinearScale)); } }

        public bool LinearScale { get => !LogScale; set => LogScale = !value; }

        private bool showOutput = true;
        /// <summary>Volet « après étirement » au-dessus de l'histogramme.</summary>
        public bool ShowOutput { get => showOutput; set { showOutput = value; RaisePropertyChanged(); } }

        private bool smooth = true;
        /// <summary>Lissage des queues de l'histogramme (affichage seulement).</summary>
        public bool Smooth { get => smooth; set { smooth = value; RaisePropertyChanged(); } }

        private bool showR = true, showG = true, showB = true;
        public bool ShowR { get => showR; set => SetChannel(ref showR, value, nameof(ShowR)); }
        public bool ShowG { get => showG; set => SetChannel(ref showG, value, nameof(ShowG)); }
        public bool ShowB { get => showB; set => SetChannel(ref showB, value, nameof(ShowB)); }

        /// <summary>
        /// En mode Mono, un clic sur R, V ou B passe en mode RVB et affiche ce canal
        /// (au lieu d'un bouton grisé et illisible).
        /// </summary>
        private void SetChannel(ref bool field, bool value, string name) {
            if (!ShowColorCurves && IsColorImage) {
                field = true;
                IsRgbMode = true;
            } else {
                field = value;
            }
            RaisePropertyChanged(name);
            if (IsUnlinkedActive) SyncHandles();
        }

        private IReadOnlyList<StatRow> statRows = Array.Empty<StatRow>();
        public IReadOnlyList<StatRow> StatRows { get => statRows; private set { statRows = value; RaisePropertyChanged(); } }

        private string sourceText = "Aucune image";
        public string SourceText { get => sourceText; private set { sourceText = value; RaisePropertyChanged(); } }

        private string detailText = "";
        public string DetailText { get => detailText; private set { detailText = value; RaisePropertyChanged(); } }

        private string statusText = "";
        public string StatusText { get => statusText; private set { statusText = value; RaisePropertyChanged(); } }

        private string adviceTitle = "Temps de pose conseillé";
        public string AdviceTitle { get => adviceTitle; private set { adviceTitle = value; RaisePropertyChanged(); } }

        private string adviceText = "En attente d'une image.";
        public string AdviceText { get => adviceText; private set { adviceText = value; RaisePropertyChanged(); } }

        private string adviceDetail = "";
        public string AdviceDetail { get => adviceDetail; private set { adviceDetail = value; RaisePropertyChanged(); } }

        private Brush verdictBrush = Brushes.Gray;
        public Brush VerdictBrush { get => verdictBrush; private set { verdictBrush = value; RaisePropertyChanged(); } }

        private bool needsReadNoise = true;
        public bool NeedsReadNoise { get => needsReadNoise; private set { needsReadNoise = value; RaisePropertyChanged(); } }

        private bool busy;
        public bool Busy { get => busy; private set { busy = value; RaisePropertyChanged(); } }

        public ICommand OpenFileCommand { get; }
        public ICommand AutoFrameCommand { get; }
        public ICommand ResetImageCommand { get; }
        public ICommand CurveCommittedCommand { get; }
        public ICommand CurveChangingCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand SaveReferenceCommand { get; }
        public ICommand ClearReferenceCommand { get; }
        public ICommand MeasureBiasCommand { get; }

        #endregion

        private static void Dispatch(Action a) {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) a();
            else d.BeginInvoke(a);
        }
    }

    /// <summary>Commande minimale, pour ne pas dépendre de la bibliothèque MVVM interne de NINA.</summary>
    public class SimpleCommand : ICommand {
        private readonly Action<object> execute;
        private readonly Func<object, bool> canExecute;

        public SimpleCommand(Action<object> execute, Func<object, bool> canExecute = null) {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => canExecute?.Invoke(parameter) ?? true;

        public void Execute(object parameter) => execute(parameter);
    }
}
