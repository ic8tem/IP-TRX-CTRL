# Ports und Verbindung

Server und Client verbinden sich direkt, ohne Zwischenserver für das Audio. Damit das klappt, muss mindestens einer der beiden PCs aus dem Internet erreichbare Ports haben. IP-TRX-CTRL öffnet sie selbst per **UPnP** und prüft, ob sie wirklich funktionieren.

## UPnP beim Start und die LED

Beim Start sucht das Programm, auf Server wie Client, den Router per **UPnP** und **NAT-PMP** (drei Versuche à 10 Sekunden) und zeigt ein Fenster mit dem Ergebnis:

- **UPnP aktiv**: der Router kann die Ports selbst öffnen; das Fenster nennt das Protokoll und die vom Router gemeldete externe IP;
- **UPnP nicht gefunden**: das Fenster nennt die wahrscheinlichste Ursache und was zu tun ist:
  - Windows-Netzwerk als «Öffentlich» eingestellt (Windows blockiert die Antworten des Routers: auf «Privat» stellen);
  - VPN oder andere virtuelle Netzwerkadapter aktiv;
  - UPnP am Router ausgeschaltet: auf der Router-Seite einschalten (bei der FRITZ!Box auch für den einzelnen PC).

Oben im Hauptfenster bleibt die **LED «UPnP»** **grün blinkend**, solange der Router antwortet (Prüfung alle 60 Sekunden), **dauerhaft rot**, wenn UPnP fehlt, und **grau** während der Suche. War UPnP aktiv und antwortet der Router nicht mehr, erscheint das Fenster erneut. Ein Klick auf die LED öffnet es jederzeit.

## UPnP und NAT-PMP (seit 0.2.8)

Das Programm sucht den Router **zuerst per UPnP** (zwei Versuche à 8 Sekunden) und nur wenn UPnP nicht antwortet per **NAT-PMP**. Vor 0.2.8 galt das erste Protokoll, das antwortete: manche Router antworten auf NAT-PMP, lehnen dann aber alle Ports ab, und im Protokoll erschienen «Failed to create portmap» und «Arithmetic operation resulted in an overflow» (eine Grenze der Bibliothek Open.NAT mit NAT-PMP bei Ports über 32767).

Lehnt der Router 5 Ports hintereinander ab, probiert das Programm das andere Protokoll. Lehnt auch das ab, nutzt es einen Port des PCs: das funktioniert im lokalen Netz, mit DMZ oder manueller Weiterleitung, und bei der Kopplung geht es mit dem Port-Zyklus und der umgekehrten Verbindung weiter. Mit NAT-PMP bleiben die gewählten Ports unter 32768.

## Der Server

Beim Start:

1. sucht der Server den Router per UPnP;
2. öffnet einen zufälligen Port für die **Steuerung** (TCP) und einen für das **Audio** (TCP, und denselben Port in UDP);
3. lauscht und bittet **zwei unabhängige externe Dienste**, sich mit diesem Port zu verbinden: **ifconfig.co** und **portchecker.io** (beide Open Source). Es genügt, dass einer hineinkommt, damit der Port gut ist; kommt keiner hinein, schließt der Server den Port und probiert einen anderen (bis zu 6 Mal pro Port). Antwortet keiner der Dienste, wird der Port behalten und das Protokoll vermerkt es;
4. die **ersten zwei brauchbaren Ports** werden verschlüsselt auf dem MQTT-Broker veröffentlicht, zusammen mit der öffentlichen IP-Adresse.

**Router ohne UPnP.** Der Server verwendet trotzdem zwei lokale Ports, prüft sie von außen und veröffentlicht sie: sie funktionieren, wenn am Router eine DMZ oder eine manuelle Weiterleitung zum Server-PC eingerichtet ist, und in jedem Fall von PCs im **selben lokalen Netz**. Kommt der Client auch so nicht hinein, wartet der Server auf die vom Client vorgeschlagenen Ports (umgekehrte Verbindung).

Sind die Ports des Servers aus dem Internet nicht erreichbar, erscheint zusätzlich ein Warnfenster **«Da fuori casa questo server non è raggiungibile»** (von außerhalb ist dieser Server nicht erreichbar), mit den Portnummern, der Adresse des PCs und den drei Abhilfen (UPnP, DMZ, manuelle Weiterleitung). Das Programm funktioniert für das lokale Netz und die umgekehrte Verbindung weiter.

**Selbes lokales Netz.** Der Server veröffentlicht auch seine lokale Adresse (zum Beispiel 192.168.1.20). Viele Router lassen einen PC im Haus nicht über die öffentliche Adresse desselben Hauses wieder hinein: dann probiert der Client selbst die lokale Adresse, bei der Kopplung wie bei späteren Verbindungen.

