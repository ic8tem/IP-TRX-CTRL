# Ports et liaison

Le serveur et le client se connectent directement, sans serveur intermédiaire pour l'audio. Pour que cela marche, au moins un des deux PC doit avoir des ports joignables depuis internet. IP-TRX-CTRL les ouvre tout seul avec **UPnP** et vérifie qu'ils fonctionnent vraiment.

## UPnP au démarrage et la LED

Au démarrage, sur le serveur comme sur le client, le programme cherche le routeur avec **UPnP** et **NAT-PMP** (trois essais de 10 secondes) et affiche une fenêtre avec le résultat :

- **UPnP actif** : le routeur peut ouvrir les ports tout seul ; la fenêtre indique le protocole et l'IP externe déclarée par le routeur ;
- **UPnP introuvable** : la fenêtre indique la cause la plus probable et que faire :
  - réseau Windows réglé sur «Public» (Windows bloque les réponses du routeur : il faut le mettre sur «Privé») ;
  - VPN ou autres cartes réseau virtuelles actives ;
  - UPnP désactivé sur le routeur : l'activer depuis la page du routeur (sur une FRITZ!Box aussi pour le PC concerné).

En haut de la fenêtre principale, la **LED «UPnP»** reste **verte clignotante** tant que le routeur répond (contrôle toutes les 60 secondes), **rouge fixe** si UPnP manque et **grise** pendant la recherche. Si UPnP était actif et que le routeur cesse de répondre, la fenêtre réapparaît. Un clic sur la LED la rouvre à tout moment.

## UPnP et NAT-PMP (depuis la 0.2.8)

Le programme cherche le routeur **d'abord avec UPnP** (deux essais de 8 secondes) et seulement si UPnP ne répond pas, avec **NAT-PMP**. Avant la 0.2.8, le premier protocole qui répondait était retenu : certains routeurs répondent à NAT-PMP puis refusent tous les ports, et le journal affichait «Failed to create portmap» et «Arithmetic operation resulted in an overflow» (une limite de la bibliothèque Open.NAT avec NAT-PMP sur les ports au-delà de 32767).

Si le routeur refuse 5 ports de suite, le programme essaie l'autre protocole. Si celui-ci refuse aussi, il utilise un port du PC : cela marche en réseau local, avec une DMZ ou une redirection manuelle, et pendant l'appairage on continue avec le cycle des ports et la connexion inverse. Avec NAT-PMP les ports choisis restent sous 32768.

## Le serveur

Au démarrage, le serveur :

1. cherche le routeur avec UPnP ;
2. ouvre un port au hasard pour le **contrôle** (TCP) et un pour l'**audio** (TCP, et le même port en UDP) ;
3. se met à l'écoute et demande à **deux services externes indépendants** de se connecter à ce port : **ifconfig.co** et **portchecker.io** (tous deux open source). Il suffit que l'un des deux entre pour que le port soit bon ; si aucun n'entre, le serveur ferme le port et en essaie un autre (jusqu'à 6 fois par port). Si aucun des services ne répond, le port est gardé, et le journal le dit ;
4. les **deux premiers ports utilisables** sont publiés, chiffrés, sur le broker MQTT avec l'adresse IP publique.

**Routeur sans UPnP.** Le serveur utilise quand même deux ports locaux, les vérifie de l'extérieur et les publie : ils marchent si le routeur a une DMZ ou une redirection manuelle vers le PC du serveur, et dans tous les cas depuis les PC du **même réseau local**. Si même ainsi le client n'entre pas, le serveur attend les ports proposés par le client (connexion inverse).

Quand les ports du serveur ne sont pas joignables depuis internet, une fenêtre d'avertissement apparaît aussi : **«Da fuori casa questo server non è raggiungibile»** (hors de la maison ce serveur n'est pas joignable), avec les numéros de port, l'adresse du PC et les trois remèdes (UPnP, DMZ, redirection manuelle). Le programme continue de fonctionner pour le réseau local et pour la connexion inverse.

**Même réseau local.** Le serveur publie aussi son adresse de réseau local (par exemple 192.168.1.20). Beaucoup de routeurs ne permettent pas à un PC de la maison de rentrer par l'adresse publique de la maison elle-même : dans ce cas, le client essaie de lui-même l'adresse locale, pendant l'appairage comme lors des connexions suivantes.

## Le client et le cycle des ports

Le client lit sur le broker MQTT l'adresse et les ports du serveur et essaie de se connecter. S'il n'y arrive pas, le **cycle des ports** commence :

