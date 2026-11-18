using System.Reflection;
using System.Runtime.InteropServices;

// Identifiant unique du plugin : ne plus jamais le changer après la première diffusion
[assembly: Guid("f068a0d7-32a8-481c-864e-6b21455f2394")]

[assembly: AssemblyTitle("FitsAnalyse")]
[assembly: AssemblyDescription("Statistiques, histogramme interactif et suggestion de temps de pose pour la dernière image de NINA.")]
[assembly: AssemblyCompany("Fred")]
[assembly: AssemblyProduct("FitsAnalyse")]
[assembly: AssemblyCopyright("Copyright © 2026 Fred")]

[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]

[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.0")]
[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "https://github.com/fredM1968/FitsAnalyse")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/fredM1968/FitsAnalyse")]
[assembly: AssemblyMetadata("Tags", "Histogramme,Statistiques,Temps de pose,FITS")]
[assembly: AssemblyMetadata("ShortDescription", "Statistiques, histogramme interactif et temps de pose conseillé pour la dernière image.")]
[assembly: AssemblyMetadata("LongDescription", "FitsAnalyse remplace le panneau Statistiques de l'onglet Imagerie. Il reprend les mêmes valeurs (moyenne, médiane, écart-type, MAD, min, max, HFR, étoiles), par canal pour les caméras couleur, et ajoute :\n\n- une suggestion de temps de pose calculée sur la dernière image : le bruit du fond de ciel, mesuré par différences de pixels voisins, est comparé au bruit de lecture de la caméra (mesuré sur un bias) ; le résultat est arrondi au palier le plus proche de la bibliothèque de darks ;\n- un histogramme interactif : mono ou R, V, B séparés, échelle linéaire ou logarithmique, zoom à la molette et aux boutons, déplacement à la souris, valeurs (x ; y) au clic, cadrage automatique sur le fond de ciel, histogramme de référence en gris pour comparer deux images ;\n- l'ouverture d'un fichier déjà acquis (FITS, XISF, TIFF), affiché dans l'onglet Image ; l'image suivante de la caméra le remplace.")]

[assembly: ComVisible(false)]
