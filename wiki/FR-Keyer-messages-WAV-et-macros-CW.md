# Keyer : messages WAV et macros CW

Le keyer fonctionne **seulement sur le client**. Il reprend le programme WavPTT, réécrit en C# dans IP-TRX-CTRL. Les mêmes touches F1…F8 changent de fonction selon le mode de la radio.

Commandes communes : **F1…F8** dans la fenêtre, **Ctrl+Alt+F1…F8** aussi depuis d'autres programmes, **Échap** interrompt.

## Modes phonie (BLU) : messages WAV

- Chaque touche joue un fichier WAV ; l'audio va à la radio distante via le serveur, comme si l'on parlait au micro, et le PTT est géré automatiquement.
- Les fichiers sont convertis à 8 kHz au chargement.
- Chaîne audio : conversion à 8 kHz → **normalisation** (crête à −1 dB sous la pleine échelle, pour que tous les fichiers partent du même niveau) → compresseur (arrêt ou de 3 à 13 dB) → filtre (aucun, 300–2700, 500–2400, 500–2100 Hz) → renfort du curseur VOLUME → limiteur.
- Renfort du curseur VOLUME (comme WavPTT) : gain = 1000^(volume/100).

| Curseur | Gain |
|---|---|
| 0 | silence |
| 33 | ×10 (+20 dB) |
| 50 | ×31,6 (+30 dB) |
| 66 | ×100 (+40 dB) |
| 100 | ×1000 (+60 dB) |

- Le micro ne passe pas par cette chaîne : TX gain et «Boost oltre soglia» (renfort au-dessus du seuil) ne valent que pour la voix.
- Plusieurs touches appuyées à la suite sont mises en file ; le PTT est relâché à la fin du dernier message.
- Le bouton **File WAV…** ouvre la fenêtre **Memorie WAV (F1 – F8)** (mémoires WAV), reprise de WavPTT :
  - **…** choisit le fichier pour cette touche ;
  - **Play** / **Stop** écoute d'essai sur la sortie audio locale, sans PTT ;
  - **Salva** (enregistrer) copie les fichiers dans le dossier WAV à côté du programme ; **Annulla** (annuler) ferme sans enregistrer.

## Mode CW : macros de texte

- En appuyant sur F1…F8, le client remplace les variables et **transmet au serveur le texte de la macro sous forme de chaîne**, avec la vitesse choisie.
- Le serveur l'émet de l'une de deux façons, choisie dans le cadre PTT du serveur à la ligne **Invio CW** (envoi CW) :
  - **CAT** : le texte va au keyer interne de la radio (commande Hamlib `send_morse`, vitesse avec `KEYSPD`) ;
  - **manipulateur sur la ligne DTR ou RTS** d'un port série : le serveur lève le PTT sur le port indiqué, attend le délai PTT,
    manipule le CW sur la ligne choisie avec les durées Morse à la vitesse reçue, attend 3 points et relâche le PTT.
    Plusieurs macros à la suite restent sous le même PTT. Échap interrompt aussitôt : manipulateur relevé et PTT relâché.
- Le bouton **Macro…** ouvre la fenêtre **Macro CW (F1 – F8)** : pour chaque touche, le nom affiché sur la touche et le texte à émettre. **Prova** (essai) fait écouter le texte en local, sans émettre.
- Variables utilisables dans les textes :

| Variable | Signification |
|---|---|
| `{MYCALL}` | mon indicatif |
| `{CALL}` | l'indicatif du correspondant |
| `{NR}` | numéro de série |
| `{RST}` | report |

- Sous les touches, on règle la vitesse d'émission, mon indicatif, l'indicatif du correspondant et le numéro.

### Ports série pour PTT et CW (serveur)

| Réglage | Effet |
|---|---|
| PTT sur RTS ou DTR, port X | le serveur commande la ligne du port X |
| CW sur DTR ou RTS, port Y (ou «même port que le PTT») | le serveur manipule le CW sur la ligne du port Y |
| PTT et CW sur le **même port** | ils utilisent des lignes opposées : si le CW est sur DTR, le PTT va sur RTS, et inversement |
| port PTT identique au port CAT | le PTT est commandé par rigctld (le port est le sien) |
| port CW identique au port CAT | impossible d'y manipuler le CW : le CW passe par le CAT. Utilisez le second port série de la radio (par exemple le port «Standard» des Yaesu) ou un adaptateur USB-série |

Les choix effectifs et les éventuels déplacements de ligne sont affichés sous les réglages et écrits dans `sessioni.log` (lignes `PTT/CW`).

## Sur le serveur

Le serveur n'a ni touches F ni fichiers WAV : il garde la méthode PTT vers la radio (RTS, DTR, CAT, VOX ou TCI), le délai PTT et le mode d'envoi du CW (CAT ou manipulateur sur DTR/RTS).


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Keyer-messaggi-WAV-e-macro-CW) · [English](EN-Keyer-WAV-messages-and-CW-macros) · [Español](ES-Keyer-mensajes-WAV-y-macros-CW) · [Deutsch](DE-Keyer-WAV-Meldungen-und-CW-Makros)