## Der Client und der Port-Zyklus

Der Client liest auf dem MQTT-Broker Adresse und Ports des Servers und versucht sich zu verbinden. Klappt das nicht, beginnt der **Port-Zyklus**:

1. der Client öffnet per UPnP zwei Ports an **seinem** Router, prüft sie wie der Server von außen und veröffentlicht sie auf dem MQTT-Broker, mit der Rundennummer und dem Server-Port, den er nicht erreicht hat;
2. der Server liest den Vorschlag und versucht, sich mit den Ports des Clients zu verbinden (**umgekehrte Verbindung**: der Server öffnet die Verbindung, Verschlüsselung und Authentifizierung bleiben aber dieselben);
3. klappt auch das nicht, schließt der Server seine Ports, öffnet und prüft zwei neue und veröffentlicht sie erneut;
4. der Client probiert es mit den neuen Ports des Servers erneut, und so weiter.

Der Zyklus dauert **20 Runden**. Nach der zwanzigsten zeigt der Client das Fenster **«Nessuna porta è apribile»** (kein Port lässt sich öffnen) mit der Taste **Exit**, die das Programm schließt.

Jeder Schritt steht im Protokoll `sessioni.log` mit dem Präfix `PORTE` (Impostazioni › Apri cartella dati, Datenordner öffnen), zum Beispiel:

```
PORTE giro 3/20: il client non raggiunge il server (93.45.x.x:41234)
PORTE IP-TRX-CTRL client controllo: porta TCP 52310 aperta sul router (tentativo 1)
PORTE verifica esterna TCP 151.x.x.x:52310: raggiungibile
PORTE giro 3/20: il client propone 151.x.x.x porte 52310/38877 sul broker MQTT (consegnato)
PORTE collegamento inverso riuscito: il server si è collegato alle porte del client 151.x.x.x:52310
```

## Netze, die ausgehende Ports filtern (seit 0.2.4)

Manche Netze (Firmen, Hotels, öffentliches WLAN, bestimmte Mobilfunknetze) lassen nur wenige Ports **hinaus**, meist 443 und 80. Von dort erreicht der Client die zufälligen Ports des Servers nicht, auch wenn sie offen sind.

Beim Start prüft der Client, welche Ports aus seinem Netz hinausgehen, indem er sich mit **portquiz.net** verbindet, einem Dienst, der auf allen TCP-Ports antwortet: 443, 80, 8443, 8080, 993, 587, 465, 1935 und zwei hohe Ports. Ein Port gilt nur als gut, wenn die Seite von portquiz.net wirklich ankommt (ein Portal oder ein abfangender Proxy genügt nicht).

- Gehen auch hohe Ports hinaus, filtert das Netz nicht und nichts ändert sich.
- Filtert das Netz, schickt der Client dem Server **sofort**, verschlüsselt über den MQTT-Broker, die Liste der durchgelassenen Ports; er wiederholt sie in jeder Runde des Port-Zyklus.
- Der Server fordert vom Router per UPnP **genau diese Ports als externe Ports** an (zum Beispiel 443 für die Steuerung und 80 für das Audio), weitergeleitet auf freie interne Ports des PCs, prüft sie von außen und veröffentlicht sie. Lehnt der Router einen ab (schon von einem anderen PC oder vom Router selbst belegt), probiert er den nächsten; kann er keine zwei öffnen, kehrt er zu zufälligen Ports zurück.

Es werden **zwei** ausgehende Ports gebraucht: lässt das Netz des Clients nur einen durch, ist eine Verbindung aus diesem Netz nicht möglich und das Protokoll sagt es. Nötig ist UPnP am Router des Servers (oder eine manuelle Weiterleitung dieser Ports). Alles steht in `sessioni.log` mit dem Präfix `PORTE`.

## Selbst gewählte Ports und doppeltes NAT (seit 0.3.4)

**Von Hand geöffnete Ports.** Unter Einstellungen › **PORTE APERTE SUL ROUTER** (am Router geöffnete Ports) trägt man zwei Ports ein: **Controllo** (Steuerung, TCP) und **Audio** (TCP und UDP). Sie werden am Hauptrouter zum PC weitergeleitet, innen und außen mit derselben Nummer; dann **Usa queste porte** (diese Ports verwenden) drücken.

