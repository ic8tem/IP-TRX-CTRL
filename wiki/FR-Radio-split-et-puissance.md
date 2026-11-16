# Radio, split et puissance

Toutes les commandes vers la radio passent par le serveur, qui utilise **Hamlib** (`rigctld`) ou, depuis la 0.2.5, le **TCI** des programmes SDR (voir *Connexion TCI*). Au début de la liaison, le serveur reconnaît toujours le modèle de radio et les commandes qu'il prend en charge.

## VFO A, VFO B et split

| Commande | Effet |
|---|---|
| **A/B** | échange les deux VFO |
| **A=B** | copie le VFO A dans le VFO B |
| **SPLIT** | réception sur A, émission sur B |

Le split se règle comme **décalage** du VFO B par rapport au VFO A :

- plage : de **−50 kHz** à **+50 kHz** ;
- pas de **1 kHz** dans les modes phonie (BLU) ;
- pas de **0,1 kHz** en CW ;
- boutons − et +, case de la valeur ; la molette de la souris déplace d'un pas (depuis la 0.3.0 il n'y a plus de curseur, pour une fenêtre plus petite).

## Puissance TX

- Curseur de **1 à 100 W**.
- Si la radio, ou Hamlib pour ce modèle, ne prend pas en charge la conversion en watts, le curseur passe automatiquement à **1–100 %**.
- La commande (niveau `RFPOWER`) part au relâchement du curseur ; après chaque modification, la valeur est relue sur la radio et affichée à côté, avec le modèle et l'unité utilisée (p. ex. «IC-7300 · watt»).

## Protection ROS

Depuis la 0.2.4, le serveur surveille le **ROS** (SWR) lu sur la radio pendant l'émission. La protection est **active dès l'installation**.

| ROS | Ce qui se passe |
|---|---|
| dès **1,9** | avertissement : la barre du ROS clignote en rouge, sur le serveur et sur le client |
| dès **2,5** pendant deux lectures de suite (environ une seconde) | blocage : le serveur coupe le PTT, arrête messages WAV, texte et manipulateur CW et l'audio vers la radio (même avec PTT par VOX) ; une fenêtre apparaît sur le client |

L'émission reste bloquée jusqu'à ce que l'opérateur appuie sur **«Sblocca la trasmissione»** (débloquer l'émission) dans la fenêtre. S'il l'a fermée avec «Lascia bloccata» (laisser bloquée), un clic sur la barre du ROS la rouvre. Après le déblocage, il faut appuyer à nouveau sur le PTT : même si la pédale ou le bouton étaient restés appuyés, la radio ne repasse pas seule en émission.

Avant de débloquer : contrôler antenne, câble et connecteurs, boîte d'accord, et que la fréquence est dans la bande de l'antenne.

**Impostazioni › Radio (server)** :

- case **Protezione ROS** (protection ROS) pour l'activer ou la désactiver (à la désactivation le programme demande confirmation) ;
- **Avviso da ROS** (avertissement à partir du ROS, 1,9 par défaut) et **Blocco da ROS** (blocage à partir du ROS, 2,5 par défaut) ; le blocage va de 1,2 à 10, l'avertissement reste sous le blocage.

Les réglages valent sur le serveur, qui mesure le ROS. Depuis le client on peut les changer quand il est connecté à un serveur 0.2.4 ou ultérieur : ils sont envoyés au serveur à l'enregistrement. Chaque blocage, déblocage et changement de réglage est écrit dans `sessioni.log` avec le préfixe `ROS`.

La protection ne fonctionne que si la radio fournit le ROS à Hamlib (niveau `SWR`) ; sinon, la barre reste sur «—» et `sessioni.log` le signale une fois. Elle ne remplace pas les protections internes de la radio et de l'amplificateur.

## Sécurité en émission (depuis la 0.3.0)

C'est le serveur qui décide, pour tous les clients. **Impostazioni › Sicurezza in trasmissione (server)** (sécurité en émission) :

