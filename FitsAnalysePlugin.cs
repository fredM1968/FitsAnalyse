using FitsAnalyse.Core;
using FitsAnalyse.Dockables;
using Microsoft.Win32;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FitsAnalyse {

    /// <summary>
    /// Point d'entrée du plugin : réglages du calcul du temps de pose (enregistrés dans le profil NINA)
    /// et mesure du bruit de lecture sur un bias. Le panneau lui-même est FitsAnalyseDockableVM.
    /// </summary>
    /// <summary>Bruit de lecture et niveau du bias retenus pour une image, avec leur origine.</summary>
    internal sealed class BiasResolution {
        public double ReadNoise;     // ADU, 0 = inconnu
        public double BiasLevel;     // ADU, 0 = inconnu
        public string Description = "";
    }

    [Export(typeof(IPluginManifest))]
    public class FitsAnalysePlugin : PluginBase, INotifyPropertyChanged {

        private readonly IProfileService profileService;
        private readonly PluginOptionsAccessor options;

        internal static FitsAnalysePlugin Current { get; private set; }

        /// <summary>Un réglage a changé : le panneau recalcule la suggestion.</summary>
        internal static event EventHandler SettingsChanged;

        [ImportingConstructor]
        public FitsAnalysePlugin(IProfileService profileService) {
            this.profileService = profileService;
            options = new PluginOptionsAccessor(profileService, Guid.Parse(Identifier));
            MeasureBiasCommand = new SimpleCommand(async _ => await MeasureBiasAsync(), _ => !measuring);
            ClearBiasCommand = new SimpleCommand(_ => { ReadNoiseAdu = 0; BiasLevel = 0; BiasInfo = ""; RaisePropertyChanged(nameof(HasLegacyBias)); });
            DeleteBiasEntryCommand = new SimpleCommand(p => DeleteEntry(p as BiasEntry));
            Library = BiasLibrary.Load();
            Current = this;
            profileService.ProfileChanged += OnProfileChanged;
        }

        public override Task Teardown() {
            profileService.ProfileChanged -= OnProfileChanged;
            return Task.CompletedTask;
        }

        private void OnProfileChanged(object sender, EventArgs e) {
            foreach (var n in new[] { nameof(ReadNoiseAdu), nameof(BiasLevel), nameof(BiasInfo), nameof(ReadNoiseElectrons),
                                      nameof(GainElectronsPerAdu), nameof(TargetK), nameof(DarkSteps), nameof(ReadNoiseSummary) }) {
                RaisePropertyChanged(n);
            }
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        #region Réglages (profil NINA)

        /// <summary>Bruit de lecture mesuré sur un bias, en ADU du fichier (0 = non mesuré).</summary>
        public double ReadNoiseAdu {
            get => options.GetValueDouble(nameof(ReadNoiseAdu), 0);
            set => SetDouble(nameof(ReadNoiseAdu), Math.Max(0, value));
        }

        /// <summary>Niveau du bias (offset) en ADU (0 = non mesuré). Sert à afficher le fond de ciel au-dessus du bias.</summary>
        public double BiasLevel {
            get => options.GetValueDouble(nameof(BiasLevel), 0);
            set => SetDouble(nameof(BiasLevel), Math.Max(0, value));
        }

        /// <summary>Origine de la mesure (date, fichiers, gain et offset du bias).</summary>
        public string BiasInfo {
            get => options.GetValueString(nameof(BiasInfo), "");
            set {
                options.SetValueString(nameof(BiasInfo), value ?? "");
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ReadNoiseSummary));
            }
        }

        /// <summary>Saisie manuelle, utilisée seulement sans mesure : bruit de lecture en électrons (courbes du fabricant).</summary>
        public double ReadNoiseElectrons {
            get => options.GetValueDouble(nameof(ReadNoiseElectrons), 0);
            set => SetDouble(nameof(ReadNoiseElectrons), Math.Max(0, value));
        }

        /// <summary>Saisie manuelle : gain en électrons par ADU du fichier (gain natif ÷ 16 pour un capteur 12 bits).</summary>
        public double GainElectronsPerAdu {
            get => options.GetValueDouble(nameof(GainElectronsPerAdu), 0);
            set => SetDouble(nameof(GainElectronsPerAdu), Math.Max(0, value));
        }

        /// <summary>Rapport visé entre le bruit du ciel et le bruit de lecture (variance) : 10 ≈ 5 % de bruit en plus.</summary>
        public double TargetK {
            get => options.GetValueDouble(nameof(TargetK), 10);
            set => SetDouble(nameof(TargetK), Math.Max(1, Math.Min(100, value)));
        }

        /// <summary>Durées disponibles dans la bibliothèque de darks, en secondes.</summary>
        public string DarkSteps {
            get => options.GetValueString(nameof(DarkSteps), "30; 60; 120; 180; 240; 300; 600");
            set {
                var steps = ExposureAdvisor.ParseSteps(value);
                options.SetValueString(nameof(DarkSteps), string.Join("; ", steps.Select(s => s.ToString(CultureInfo.InvariantCulture))));
                RaisePropertyChanged();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal List<double> DarkStepList => ExposureAdvisor.ParseSteps(DarkSteps);

        /// <summary>Bruit de lecture utilisé pour le calcul, en ADU : la mesure sur bias, sinon la saisie manuelle.</summary>
        internal double EffectiveReadNoiseAdu {
            get {
                if (ReadNoiseAdu > 0) return ReadNoiseAdu;
                double rn = ReadNoiseElectrons, g = GainElectronsPerAdu;
                return rn > 0 && g > 0 ? rn / g : 0;
            }
        }

        public string ReadNoiseSummary {
            get {
                if (ReadNoiseAdu > 0) {
                    var s = $"Bruit de lecture mesuré : {ReadNoiseAdu:0.0} ADU, bias à {BiasLevel:0} ADU";
                    return string.IsNullOrWhiteSpace(BiasInfo) ? s : $"{s}\n{BiasInfo}";
                }
                double eff = EffectiveReadNoiseAdu;
                return eff > 0 ? $"Bruit de lecture saisi : {ReadNoiseElectrons:0.00} e- ÷ {GainElectronsPerAdu:0.####} e-/ADU = {eff:0.0} ADU"
                               : "Bruit de lecture non réglé : mesure-le sur un ou deux bias.";
            }
        }

        private void SetDouble(string name, double value) {
            if (double.IsNaN(value)) return;
            options.SetValueDouble(name, value);
            RaisePropertyChanged(name);
            RaisePropertyChanged(nameof(ReadNoiseSummary));
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Mesure du bias

        private bool measuring;

        public ICommand MeasureBiasCommand { get; }
        public ICommand ClearBiasCommand { get; }

        private string biasStatus = "";
        public string BiasStatus {
            get => biasStatus;
            private set { biasStatus = value; RaisePropertyChanged(); }
        }

        #region Bibliothèque de bias par caméra

        /// <summary>Mesures mémorisées par caméra, gain, offset et binning (fichier commun à tous les profils).</summary>
        internal BiasLibrary Library { get; }

        /// <summary>Liste affichée dans les options.</summary>
        public IReadOnlyList<BiasEntry> BiasEntries => Library.Entries.ToList();

        public bool HasBiasEntries => Library.Entries.Count > 0;

        /// <summary>Ancienne mesure unique enregistrée dans le profil (versions 1.0 à 1.3), non associée à une caméra.</summary>
        public bool HasLegacyBias => ReadNoiseAdu > 0;

        public string LibraryPath => BiasLibrary.FilePath;

        public ICommand DeleteBiasEntryCommand { get; }

        /// <summary>Caméra et réglages de la dernière image analysée : servent si l'en-tête du bias ne les donne pas.</summary>
        internal string LastImageCamera, LastImageGain, LastImageOffset;
        internal int LastImageBinning = 1;

        private void DeleteEntry(BiasEntry e) {
            if (e == null) return;
            Library.Remove(e);
            SaveLibrary();
        }

        private void SaveLibrary() {
            try {
                Library.Save();
            } catch (Exception ex) {
                BiasStatus = $"Enregistrement impossible : {ex.Message}";
                Logger.Error($"FitsAnalyse : enregistrement de {BiasLibrary.FilePath} impossible : {ex.Message}");
            }
            RaisePropertyChanged(nameof(BiasEntries));
            RaisePropertyChanged(nameof(HasBiasEntries));
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Bruit de lecture et niveau du bias à utiliser pour une image : la mesure de sa caméra (mêmes gain, offset et binning),
        /// sinon celle de la même caméra au même gain (niveau du bias alors inconnu), sinon l'ancienne mesure du profil,
        /// sinon la saisie manuelle.
        /// </summary>
        internal BiasResolution ResolveBias(string camera, string gain, string offset, int binning) {
            var (e, match) = Library.Find(camera, gain, offset, binning);
            if (match == BiasMatch.Exact) {
                return new BiasResolution { ReadNoise = e.ReadNoiseAdu, BiasLevel = e.BiasLevel, Description = $"bias : {e.Label}, mesuré le {e.Measured:dd/MM/yyyy}" };
            }
            if (match == BiasMatch.OffsetDiffers) {
                return new BiasResolution {
                    ReadNoise = e.ReadNoiseAdu,
                    Description = $"bias : {e.Label} (offset différent de l'image : bruit de lecture repris, niveau du bias inconnu)"
                };
            }
            // l'ancienne mesure du profil n'a pas de caméra : on ne s'en sert que pour une image sans nom de caméra,
            // pour ne jamais appliquer le bruit de lecture d'une caméra à une autre
            if (ReadNoiseAdu > 0 && string.IsNullOrWhiteSpace(camera)) {
                return new BiasResolution { ReadNoise = ReadNoiseAdu, BiasLevel = BiasLevel, Description = "bias : ancienne mesure du profil, non associée à une caméra" };
            }
            double rn = ReadNoiseElectrons, g = GainElectronsPerAdu;
            if (rn > 0 && g > 0) {
                return new BiasResolution { ReadNoise = rn / g, Description = "bruit de lecture : saisie manuelle (options)" };
            }
            var who = string.IsNullOrWhiteSpace(camera) ? "cette caméra" : camera.Trim();
            var cfg = new List<string>();
            if (!string.IsNullOrEmpty(BiasLibrary.NormalizeNumber(gain))) cfg.Add($"gain {BiasLibrary.NormalizeNumber(gain)}");
            if (!string.IsNullOrEmpty(BiasLibrary.NormalizeNumber(offset))) cfg.Add($"offset {BiasLibrary.NormalizeNumber(offset)}");
            cfg.Add($"{Math.Max(1, binning)}x{Math.Max(1, binning)}");
            return new BiasResolution { Description = $"aucune mesure de bias pour {who} ({string.Join(", ", cfg)})" };
        }

        #endregion

        /// <summary>Choisit un ou deux bias FITS, en déduit le niveau et le bruit de lecture et les range sous la caméra de l'en-tête.</summary>
        internal async Task MeasureBiasAsync() {
            var dlg = new OpenFileDialog {
                Title = "Choisir un ou deux bias (mêmes gain, offset et mode de lecture que les images)",
                Filter = "Fichiers FITS (*.fits;*.fit;*.fts)|*.fits;*.fit;*.fts|Tous les fichiers (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            var files = dlg.FileNames.Take(2).ToList();

            measuring = true;
            BiasStatus = "Mesure en cours…";
            CommandManager.InvalidateRequerySuggested();
            try {
                var r = await Task.Run(() => BiasMeasurement.Measure(files));
                // caméra, gain et offset de l'en-tête du bias ; à défaut, ceux de la dernière image analysée
                bool fromImage = string.IsNullOrWhiteSpace(r.Camera);
                var entry = new BiasEntry {
                    Camera = fromImage ? LastImageCamera ?? "" : r.Camera,
                    Gain = r.Gain ?? (fromImage ? LastImageGain : null) ?? "",
                    Offset = r.Offset ?? (fromImage ? LastImageOffset : null) ?? "",
                    Binning = fromImage && r.Binning <= 1 ? LastImageBinning : r.Binning,
                    ReadNoiseAdu = Math.Round(r.ReadNoise, 2),
                    BiasLevel = Math.Round(r.Level, 1),
                    Measured = DateTime.Now,
                    Method = r.Method,
                    Files = string.Join(", ", files.Select(Path.GetFileName))
                };
                Library.AddOrReplace(entry);
                SaveLibrary();
                BiasStatus = $"{entry.Label} : bruit de lecture {r.ReadNoise:0.0} ADU, niveau du bias {r.Level:0} ADU." +
                             (fromImage ? " (Caméra absente de l'en-tête du bias : rangé sous celle de la dernière image.)" : "");
                Logger.Info($"FitsAnalyse : bias mesuré pour {entry.Label} : RN = {r.ReadNoise:0.00} ADU, niveau = {r.Level:0.0} ADU ({r.Method})");
            } catch (Exception ex) {
                BiasStatus = $"Mesure impossible : {ex.Message}";
                Logger.Error($"FitsAnalyse : mesure du bias impossible : {ex.Message}");
            } finally {
                measuring = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string name = null) {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name))));
            } else {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
