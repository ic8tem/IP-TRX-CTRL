# Panadapter de la radio

Depuis la 0.3.0 la fenêtre peut afficher le **vrai spectre de la radio** (panadapter), et non celui de l'audio comme la cascade CW : en haut le spectre, en dessous la cascade, en bas l'échelle en kHz. La **ligne rouge** est la fréquence de réception, la **bande claire** le filtre.

- **Clic** sur un point : accord à cet endroit (arrondi au pas d'accord, au plus 1 kHz).
- **Molette** : un pas d'accord.
- **Intensità** (intensité) : contraste de la cascade.

## Activation

On l'active **au choix**, à deux endroits :

1. **Sur le serveur** : *Impostazioni › Panadapter della radio (server)* › case **«Panadapter della radio per i client e per questa finestra»** (panadapter pour les clients et pour cette fenêtre ; désactivée à l'installation).
2. **Sur chaque PC** : *Impostazioni › Finestra principale* › **«Panadapter della radio»** (activé par défaut : le cadre n'apparaît que si le serveur le fournit).

Le serveur n'envoie le spectre au client que pendant que le client l'affiche : au plus 10 lignes par seconde, environ 5 ko/s.

## D'où vient le spectre

| Radio | Source | Ce qu'il faut |
|---|---|---|
| **SDR avec CAT via TCI** (ExpertSDR, Thetis…) | flux IQ du récepteur 0 (`iq_start`, 48 000 échantillons/s), transformé en spectre par une FFT de 2048 points autour du centre du panorama (`dds`) | rien : le CAT via TCI suffit |
| **Icom** avec scope (IC-7300, IC-705, IC-9700, IC-7610…) | données de la cascade de la radio, commande CI-V `27 00` | un port CI-V **que Hamlib n'utilise pas** : par exemple le second port USB des radios qui l'offrent comme CI-V (à régler dans le menu de la radio), ou une interface CI-V séparée. Dans les réglages : port, vitesse et **adresse CI-V** de la radio en hexadécimal (IC-7300 `94`, IC-705 `A4`, IC-9700 `A2`, IC-7610 `98` : vérifiez dans le menu de la radio) |

Avec le port Icom, le programme allume le scope (`27 10 01`) et la sortie des données de la cascade (`27 11 01`), et les éteint en quittant. Le scope en mode **centre** affiche centre ± largeur ; en mode **fixe** les deux bords. Si la fréquence est hors du champ du scope fixe, le cadre le signale.

Avec d'autres marques (Yaesu, Kenwood, Elecraft…), pour l'instant le panadapter n'existe qu'avec le TCI.

**Fonction nouvelle, pas encore essayée avec toutes les radios** : si le cadre reste sur «In attesa dei dati» (en attente des données), la ligne d'état à côté du titre et `sessioni.log` (lignes `PANADAPTER`) disent ce qui manque (port, vitesse, adresse).

## Sources

- Icom, *CI-V Reference Guide* IC-9700 et IC-7300MK2 : commande `27 00` (données de la cascade : scope principal ou secondaire, division et nombre de divisions, mode centre ou fixe, fréquences, hors champ, niveaux de 0 à 160), `27 10` (scope allumé/éteint), `27 11` (sortie des données). Comparées entre elles : elles concordent sur le format ; le guide indique que via USB les données arrivent en 11 parties et via LAN d'un seul bloc.
  https://www.icomfrance.com/uploads/files/produit/not-IC-9700_ENG_CI-V_1-en.pdf
  https://icomuk.co.uk/files/icom/PDF/productAdditionalFile/IC-7300MK2_ENG_CI-V_0.pdf
- Bibliothèque TCI `github.com/ftl/tci` (Go) : commandes `iq_start`, `iq_stop`, `iq_samplerate` (48, 96, 192 kHz), `dds`, flux binaire de type 0 = IQ.
  https://pkg.go.dev/github.com/ftl/tci/client


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Panadapter) · [English](EN-Panadapter) · [Español](ES-Panadaptador) · [Deutsch](DE-Panadapter)