| Option | Par défaut | Ce qu'elle fait |
|---|---|---|
| **Tempo massimo di una trasmissione** (durée maximale d'une émission) | active, 180 s | compte les émissions commandées par le programme (PTT, messages WAV, manipulateur CW sur ligne série, programmes numériques). 15 secondes avant la fin, la ligne sous le PTT devient rouge ; à l'échéance le PTT est coupé et **reste coupé jusqu'à ce que l'opérateur le relâche**. De 30 à 600 s |
| **Niente cambi di frequenza in trasmissione** (pas de changement de fréquence en émission) | active | pendant que la radio émet, le serveur refuse les changements de fréquence, mode, filtre, VFO B, split et A/B ; l'avis apparaît sur le client |
| **Precedenza a chi è alla radio** (priorité à celui qui est à la radio) | active, 3 minutes | si la radio émet sans que le programme l'ait commandé (micro ou PTT de la radio), ou si on appuie sur le PTT du programme sur le serveur, le client ne peut pas émettre pendant les minutes choisies (de 1 à 30). Le blocage s'allonge tant que l'émission locale continue ; sur le serveur, un clic sur la ligne sous le PTT le lève plus tôt |
| **Codice fisso in solo ascolto** (code fixe en écoute seule) | active | celui qui s'appaire avec le code fixe du serveur entre en **écoute seule** ; c'est le serveur qui l'autorise |

**Écoute seule** : dans **Impostazioni › Abbinamento e registri**, le serveur choisit pour chaque client **«Può trasmettere»** (peut émettre) ou non. Un client en écoute seule entend la radio, change de fréquence et utilise le panadapter, mais n'émet pas (PTT, messages, CW, programmes numériques), ne débloque pas la protection ROS, ne modifie pas les mémoires du serveur et ne demande pas l'échange des rôles.

Remarques :

- La priorité fonctionne avec les radios qui indiquent via CAT quand elles émettent (commande Hamlib `t`). Le CW envoyé au keyer de la radio compte comme émission du programme pendant la durée estimée du texte.
- Avec le PTT par VOX, la durée maximale coupe le PTT du programme, mais c'est la radio qui décide quand cesser d'émettre.
- La durée maximale ne touche pas les émissions faites par la radio elle-même (opérateur à la radio).
- Chaque événement est écrit dans `sessioni.log` (lignes `TEMPO MASSIMO`, `QSY rifiutato`, `PRECEDENZA`, `TRASMISSIONE remota rifiutata`).

## Commandes de la radio (depuis la 0.3.0)

Rangée de boutons sous la puissance (elle se masque depuis *Impostazioni › Finestra principale*). Ce sont les commandes standard de Hamlib : elles fonctionnent avec les radios pour lesquelles Hamlib les prend en charge :

| Bouton | Commande Hamlib | Fonction |
|---|---|---|
| **NR** | fonction `NR` | réduction de bruit |
| **NB** | fonction `NB` | noise blanker |
| **NOTCH** | fonction `ANF` | notch automatique |
| **COMP** | fonction `COMP` | compresseur du micro de la radio |
| **ATT** | niveau `ATT` | atténuateur : chaque clic passe à la valeur suivante acceptée par la radio, puis arrêt |
| **PRE** | niveau `PREAMP` | préamplificateur, comme l'atténuateur |
| **AGC** | niveau `AGC` | rapide, moyen, lent |
| **SQL** | niveau `SQL` | squelch, 0–100 % |

Un bouton gris signifie que la radio (ou Hamlib pour ce modèle) ne fournit pas cette commande. Le serveur lit ces valeurs toutes les 4 secondes et juste après chaque commande. Avec le **CAT via TCI**, NR, NB, NOTCH et SQL fonctionnent (commandes TCI `rx_nr_enable`, `rx_nb_enable`, `rx_anf_enable`, `sql_enable` et `sql_level`).

## Bandes, pas et mémoires (depuis la 0.3.0)

- **Bandes** de 160 m à 70 cm : un clic mène à la dernière fréquence et au dernier mode utilisés sur cette bande, la première fois à une fréquence typique (p. ex. 14,200 MHz USB). Les dernières fréquences sont enregistrées sur ce PC.
- **Pas d'accord** de 1 Hz à 25 kHz avec les boutons ◀ ▶ et la molette sur les boutons ; il vaut aussi pour la molette sur le panadapter.
- **Memorie** (mémoires) : «Salva…» (enregistrer) demande le nom et où l'enregistrer : **in questo PC** (sur ce PC, pour vous seul) ou **sul server** (sur le serveur), commune à tous les clients. Dans la liste, les mémoires sont marquées «PC» ou «Server» ; **Vai** (aller, ou double clic) les rappelle, **Elimina** (supprimer) les retire. Sur le serveur, les mémoires du serveur se gèrent directement. Seul un client qui peut émettre modifie les mémoires du serveur.

Source des noms des commandes Hamlib : manuel de `rigctl`/`rigctld` (fonctions `NR`, `NB`, `ANF`, `COMP` ; niveaux `ATT`, `PREAMP`, `AGC`, `SQL`, `ALC`), https://hamlib.sourceforge.net/html/rigctl.1.html


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Radio-split-e-potenza) · [English](EN-Radio-split-and-power) · [Español](ES-Radio-split-y-potencia) · [Deutsch](DE-Funkgerät-Split-und-Leistung)
