# FitsAnalyse – plugin NINA

Panneau de l'onglet **Imagerie** qui remplace le panneau *Statistiques* : mêmes valeurs, un histogramme interactif et une **suggestion de temps de pose** calculée sur la dernière image.

## Fonctions

- **Statistiques** de la dernière image (ou d'un fichier ouvert) : moyenne, médiane, écart-type, MAD, min et max (avec nombre de pixels), part de pixels saturés, bruit pixel, HFR et nombre d'étoiles (si la détection d'étoiles de NINA est active). Pour une caméra couleur, les valeurs sont données par canal R, V, B, calculées sur les pixels bruts de la matrice de Bayer (sans dématriçage).
- **Temps de pose conseillé** (voir plus bas), arrondi au palier le plus proche de la bibliothèque de darks.
- **Histogramme interactif**
  - mono (tous les pixels) ou canaux R, V, B séparés, avec choix des canaux affichés ;
  - échelle verticale linéaire ou logarithmique ;
  - zoom horizontal à la molette (autour du pointeur) et aux boutons − / + ; « Tout » ou double clic : toute la plage ;
  - glisser : déplacer la vue ; clic : repère avec les valeurs (x ; y) de chaque courbe ; clic droit : effacer le repère ;
  - **Auto** : cadrage sur le fond de ciel des canaux affichés ;
  - **Mémoriser** : l'histogramme courant reste affiché en gris pour être comparé aux suivants, jusqu'au prochain « Mémoriser » ou à « Effacer ».
  - Pour une caméra 12 bits dont les valeurs sont décalées sur 16 bits, les valeurs sont regroupées par pas de 16 ADU (pas d'histogramme « en peigne »).
- **Réglage de l'image de l'onglet Image** (la version étirée est calculée sur les pixels bruts puis affichée par NINA ; le fichier et les statistiques ne changent pas) :
  - **Autostretch** (interrupteur) : même calcul que le bouton Autostretch de NINA (réglages « facteur d'étirement » et « écrêtage du noir » du profil), refait à chaque nouvelle image ;
  - **AutoWB** (interrupteur, images couleur) : aligne les canaux R et B sur le vert ; avec le niveau du bias mesuré, c'est un vrai gain qui neutralise le fond de ciel ;
  - **courbe de transfert** en orange sur l'histogramme, avec trois curseurs (noir, tons moyens, blanc) sous le tracé, comme dans Photoshop ou Siril ; l'image est mise à jour quand on relâche le curseur ;
  - **Étirement séparé par canal** (interrupteur, images couleur ; réglé au départ comme l'option « étirement non lié » du profil NINA) : l'Autostretch calcule un noir et des tons moyens par canal, et les courbes R, V, B sont tracées en pointillés de couleur. Les curseurs règlent les canaux affichés en mode RVB : affiche un seul canal pour régler sa courbe ;
  - **Reset** : revient à l'image d'origine.
- Les boutons sont des icônes, avec une infobulle pour chacun.
- **Ouvrir…** une image déjà acquise (FITS, XISF, TIFF) : elle s'affiche dans l'onglet Image de NINA et dans FitsAnalyse ; la pose suivante de la caméra la remplace.

## Le calcul du temps de pose

Le bruit d'un pixel du fond de ciel vaut σ² = σ_ciel² + RN² (en ADU²). Le ciel (et le courant d'obscurité) suivent une loi de Poisson : leur variance croît comme la durée de pose. On vise σ_ciel² = k × RN² (k = 10 par défaut, soit environ 5 % de bruit en plus à cause de la lecture) :

    t_opt = t × k × RN² / (σ² − RN²)

- σ est mesuré sur l'image par **différences de pixels voisins de même couleur** (distants de deux colonnes), avec rejet des étoiles et pixels chauds : la mesure est insensible aux gradients et aux nébuleuses.
- RN, le bruit de lecture, est **mesuré sur un ou deux bias** (Options > Plugins > FitsAnalyse, ou bouton « Mesurer un bias… » du panneau). Avec deux bias, le bruit vient de leur différence (le motif fixe s'annule).
- Les mesures sont **mémorisées par caméra, gain, offset et binning** (lus dans l'en-tête du bias : INSTRUME, GAIN, OFFSET, XBINNING), dans `%LOCALAPPDATA%\NINA\FitsAnalyse\BiasLibrary.json`, commun à tous les profils NINA. Chaque image utilise automatiquement la mesure de sa caméra ; si seul l'offset diffère, le bruit de lecture est repris mais pas le niveau du bias.
- Tout est en ADU du fichier : **le gain de la caméra n'est pas nécessaire**. Il faut seulement que les bias soient pris avec les mêmes gain, offset et mode de lecture que les images.
- À défaut de bias, on peut saisir le bruit de lecture en électrons et le gain en e-/ADU du fichier (gain du fabricant ÷ 16 pour un capteur 12 bits, ÷ 4 pour 14 bits).

Le résultat vaut pour le ciel du moment et le filtre utilisé : à refaire si la Lune, la hauteur de la cible ou le filtre changent. Pour une caméra couleur, la suggestion principale porte sur tous les pixels ; la ligne « Pose optimale » du tableau donne la valeur par canal.

## Compilation

Projet .NET 8 (`FitsAnalyse.csproj`), paquet `NINA.Plugin` 3.2.0.9001. Après compilation, la DLL est copiée dans `%LOCALAPPDATA%\NINA\Plugins\3.0.0\FitsAnalyse`.

Diffusion : compiler en Release puis lancer `Installer\Publier.ps1` (installateur Inno Setup et `manifest.json`).

## Licence

MPL-2.0 (voir LICENSE.txt).
