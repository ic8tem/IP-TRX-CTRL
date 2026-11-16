# Kopplung und Treffpunkt über MQTT

Die Kopplung erfolgt für jeden Server nur einmal: danach laufen Schlüssel, Ports und Wiederverbindungen automatisch.

## So wird gekoppelt

1. Auf dem **Server** öffnet man das Fenster *Abbinamento* (Kopplung): eine 10-stellige Zahl erscheint (z. B. `482 913 0571`, seit 0.3.0; vorher war es ein langer Code wie `R4VGZ-TRGSF-…`). Alternativ kann der Client den 12-stelligen festen Code des Servers verwenden, falls er aktiviert ist (siehe unten).
2. Auf dem **Client** gibt man diese Zahl ein und drückt **Abbina** (koppeln). Sie gilt 10 Minuten.
3. Die beiden Programme tauschen einen geheimen Schlüssel aus (ECDH P-256); ab dann ist der gesamte Verkehr mit AES-256-GCM verschlüsselt.

## Die Rolle des MQTT-Brokers

Der Server hat keine feste Adresse. Damit er gefunden wird, hinterlegt er seine IP-Adresse und Ports **verschlüsselt** in einem Kanal eines öffentlichen MQTT-Brokers. Der Kanalname wird aus dem geheimen Schlüssel abgeleitet: nur wer die beiden PCs gekoppelt hat, weiß, wo er suchen muss, und kann den Inhalt lesen. Antwortet ein Broker nicht, wechselt das Programm zum nächsten.

Auch die Kopplung läuft über den Broker: der Client meldet sich in dem aus der Zahl abgeleiteten Kanal, der Server antwortet mit seiner aktuellen Adresse (verschlüsselt), dann öffnet der Client die direkte Verbindung. Seit 0.3.0 enthält die Zahl die Adresse nicht: sie kommt nur auf diesem Weg.

## Das Protokoll des Dialogs

Das Kopplungsfenster zeigt Zeile für Zeile den Dialog mit dem Broker und mit dem Server:

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

`▶` = gesendet, `◀` = empfangen, `✗` = Fehler, `✓` = abgeschlossen. **Copia registro** (Protokoll kopieren) legt den Text in die Zwischenablage, nützlich um zu sehen, wo eine fehlgeschlagene Kopplung hängen bleibt. Das Protokoll kann auch im Sitzungslog gespeichert werden.

## Typische Probleme

| Was das Protokoll zeigt | Wahrscheinliche Ursache |
|---|---|
| Kein Broker antwortet | Keine Internetverbindung oder Port 1883 vom Netz gesperrt |
| Der Broker antwortet, der Server nicht | Das Serverprogramm ist geschlossen oder wartet noch nicht auf die Kopplung |
| Der Server antwortet, aber TCP öffnet sich nicht | Port am Router nicht offen (UPnP aus oder Providernetz mit geteilter IP): seit 0.2.7 startet der Port-Zyklus |
| «nessuna risposta» (keine Antwort) im lokalen Netz mit derselben öffentlichen IP | Die Windows-Firewall des Servers verwirft die Verbindungen: auf dem Server Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (in der Windows-Firewall zulassen) |
| «connessione rifiutata» (Verbindung abgelehnt) | Das Serverprogramm ist geschlossen oder lauscht auf einem anderen Port |
| «le porte del server NON sono raggiungibili» und «CGNAT» | Der Server kann keine Verbindungen aus dem Internet empfangen: nötig ist ein offener Port am Router des Clients (UPnP, DMZ oder Weiterleitung), den der Port-Zyklus für die umgekehrte Verbindung nutzt |

## Port-Zyklus bei der Kopplung (seit 0.2.7)

Scheitert die direkte Verbindung, hört die Kopplung nicht auf: es starten bis zu **20 Runden** Portaustausch, immer über den MQTT-Broker und verschlüsselt mit dem aus der Kopplungszahl abgeleiteten Schlüssel.