1. le client ouvre avec UPnP deux ports sur **son** routeur, les vérifie de l'extérieur comme le serveur et les publie sur le broker MQTT, avec le numéro du tour et le port du serveur qu'il n'a pas atteint ;
2. le serveur lit la proposition et essaie de se connecter aux ports du client (**connexion inverse** : le serveur ouvre la connexion, mais le chiffrement et l'authentification restent les mêmes) ;
3. si cela échoue aussi, le serveur ferme ses ports, en ouvre et vérifie deux nouveaux et les republie ;
4. le client réessaie avec les nouveaux ports du serveur, et ainsi de suite.

Le cycle dure **20 tours**. Après le vingtième, le client affiche la fenêtre **«Nessuna porta è apribile»** (aucun port ne peut être ouvert) avec le bouton **Exit**, qui ferme le programme.

Chaque étape est écrite dans le fichier `sessioni.log` avec le préfixe `PORTE` (Impostazioni › Apri cartella dati, ouvrir le dossier des données), par exemple :

```
PORTE giro 3/20: il client non raggiunge il server (93.45.x.x:41234)
PORTE IP-TRX-CTRL client controllo: porta TCP 52310 aperta sul router (tentativo 1)
PORTE verifica esterna TCP 151.x.x.x:52310: raggiungibile
PORTE giro 3/20: il client propone 151.x.x.x porte 52310/38877 sul broker MQTT (consegnato)
PORTE collegamento inverso riuscito: il server si è collegato alle porte del client 151.x.x.x:52310
```

## Réseaux qui filtrent les ports sortants (depuis la 0.2.4)

Certains réseaux (entreprises, hôtels, Wi-Fi publics, certains réseaux mobiles) ne laissent **sortir** que quelques ports, en général 443 et 80. De là, le client n'atteint pas les ports aléatoires du serveur, même s'ils sont ouverts.

Au démarrage, le client teste quels ports sortent de son réseau en se connectant à **portquiz.net**, un service qui répond sur tous les ports TCP : 443, 80, 8443, 8080, 993, 587, 465, 1935 et deux ports hauts. Un port ne compte comme bon que si la page de portquiz.net arrive vraiment (un portail captif ou un proxy qui intercepte ne suffit pas).

- Si les ports hauts sortent aussi, le réseau ne filtre pas et rien ne change.
- Si le réseau filtre, le client envoie **aussitôt** au serveur, chiffrée sur le broker MQTT, la liste des ports qui passent ; il la répète à chaque tour du cycle des ports.
- Le serveur demande au routeur via UPnP **exactement ces ports comme ports externes** (par exemple 443 pour le contrôle et 80 pour l'audio), redirigés vers des ports internes libres du PC, les vérifie de l'extérieur et les publie. Si le routeur en refuse un (déjà utilisé par un autre PC ou par le routeur lui-même), il essaie le suivant ; s'il n'arrive pas à en ouvrir deux, il revient aux ports aléatoires.

Il faut **deux** ports sortants : si le réseau du client n'en laisse passer qu'un, la connexion depuis ce réseau n'est pas possible et le journal le dit. Il faut UPnP sur le routeur du serveur (ou une redirection manuelle de ces ports). Tout est écrit dans `sessioni.log` avec le préfixe `PORTE`.

## Audio en UDP

Une fois connectés, les deux PC essaient de faire passer l'audio en **UDP**, qui souffre moins des retards de TCP :

- chacun demande son adresse publique à deux serveurs **STUN** depuis la même socket UDP (ouverte sur le port audio, que UPnP a aussi ouvert en UDP) ;
- les adresses candidates sont échangées sur le canal de contrôle chiffré ;
- les deux PC «frappent» l'un vers l'autre (perçage du NAT) ; avec un NAT symétrique, on essaie aussi les ports suivants ;
- l'audio ne passe en UDP (chiffré AES-256-GCM) que lorsque **les deux** confirment qu'ils reçoivent.

Si UDP ne passe pas, ou si le client ne reçoit plus de paquets UDP pendant 4 secondes, l'audio reste (ou revient) en TCP. Le chemin utilisé apparaît dans la ligne réseau de l'autre PC : «audio UDP» ou «audio TCP».

## Que faire si la liaison ne s'établit pas

- Activez **UPnP** sur le routeur d'au moins un des deux PC (mieux sur les deux).
- Si l'opérateur utilise le **CGNAT** (IP partagée, fréquent sur réseau mobile et FWA), ce PC ne peut pas recevoir de connexions : c'est l'autre qui doit ouvrir les ports.
- Si les deux PC sont derrière un CGNAT ou sans UPnP, la connexion directe n'est pas possible.
- **Pare-feu Windows** : même avec les ports ouverts sur le routeur, le pare-feu du PC peut rejeter les connexions («nessuna risposta» même depuis le réseau local). Depuis la 0.2.7 l'installateur autorise le programme sur tous les réseaux ; avec les versions portables il y a le bouton Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (autoriser dans le pare-feu Windows).
- Pendant l'appairage, depuis la 0.2.7, le cycle des ports démarre tout seul (voir *Appairage et rendez-vous via MQTT*).


---

[Sommaire](Français) · Autres langues: [Italiano](IT-Porte-e-collegamento) · [English](EN-Ports-and-connection) · [Español](ES-Puertos-y-conexión) · [Deutsch](DE-Ports-und-Verbindung)
