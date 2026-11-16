# Fenêtre principale

Depuis la 0.3.0 la fenêtre est plus petite : elle s'ouvre à **1080 × 760** points (avant 1240 × 816) et peut être réduite jusqu'à 900 × 520 ; si le contenu ne tient pas, une barre de défilement apparaît. Elle est organisée ainsi :

1. **En-tête** sur une ligne : nom du programme, état de la liaison, LED UPnP, rôle (CLIENT ou SERVER), couleurs, **Impostazioni** (réglages), **Inverti ruoli** (inverser les rôles), **Help**. En dessous, quand il y en a une, la ligne de la **nouvelle version** avec **Aggiorna adesso** (mettre à jour maintenant) (voir *Journaux et résolution des problèmes*).
2. **Trois colonnes** : radio · instruments et PTT · l'autre PC.
3. **Panadapter de la radio**, s'il est activé (voir *Panadapter*).
4. **Onglets** en bas : **Keyer**, **CW** (seulement en mode CW), **Audio**, **Collegamento** (liaison).
5. Ligne des **avis** (serveur occupé, émission non autorisée, durée maximale…).

Les nouvelles parties se masquent depuis **Impostazioni › Finestra principale** (réglages › fenêtre principale) : boutons des bandes et pas, commandes de la radio, mémoires, instrument ALC, panadapter, contrôle de nouvelle version et mises à jour automatiques. La fenêtre reste ainsi encore plus basse.

## Radio

- **VFO A** en grands chiffres, avec le mode. **Molette sur les chiffres** : avec la souris sur un chiffre, la molette le fait monter ou descendre, avec retenue (VFO B aussi).
- Case **Nuova QRG** (nouvelle fréquence) en Hz, mode, **Imposta** (appliquer).
- **Pas d'accord** (de 1 Hz à 25 kHz) avec les boutons ◀ ▶ (la molette sur les boutons marche aussi).
- **Bandes** de 160 m à 70 cm : un clic mène à la dernière fréquence et au dernier mode utilisés sur cette bande (la première fois, à une fréquence typique). La bande utilisée est mise en évidence.
- **VFO B** sur une ligne avec **A/B**, **A=B**, **SPLIT** et le décalage (−, valeur, +). L'explication du split est dans l'infobulle du bouton.
- **Potenza** (puissance) TX.
- **Commandes de la radio** (voir *Radio, split et puissance*) : NR, NB, NOTCH, COMP, ATT, PRE, AGC, SQL.
- **Memorie** (mémoires) (voir *Radio, split et puissance*).

## Instruments et PTT

- **S**, **Po** (puissance de sortie), **ROS**, **ALC** (en émission), **Gain** (gain TX de la radio). La barre du ROS clignote avec la protection ROS.
- Voyant **RX/TX**.
- **PTT** «appuyer et maintenir», aussi avec la barre d'espace. Sous le PTT, une ligne indique depuis combien de temps dure l'émission et la **durée maximale**, ou pourquoi l'émission **n'est pas autorisée** (écoute seule, opérateur à la radio). Quand elle ne l'est pas, le bouton est estompé et affiche «PTT NON PERMESSO».
- Sur le client : méthode PTT et délai réglés sur le serveur.

## L'autre PC

- Client : liste des **serveurs appairés** et **Abbina…** (appairer).
- Indicatif, IP, heure de connexion, latence, niveau de l'audio reçu.
- Cadres repliables : **Reti dei due PC** (réseaux des deux PC : IPv4/IPv6, chemin de l'audio, version de l'autre PC), **Metodo PTT e CW** (méthode PTT et CW, serveur seulement), **Schede audio del server** (cartes son du serveur).

## Onglets

- **Keyer** : F1…F8 (WAV en phonie, macros en CW). Voir *Keyer, messages WAV et macros CW*.
- **CW** : apparaît en mode CW et s'ouvre seul ; audio RX sur une ligne, pitch, largeur, vitesse, seuil, filtre de la radio, cascade et décodeur. F1…F8 marchent aussi d'ici. Voir *Mode CW*.
- **Audio** : micro et haut-parleur, volumes, renfort et niveaux.
- **Collegamento** (liaison) : état, latence, gigue, paquets perdus, tampon, broker MQTT et, sur le serveur, le code de secours.


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Finestra-principale) · [English](EN-Main-window) · [Español](ES-Ventana-principal) · [Deutsch](DE-Hauptfenster)