1. Der Client öffnet einen **neuen** Port an seinem Router (abwechselnd einen gängigen Port, der Filter oft passiert, etwa 443, 80, 8080, 8443, oder einen zufälligen), prüft ihn aus dem Internet und bietet ihn dem Server an.
2. Der Server versucht, sich mit diesem Port zu verbinden: das ist die **umgekehrte Verbindung**, die einzig mögliche, wenn der Server hinter CGNAT sitzt.
3. Klappt das nicht, öffnet auch der Server einen **neuen** Port, prüft ihn aus dem Internet und teilt dem Client Nummer und Ergebnis mit.
4. Der Client probiert sofort den neuen Port des Servers (auch im lokalen Netz).

Kein Port wird zweimal probiert, weder am Client noch am Server. Lässt das Netz des Clients nur bestimmte Ports hinaus, probiert der Server diese zuerst. Ein erreichbarer Port des Servers wird zu seinem Steuerport. Hat am Ende nichts funktioniert, sagt die letzte Zeile des Protokolls warum (zum Beispiel: Server hinter CGNAT und Router des Clients ohne UPnP).

## 10-stellige Kopplungszahl (seit 0.3.0)

Der Server zeigt eine **10-stellige Zahl** (zum Beispiel `482 913 0571`) anstelle des alten langen Codes.

- Sie gilt **10 Minuten** und **nur einmal**; ist sie abgelaufen, drückt man am Server «Riprova» (erneut versuchen) für eine neue.
- Sie **enthält nicht die Adresse** des Servers: der Client erhält diese **verschlüsselt** (AES-256-GCM) über den MQTT-Broker. Ohne mindestens einen erreichbaren Broker ist keine Kopplung möglich.
- Der Schlüssel wird aus der Zahl mit einem langsamen **PBKDF2-SHA256** (200 000 Iterationen) abgeleitet, mit dem 10-Minuten-Fenster, in dem die Zahl entstand, als Salt. Alle 10 Milliarden Zahlen durchzuprobieren würde zu lange dauern, und eine vorberechnete Tabelle nützt nichts, weil jedes Fenster sein eigenes Salt hat. Der Client probiert das aktuelle Fenster, die zwei vorherigen und das nächste, wegen der Wartezeit und leicht abweichender Uhren (einige Sekunden Rechenzeit).
- Der Rest der Kopplung ist wie bisher: ECDH-P-256-Austausch, authentifiziert mit dem aus der Zahl abgeleiteten Schlüssel, und ein 256-Bit-Schlüssel für die Sitzungen.

## Gekoppelte Server (Client, seit 0.3.0)

Der Client führt eine **Liste von Servern**, jeder mit seinem eigenen 256-Bit-Schlüssel, aufbewahrt wie bisher (DPAPI unter Windows, Schlüsselbund unter macOS, verschlüsselte Datei unter Linux).

- Im Server-Bereich des Hauptfensters wählt man den Server: der Client ist mit **jeweils nur einem** verbunden, und die Wahl eines anderen schließt die laufende Verbindung.
- **Abbina nuovo…** (neu koppeln) fügt einen Server mit seiner Zahl hinzu; die vorherigen bleiben.
- In **Impostazioni › Abbinamento e registri** (Einstellungen › Kopplung und Protokolle) benennt man einen Server um oder entfernt ihn aus der Liste (sein Schlüssel wird gelöscht).
- Der anfängliche Name ist das Rufzeichen des Servers, sobald der Server es mitteilt.
- Eine mit einer 0.2.x gemachte Kopplung wird von selbst zum ersten Server der Liste: kein erneutes Koppeln nötig.

## Gekoppelte Clients (Server, seit 0.3.0)

Auch der Server führt eine **Liste von Clients**, jeder mit seinem Schlüssel: eine neue Kopplung entzieht den anderen **nicht** den Zugang.