- Der **Server** nutzt sie statt der zufälligen Ports, die sich bei jedem Start ändern. Mit UPnP bittet er auch den Router, sie zu öffnen. Er prüft sie aus dem Internet und behält sie in jedem Fall, denn im lokalen Netz funktionieren sie auch.
- Der **Client** bietet sie dem Server für die umgekehrte Verbindung an, auch wenn sein Router kein UPnP hat.
- Unter den Feldern steht die **Liste der beiden zu öffnenden Ports** mit der Adresse des PCs und der Taste **Copia l'elenco** (Liste kopieren). Ohne gewählte Ports zeigt sie die automatischen Ports, die gerade benutzt werden. **Porte automatiche** (automatische Ports) hebt die Wahl auf.

**UPnP vom falschen Gerät.** In manchen Haushalten antwortet auf UPnP nicht der Router, der ins Internet führt, sondern ein anderes Gerät im Netz: ein Repeater oder Mesh-Knoten im Router-Modus, ein Receiver, ein Router hinter einem anderen Router. Man erkennt es daran, dass seine „externe IP“ im selben Netz wie der PC liegt (zum Beispiel PC 192.168.1.186, „Router“ 192.168.1.157). Dort geöffnete Ports kommen aus dem Internet nicht an. Das Programm sucht dann einen anderen UPnP-Router; findet es keinen, nutzt es das Gerät nicht und schreibt das ins Protokoll. Abhilfe: UPnP am Hauptrouter, DMZ oder die beiden gewählten Ports von Hand weiterleiten.

**Doppeltes NAT oder CGNAT.** Ist die externe IP des Routers nicht die, die das Internet sieht, probiert das Programm **nur einen Port** statt 6: Die anderen würden vom vorgeschalteten Router genauso blockiert, und zu viele Prüfungen lassen ifconfig.co mit „429“ antworten. Das Protokoll unterscheidet das **doppelte NAT** im Haus (Router mit privater IP, zum Beispiel 192.168.x.x: am vorgeschalteten Router lösen) vom **CGNAT** des Anbieters (IP 100.64–100.127: es braucht den anderen PC oder ein VPN).

**Ports, die antworten, aber nicht zum Programm gehören.** Finden die Prüfdienste den Port offen, aber beim Programm kommt keine Verbindung an, hat ein anderes Gerät geantwortet (oft die Seite des Routers auf Port 80 oder 443): Der Port gilt als geschlossen.

**STUN.** Für Audio über UDP wird der NAT-Typ mit zwei STUN-Servern verschiedener Anbieter (Google und Cloudflare) bestimmt. Zwei Server mit derselben Adresse zählen nicht als zwei Tests: Früher konnte ein symmetrisches NAT wie ein „Cone“-NAT aussehen.

## Audio über UDP

Nach dem Verbindungsaufbau versuchen die beiden PCs, das Audio über **UDP** zu führen, das unter den Verzögerungen von TCP weniger leidet:

- jeder fragt zwei **STUN**-Server vom selben UDP-Socket aus nach seiner öffentlichen Adresse (geöffnet auf dem Audioport, den UPnP auch in UDP geöffnet hat);
- die Kandidatenadressen werden über den verschlüsselten Steuerkanal ausgetauscht;
- die beiden PCs «klopfen» beieinander an (NAT-Hole-Punching); bei symmetrischem NAT werden auch die folgenden Ports probiert;
- erst wenn **beide** den Empfang bestätigen, läuft das Audio über UDP (verschlüsselt mit AES-256-GCM).

Kommt UDP nicht durch oder empfängt der Client 4 Sekunden lang keine UDP-Pakete, bleibt das Audio auf TCP (oder kehrt dorthin zurück). Der aktuelle Weg steht in der Netzzeile des anderen PCs: «audio UDP» oder «audio TCP».

## Was tun, wenn keine Verbindung zustande kommt

- **UPnP** am Router mindestens eines der beiden PCs einschalten (besser an beiden).
- Nutzt der Anbieter **CGNAT** (geteilte IP, häufig in Mobilfunk- und FWA-Netzen), kann dieser PC keine Verbindungen empfangen: der andere muss die Ports öffnen.
- Sitzen beide PCs hinter CGNAT oder ohne UPnP, ist eine direkte Verbindung nicht möglich.
- **Windows-Firewall**: auch mit offenen Ports am Router kann die Firewall des PCs Verbindungen verwerfen («nessuna risposta» sogar aus dem lokalen Netz). Seit 0.2.7 lässt der Installer das Programm in allen Netzen zu; bei den portablen Versionen gibt es die Taste Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (in der Windows-Firewall zulassen).
- Bei der Kopplung startet seit 0.2.7 der Port-Zyklus von selbst (siehe *Kopplung und Treffpunkt über MQTT*).


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Porte-e-collegamento) · [English](EN-Ports-and-connection) · [Español](ES-Puertos-y-conexión) · [Français](FR-Ports-et-liaison)
