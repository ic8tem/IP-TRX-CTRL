# Appairage et rendez-vous via MQTT

L'appairage se fait une seule fois pour chaque serveur : ensuite, clés, ports et reconnexions sont automatiques.

## Comment appairer

1. Sur le **serveur**, on ouvre la fenêtre *Abbinamento* (appairage) : un numéro de 10 chiffres apparaît (par ex. `482 913 0571`, depuis la 0.3.0 ; avant, c'était un long code comme `R4VGZ-TRGSF-…`). Le client peut aussi utiliser le code fixe du serveur à 12 chiffres, s'il est activé (voir plus bas).
2. Sur le **client**, on saisit ce numéro et on appuie sur **Abbina** (appairer). Il est valable 10 minutes.
3. Les deux programmes échangent une clé secrète (ECDH P-256) et dès lors tout le trafic est chiffré en AES-256-GCM.

## Le rôle du broker MQTT

Le serveur n'a pas d'adresse fixe. Pour être trouvé, il dépose son adresse IP et ses ports, **chiffrés**, sur un canal d'un broker MQTT public. Le nom du canal est dérivé de la clé secrète : seul celui qui a appairé les deux PC sait où regarder et peut lire le contenu. Si un broker ne répond pas, le programme passe au suivant.

L'appairage passe lui aussi par le broker : le client annonce sa présence sur le canal dérivé du numéro, le serveur répond avec son adresse actuelle (chiffrée), puis le client ouvre la connexion directe. Depuis la 0.3.0, le numéro ne contient pas l'adresse : elle n'arrive que par ce chemin.

## Le journal du dialogue

La fenêtre d'appairage montre, ligne par ligne, le dialogue avec le broker et avec le serveur :

```
20:01:02.104  ▶  test.mosquitto.org:1883  CONNECT  id=aal-3f9a21
20:01:10.106  ✗  nessuna risposta in 8 s → broker successivo
20:01:10.110  ▶  broker.hivemq.com:1883  CONNECT  id=aal-3f9a21
20:01:10.296  ◀  CONNACK  accettato
20:01:10.298  ▶  SUBSCRIBE  aal/pair/7c1e…d4/srv  QoS 1
20:01:10.417  ◀  SUBACK  QoS 1
20:01:10.419  ▶  PUBLISH  aal/pair/7c1e…d4/cli  HELLO cifrato 88 B
20:01:11.020  ◀  PUBLISH  …/srv  server presente · IP 93.41.127.18
                    porte 51234 / 51235 · UPnP ok · versione 3.1
20:01:11.022  ▶  TCP 93.41.127.18:51234  apertura canale di controllo
20:01:11.140  ◀  handshake ECDH P-256 ok · chiave AES-256 derivata
20:01:11.143  ✓  Abbinamento completato
```

`▶` = envoyé, `◀` = reçu, `✗` = erreur, `✓` = terminé. **Copia registro** (copier le journal) place le texte dans le presse-papiers, utile pour voir où s'arrête un appairage qui échoue. Le journal peut aussi être enregistré dans le log de session.

## Problèmes fréquents

| Ce qu'on voit dans le journal | Cause probable |
|---|---|
| Aucun broker ne répond | Pas de connexion internet, ou port 1883 bloqué par le réseau |
| Le broker répond mais pas le serveur | Le programme serveur est fermé, ou n'attend pas encore l'appairage |
| Le serveur répond mais le TCP ne s'ouvre pas | Port non ouvert sur le routeur (UPnP désactivé ou réseau du fournisseur avec IP partagée) : depuis la 0.2.7 le cycle des ports démarre |
| «nessuna risposta» (pas de réponse) en réseau local avec la même IP publique | Le pare-feu Windows du serveur rejette les connexions : sur le serveur, Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (autoriser dans le pare-feu Windows) |
| «connessione rifiutata» (connexion refusée) | Le programme serveur est fermé ou écoute sur un autre port |
| «le porte del server NON sono raggiungibili» et «CGNAT» | Le serveur ne peut pas recevoir de connexions depuis internet : il faut un port ouvert sur le routeur du client (UPnP, DMZ ou redirection), que le cycle des ports utilise pour la connexion inverse |

## Cycle des ports pendant l'appairage (depuis la 0.2.7)

Si la connexion directe échoue, l'appairage ne s'arrête pas : jusqu'à **20 tours** d'échange de ports démarrent, toujours via le broker MQTT et chiffrés avec la clé dérivée du numéro d'appairage.

1. Le client ouvre un port **nouveau** sur son routeur (un tour sur deux, un port courant qui passe souvent les filtres, comme 443, 80, 8080, 8443, ou un port au hasard), le vérifie depuis internet et le propose au serveur.
2. Le serveur essaie de se connecter à ce port : c'est la **connexion inverse**, la seule possible quand le serveur est derrière un CGNAT.
3. Si cela échoue, le serveur ouvre à son tour un port **nouveau**, le vérifie depuis internet et communique au client son numéro et le résultat.
4. Le client essaie aussitôt le nouveau port du serveur (en réseau local aussi).

Aucun port n'est essayé deux fois, ni sur le client ni sur le serveur. Si le réseau du client ne laisse sortir que certains ports, le serveur essaie ceux-là en premier. Un port du serveur trouvé joignable devient son port de contrôle. À la fin, si rien n'a marché, la dernière ligne du journal dit pourquoi (par exemple : serveur derrière CGNAT et routeur du client sans UPnP).

## Numéro d'appairage à 10 chiffres (depuis la 0.3.0)

Le serveur affiche un **numéro de 10 chiffres** (par exemple `482 913 0571`) à la place de l'ancien code long.

- Il est valable **10 minutes** et **une seule fois** ; une fois expiré, on appuie sur «Riprova» (réessayer) sur le serveur pour en obtenir un nouveau.
- Il **ne contient pas l'adresse** du serveur : le client la reçoit **chiffrée** (AES-256-GCM) depuis le broker MQTT. Sans au moins un broker joignable, l'appairage est impossible.
- La clé est dérivée du numéro par un **PBKDF2-SHA256** lent (200 000 itérations), avec comme sel la fenêtre de 10 minutes où le numéro est né. Essayer les 10 milliards de numéros prendrait trop de temps, et une table précalculée ne sert à rien car chaque fenêtre a son propre sel. Le client essaie la fenêtre actuelle, les deux précédentes et la suivante, pour tenir compte de l'attente et d'horloges légèrement différentes (quelques secondes de calcul).
- Le reste de l'appairage est inchangé : échange ECDH P-256 authentifié par la clé dérivée du numéro, puis clé de 256 bits pour les sessions.

## Serveurs appairés (client, depuis la 0.3.0)

Le client garde une **liste de serveurs**, chacun avec sa clé de 256 bits conservée comme avant (DPAPI sous Windows, trousseau sous macOS, fichier chiffré sous Linux).

- Dans le cadre du serveur de la fenêtre principale, on choisit le serveur : le client est connecté à **un seul à la fois**, et en choisir un autre ferme la connexion en cours.
- **Abbina nuovo…** (appairer un nouveau) ajoute un serveur avec son numéro ; les précédents restent.
- Dans **Impostazioni › Abbinamento e registri** (réglages › appairage et journaux), on renomme un serveur ou on le retire de la liste (sa clé est effacée).
- Le nom initial est l'indicatif du serveur, dès que le serveur le communique.
- Un appairage fait avec une 0.2.x devient de lui-même le premier serveur de la liste : inutile d'appairer à nouveau.

## Clients appairés (serveur, depuis la 0.3.0)

Le serveur garde lui aussi une **liste de clients**, chacun avec sa clé : un nouvel appairage ne retire **pas** l'accès aux autres.

- **Un seul client à la fois** se connecte (il y a une seule radio). Si un deuxième client essaie d'entrer pendant qu'un autre opère, il reçoit «Il server è occupato: è collegato …» (le serveur est occupé : … est connecté) avec l'indicatif de celui qui opère, et réessaie seul toutes les 30 secondes.
- Le même client qui se reconnecte (par exemple après un changement de réseau) reprend sa place sans attendre.
- Dans **Impostazioni › Abbinamento e registri**, on voit la liste avec l'indicatif, le mode d'appairage (numéro temporaire, code fixe, échange des rôles, appairage précédent) et la dernière utilisation. On peut renommer un client ou **lui retirer l'accès** («Togli accesso») : sa clé est effacée et, s'il est connecté, il est déconnecté aussitôt.
- **Abbina un nuovo client** (appairer un nouveau client) ouvre la fenêtre avec un numéro temporaire ; les clients déjà appairés restent.
- Un appairage fait avec une 0.2.x devient de lui-même le premier client de la liste.
- «Esporta chiavi per le autorità» (exporter les clés pour les autorités) exporte les clés de tous les clients.

## Code fixe du serveur (depuis la 0.3.0)

En plus du numéro temporaire, le serveur peut avoir un **code fixe de 12 chiffres** (par exemple `4829 1305 7712`), comme certains programmes de prise de main à distance. On l'active dans **Impostazioni › Abbinamento e registri** avec «Accetta nuovi client con il codice fisso del server» (accepter de nouveaux clients avec le code fixe du serveur) ; il est **désactivé** à l'installation.

- Le code est **toujours** valable, jusqu'à ce que vous le changiez («Cambia codice») ou désactiviez l'option. Celui qui le reçoit s'appaire quand il veut, même des jours plus tard, en le saisissant dans la même case que le numéro ; ensuite il entre dans la liste des clients et n'en a plus besoin.
- Il ne fonctionne qu'avec le programme serveur lancé et au moins un broker MQTT joignable, comme le numéro temporaire ; là aussi, l'adresse n'arrive au client que chiffrée.
- La clé est dérivée du code par PBKDF2-SHA256 à 600 000 itérations (trois fois le numéro temporaire) et le code a deux chiffres de plus : essayer tous les codes demanderait des années de calcul.
- **Sécurité :** c'est pratique mais moins sûr que le numéro temporaire, car celui qui découvre le code peut s'appairer à tout moment. Ne le donnez qu'à des personnes de confiance, changez-le s'il a trop circulé et retirez de la liste les clients qui ne doivent plus entrer. Changer le code ne retire pas l'accès aux clients déjà appairés.
- Les chiffres du code sont conservés comme les clés (DPAPI, trousseau macOS, fichier chiffré sous Linux), pas dans le fichier des réglages.

## Adresses chiffrées

L'adresse IP du serveur ne circule jamais en clair : sur les brokers MQTT (appairage, recherche après un changement d'IP) elle est chiffrée en AES-256-GCM, et les noms des canaux MQTT sont des empreintes dérivées de la clé, qui ne révèlent ni l'adresse ni l'indicatif. Le code de secours, pour quand les brokers ne répondent pas, contient lui aussi l'adresse chiffrée.


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Abbinamento-e-MQTT) · [English](EN-Pairing-and-MQTT) · [Español](ES-Emparejamiento-y-MQTT) · [Deutsch](DE-Kopplung-und-MQTT)