- Es verbindet sich **jeweils ein Client** (es gibt ein Funkgerät). Versucht ein zweiter Client hereinzukommen, während ein anderer arbeitet, erhält er «Il server è occupato: è collegato …» (der Server ist belegt: verbunden ist …) mit dem Rufzeichen des Operators und versucht es alle 30 Sekunden selbst erneut.
- Derselbe Client, der sich neu verbindet (etwa nach einem Netzwechsel), bekommt seinen Platz ohne Wartezeit zurück.
- In **Impostazioni › Abbinamento e registri** sieht man die Liste mit Rufzeichen, Art der Kopplung (temporäre Zahl, fester Code, Rollentausch, frühere Kopplung) und letzter Nutzung. Man kann einen Client umbenennen oder ihm **den Zugang entziehen** («Togli accesso»): sein Schlüssel wird gelöscht und, falls verbunden, wird er sofort getrennt.
- **Abbina un nuovo client** (neuen Client koppeln) öffnet das Fenster mit einer temporären Zahl; bereits gekoppelte Clients bleiben.
- Eine mit einer 0.2.x gemachte Kopplung wird von selbst zum ersten Client der Liste.
- «Esporta chiavi per le autorità» (Schlüssel für die Behörden exportieren) exportiert die Schlüssel aller Clients.

## Fester Code des Servers (seit 0.3.0)

Neben der temporären Zahl kann der Server einen **festen 12-stelligen Code** haben (zum Beispiel `4829 1305 7712`), wie manche Fernwartungsprogramme. Man aktiviert ihn in **Impostazioni › Abbinamento e registri** mit «Accetta nuovi client con il codice fisso del server» (neue Clients mit dem festen Code des Servers annehmen); nach der Installation ist er **aus**.

- Der Code gilt **immer**, bis man ihn ändert («Cambia codice») oder die Option abschaltet. Wer ihn erhält, koppelt sich wann er will, auch Tage später, indem er ihn ins selbe Feld wie die Zahl eingibt; danach steht er in der Client-Liste und braucht ihn nicht mehr.
- Er funktioniert nur bei laufendem Serverprogramm und mindestens einem erreichbaren MQTT-Broker, wie die temporäre Zahl; auch hier erreicht die Adresse den Client nur verschlüsselt.
- Der Schlüssel wird aus dem Code mit PBKDF2-SHA256 mit 600 000 Iterationen abgeleitet (dreimal so viele wie bei der temporären Zahl), und der Code hat zwei Stellen mehr: alle Codes durchzuprobieren würde Jahre dauern.
- **Sicherheit:** bequem, aber weniger sicher als die temporäre Zahl, denn wer den Code herausfindet, kann sich jederzeit koppeln. Gib ihn nur vertrauenswürdigen Personen, ändere ihn, wenn er zu weit herumgekommen ist, und entferne Clients aus der Liste, die nicht mehr hinein sollen. Das Ändern des Codes entzieht bereits gekoppelten Clients nicht den Zugang.
- Die Ziffern des Codes werden wie die Schlüssel aufbewahrt (DPAPI, macOS-Schlüsselbund, verschlüsselte Datei unter Linux), nicht in der Einstellungsdatei.

## Verschlüsselte Adressen

Die IP-Adresse des Servers geht nie im Klartext durchs Netz: auf den MQTT-Brokern (Kopplung, Wiederfinden nach IP-Wechsel) ist sie mit AES-256-GCM verschlüsselt, und die Namen der MQTT-Kanäle sind aus dem Schlüssel abgeleitete Fingerabdrücke, die weder Adresse noch Rufzeichen verraten. Auch der Ersatzcode für den Fall, dass die Broker nicht antworten, enthält die Adresse verschlüsselt.


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Abbinamento-e-MQTT) · [English](EN-Pairing-and-MQTT) · [Español](ES-Emparejamiento-y-MQTT) · [Français](FR-Appairage-et-MQTT)
