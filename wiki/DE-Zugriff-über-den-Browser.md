# Zugriff über den Browser

Seit 0.3.1 kann der Server als kleine **HTTPS**-Website dienen: im Browser eines Telefons, Tablets oder PCs steuert man das Funkgerät, **ohne etwas zu installieren**. Das Client-Programm bleibt der vollständige Weg (Keyer, CW, Digitalbetriebsarten, umgekehrte Verbindung); die Webseite bietet das Wesentliche.

## Einschalten (auf dem Server)

**Impostazioni › Accesso dal browser (server)** (Einstellungen › Zugriff über den Browser):

- **Accesso dal browser** (Zugriff über den Browser): nach der Installation aus.
- **Porta HTTPS** (HTTPS-Port): standardmäßig 8443.
- **Apri la porta anche sul router (UPnP)** (Port auch am Router öffnen): um ohne VPN aus dem Internet hineinzukommen. Standardmäßig aus.
- **Il browser parte in solo ascolto** (der Browser startet nur mit Empfang): danach wird er in der Client-Liste freigegeben (Eintrag «Browser»).

Man meldet sich mit dem **festen Code des Servers** an (12 Stellen, derselbe wie bei der Kopplung): gibt es ihn noch nicht, erzeugt der Server ihn. Ändert man den festen Code, ändert sich auch der für den Browser.

## Die Seite «Trova il server» (Server finden)

Es ist eine einzige Datei, `trova-server.html`, die man **auf dem Telefon oder dem PC** aufbewahrt: sie liegt auf keiner Website, und die Desktop-App ist nicht nötig.

- Man speichert sie über **Impostazioni › «Salva la pagina «Trova il server»…»** (auf Server oder Client) oder im Heimnetz über `https://<Adresse des Servers>:8443/trova-server.html`.
- Man öffnet sie im Browser, gibt den festen Code ein und drückt **Trova il server**.
- Die Seite fragt die öffentlichen MQTT-Broker (broker.hivemq.com, broker.emqx.io, test.mosquitto.org, über sicheres WebSocket) nach der Nachricht, die der Server alle 10 Minuten hinterlegt. Die Nachricht ist **verschlüsselt** (AES-256-GCM) mit einem aus dem Code abgeleiteten Schlüssel (PBKDF2-SHA256, 600 000 Iterationen): sie enthält Rufzeichen, Version, Port, Adressen (VPN, Heimnetz, öffentliche IP, wenn der Port offen ist) und den **Fingerabdruck des Zertifikats**. Der Kanalname auf dem Broker wird aus dem Code abgeleitet und verrät nichts.
- **Apri la stazione (HTTPS)** (Station öffnen) probiert der Reihe nach das **VPN**, dann das **Heimnetz**, dann die **öffentliche IP**; die anderen Adressen bleiben als Links.

Der Server sitzt hinter **CGNAT**? Von außerhalb erreicht der Browser ihn nur über ein **VPN** (Tailscale, WireGuard, ZeroTier…), das auf Server und Telefon installiert ist: die Seite weist darauf hin. Der Browser kann die umgekehrte Verbindung des Client-Programms nicht herstellen.

## Beim ersten Mal: das Zertifikat

Der Server verwendet ein selbst erstelltes HTTPS-Zertifikat (ECDSA P-256, 800 Tage). Der Browser warnt einmal pro Adresse:

1. «Dies ist keine sichere Verbindung» erscheint;
2. in den Zertifikatsdetails vergleicht man den SHA-256-Fingerabdruck mit dem von «Trova il server» angezeigten (er kommt verschlüsselt an: ohne Code kann ihn niemand fälschen);
3. stimmt er überein: «Erweitert» › «Weiter»; weicht er ab, nicht hineingehen.

**iPhone**: das Zertifikat muss als Profil installiert werden: man lädt es von `https://<Adresse>:8443/certificato.cer`, dann Einstellungen › Profil geladen › Installieren, und Einstellungen › Allgemein › Info › Zertifikatsvertrauenseinstellungen.

## Die Seite der Station

- **Anmeldung**: fester Code und Rufzeichen. Der Code **geht nicht übers Netz**: der Server schickt eine Herausforderung, die Seite antwortet mit einem aus dem Code abgeleiteten Nachweis (HMAC-SHA256). Zehn Fehlversuche von derselben Adresse sperren den Zugang für 10 Minuten.
- **Telefon**: Frequenz (antippen, um sie in kHz einzugeben), − / Betriebsart / +, Bänder, S / Po / SWR / ALC, NR, NB, NOTCH, ATT, Lautstärke, eine große **PTT-Taste zum Gedrückthalten**.
- **PC**: zusätzlich VFO B, A/B, SPLIT, Schrittweite, COMP, PRE, AGC, Leistung, Wahl von Mikrofon und Lautsprecher, Jitter und verlorene Pakete, **Panadapter** (wenn der Server ihn liefert: Klick = abstimmen, Mausrad = ein Schritt). PTT auch mit der Leertaste.
- **Audio**: 8 kHz 16 Bit wie im Programm, im selben durch HTTPS verschlüsselten WebSocket. Das Mikrofon des Browsers erreicht das Funkgerät **nur**, solange Senden aus dem Browser erlaubt ist und läuft.
- Alle Sicherheiten des Servers gelten: maximale Sendezeit, kein QSY beim Senden, Vorrang für den Operator am Gerät, nur Empfang, SWR-Schutz. Die PTT wird gelöst, wenn die Seite in den Hintergrund geht oder die Verbindung abbricht.
- **Jeweils ein Operator**: ist ein Client verbunden, findet der Browser «server occupato» (Server belegt) und umgekehrt. Der Browser erscheint in der Client-Liste des Servers als «Browser».

## Datenschutz und externe Dienste

- Öffentliche MQTT-Broker: sie sehen nur einen Kanal mit unauffälligem Namen und eine verschlüsselte Nachricht (wie beim Client-Programm).
- Schriften der Seite: Inter und JetBrains Mono von Google Fonts (Google sieht die IP-Adresse dessen, der die Seite öffnet); ohne Internet verwendet die Seite die Systemschriften.
- Kein anderer Dienst.

## Quellen

- MQTT 3.1.1 (OASIS) für den minimalen Dialog mit den Brokern über WebSocket.
- Web Crypto API (W3C): PBKDF2, HKDF, AES-GCM, HMAC im Browser; dieselben Berechnungen wie im Programm (.NET `System.Security.Cryptography`).
- Sichere WebSocket-Ports der Broker: Liste öffentlicher Broker im Wiki von mqtt.org (EMQX 8084, Mosquitto 8081) und Seite des öffentlichen HiveMQ-Brokers (8884).


---

[Inhalt](Deutsch) · Andere Sprachen: [Italiano](IT-Accesso-dal-browser) · [English](EN-Browser-access) · [Español](ES-Acceso-desde-el-navegador) · [Français](FR-Accès-depuis-le-navigateur)
