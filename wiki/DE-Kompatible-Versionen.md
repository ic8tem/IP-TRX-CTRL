# Kompatible Versionen

Server und Client müssen Versionen desselben **Protokolls** haben. Das Protokoll ändert sich nur, wenn beide PCs zusammen aktualisiert werden müssen; innerhalb desselben Protokolls verstehen sich die Versionen.

| Versionen | Protokoll | Kompatibel mit |
|---|---|---|
| 0.1.0 – 0.1.3 | 1 | nur 0.1.x |
| 0.2.0 – 0.2.8 | 2 | allen 0.2.x |
| 0.3.0 – 0.3.4 | 3 | nur 0.3.x (10-stellige Zahl, fester Code des Servers, mehrere Clients am Server, Sicherheit beim Senden, Panadapter; seit 0.3.1 Zugriff über den Browser; seit 0.3.2 automatische Updates) |

Eine 0.1.x und eine 0.2.x dürfen **nicht** zusammen verwendet werden, ebenso wenig eine 0.2.x und eine 0.3.x: beide PCs aktualisieren. 0.3.0 ändert auch das Telemetrieformat und fügt neue Nachrichten hinzu, deshalb versteht sie sich nicht mit früheren Versionen. 0.3.0 ändert die Kopplung (10-stellige Zahl statt des langen Codes); bereits ausgetauschte Schlüssel bleiben gültig und wandern von selbst in die Server-Liste des Clients und die Client-Liste des Servers.

## Funktionen mit Mindestversion auf beiden PCs

| Funktion | Server und Client mindestens |
|---|---|
| Digitalprogramme, CW-Pedal/-Taste, Port-Zyklus, UDP-Audio | 0.2.0 |
| Wahl der Soundkarten des Servers vom Client aus | 0.2.1 |
| Server ohne UPnP, Verbindung im lokalen Netz | 0.2.2 |
| Versionsaustausch und automatische Kompatibilitätsprüfung | 0.2.3 |
| Der Client teilt dem Server mit, welche Ports aus seinem Netz hinausgehen (gefilterte Netze) | 0.2.4 |
| SWR-Schutz: blinkender Balken, Sendesperre und Fenster am Client | 0.2.4 |
| CAT und Audio des Servers über TCI (ExpertSDR, Thetis und andere SDR) | 0.2.5 (am Server genügt) |
| Port-Zyklus auch bei der Kopplung (20 Runden, immer neue Ports, umgekehrte Verbindung) | 0.2.7 auf beiden PCs |
| 10-stellige Kopplungszahl, Server-Liste am Client | 0.3.0 auf beiden PCs |
| Mehrere Clients am Server (jeweils einer verbunden, «Server belegt»), fester Code des Servers | 0.3.0 auf beiden PCs |
| Sicherheit beim Senden (Maximalzeit, kein QSY beim Senden, Vorrang für den Operator am Gerät, nur Empfang), Gerätebedienung, Server-Speicher, Panadapter | 0.3.0 auf beiden PCs |
| Zugriff über den Browser (HTTPS-Webseite, Seite «Trova il server») | 0.3.1 am Server; ein Client 0.3.0 funktioniert weiter |
| Automatische Updates | 0.3.2 auf jedem PC: jeder aktualisiert sich selbst (nicht mit den Paketen .deb, .rpm, Arch und Slackware) |

## Automatische Updates (seit 0.3.2)

Seit 0.3.2 aktualisiert sich jeder PC selbst auf die neueste Version, beim Start oder innerhalb von 6 Stunden, und nur wenn keine Sitzung läuft: erscheint eines Tages eine Version mit neuem Protokoll, gleichen sich Server und Client von selbst an, ohne lange auf verschiedenen Protokollen zu bleiben. Ausgenommen sind die Systempakete (.deb, .rpm, Arch, Slackware), die auf beiden PCs von Hand aktualisiert werden müssen. Details im [README](https://github.com/ic8tem/IP-TRX-CTRL#aggiornamenti-automatici).

## Automatische Prüfung (seit 0.2.3)

Die beiden PCs tauschen ihre Version zu drei Zeitpunkten aus:

- **Kopplung**: der Server schickt sie verschlüsselt über den MQTT-Broker zusammen mit Adresse und Ports; der Client vergleicht sie vor dem Verbinden. Ist das Protokoll verschieden, bricht die Kopplung ab und sagt es im Protokoll des Fensters;
- **Adresse auf dem MQTT-Broker**: der Server veröffentlicht sie bei jeder Aktualisierung zusammen mit seiner Adresse;
- **Beginn jeder Verbindung**: im Gruß auf dem verschlüsselten Kanal.

Hat der andere PC ein anderes Protokoll, erscheint das Fenster **«Versioni non compatibili»** (inkompatible Versionen); hat er eine ältere Version desselben Protokolls, sagt ein Hinweis, welche Funktionen fehlen. Versionen bis 0.2.2 schicken ihre Version nicht: sie erscheinen als «0.2.2 o precedente» (0.2.2 oder älter).

Die Version des anderen PCs steht in seiner Netzzeile im Hauptfenster; jeder Vergleich steht in `sessioni.log` mit dem Präfix `VERSIONE`. Die Version dieses PCs steht im Fenster «Informazioni e licenze» (Info und Lizenzen).


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Versioni-compatibili) · [English](EN-Compatible-versions) · [Español](ES-Versiones-compatibles) · [Français](FR-Versions-compatibles)
