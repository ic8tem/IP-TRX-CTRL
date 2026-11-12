# Accesso dal browser

Dalla 0.3.1 il server può fare da piccolo sito **HTTPS**: dal browser di un telefono, di un tablet o di un PC si comanda la radio **senza installare niente**. Il programma client resta la via completa (keyer, CW, programmi digitali, collegamento inverso); la pagina web porta l'essenziale.

## Attivazione (sul server)

**Impostazioni › Accesso dal browser (server)**:

- **Accesso dal browser**: spento all'installazione.
- **Porta HTTPS**: 8443 di serie.
- **Apri la porta anche sul router (UPnP)**: per entrare da internet senza VPN. Spento di serie.
- **Il browser parte in solo ascolto**: poi si abilita nell'elenco dei client (voce «Browser»).

Si entra con il **codice fisso del server** (12 cifre, lo stesso dell'abbinamento): se non c'è ancora, il server lo crea. Cambiando il codice fisso cambia anche quello per il browser.

## La pagina «Trova il server»

È un solo file, `trova-server.html`, da tenere **sul telefono o sul PC**: non è su nessun sito e non serve l'app del computer.

- Si salva da **Impostazioni › «Salva la pagina «Trova il server»…»** (sul server o sul client), oppure, sulla rete di casa, da `https://<indirizzo del server>:8443/trova-server.html`.
- Si apre con il browser, si scrive il codice fisso e si preme **Trova il server**.
- La pagina chiede ai broker MQTT pubblici (broker.hivemq.com, broker.emqx.io, test.mosquitto.org, su WebSocket sicuro) il messaggio che il server lascia ogni 10 minuti. Il messaggio è **cifrato** (AES-256-GCM) con una chiave ricavata dal codice (PBKDF2-SHA256, 600 000 passaggi): contiene nominativo, versione, porta, indirizzi (VPN, rete di casa, IP pubblico se la porta è aperta) e l'**impronta del certificato**. Il nome del canale sul broker è ricavato dal codice e non dice niente.
- **Apri la stazione (HTTPS)** prova nell'ordine la **VPN**, poi la **rete di casa**, poi l'**IP pubblico**; gli altri indirizzi restano come link.

Il server è dietro **CGNAT**? Da fuori casa il browser lo raggiunge solo con una **VPN** (Tailscale, WireGuard, ZeroTier…) installata sul server e sul telefono: la pagina lo dice. Il browser non sa fare il collegamento inverso del programma client.

## Prima volta: il certificato

Il server usa un certificato HTTPS fatto da lui (ECDSA P-256, 800 giorni). Il browser avvisa una volta per indirizzo:

1. compare «La connessione non è privata»;
2. nei dettagli del certificato si confronta l'impronta SHA-256 con quella mostrata da «Trova il server» (arriva cifrata: chi non ha il codice non la può falsificare);
3. se è uguale: «Avanzate» › «Procedi»; se è diversa, non entrare.

**iPhone**: il certificato va installato come profilo: lo si scarica da `https://<indirizzo>:8443/certificato.cer`, poi Impostazioni › Profilo scaricato › Installa, e Impostazioni › Generali › Info › Attendibilità certificati.

## La pagina della stazione

- **Accesso**: codice fisso e nominativo. Il codice **non passa in rete**: il server manda una sfida, la pagina risponde con una prova (HMAC-SHA256) ricavata dal codice. Dieci tentativi sbagliati dallo stesso indirizzo bloccano l'accesso per 10 minuti.
- **Telefono**: frequenza (tocco per scriverla in kHz), − / modo / +, bande, S / Po / ROS / ALC, NR, NB, NOTCH, ATT, volume, grande **PTT premi e tieni**.
- **PC**: in più VFO B, A/B, SPLIT, passo, COMP, PRE, AGC, potenza, scelta di microfono e altoparlante, jitter e pacchetti persi, **panadapter** (se il server lo fornisce: clic = sintonia, rotella = un passo). PTT anche con la barra spaziatrice.
- **Audio**: 8 kHz 16 bit come il programma, nello stesso WebSocket cifrato dall'HTTPS. Il microfono del browser arriva alla radio **solo** mentre la trasmissione del browser è permessa e in corso.
- Valgono tutte le sicurezze del server: tempo massimo, niente QSY in trasmissione, precedenza a chi è alla radio, solo ascolto, protezione ROS. Il PTT si toglie se la pagina va in secondo piano o il collegamento cade.
- **Un operatore per volta**: se è collegato un client, il browser trova «server occupato», e viceversa. Il browser compare nell'elenco dei client del server come «Browser».

## Privacy e servizi esterni

- Broker MQTT pubblici: vedono solo un canale dal nome casuale e un messaggio cifrato (come per il programma client).
- Caratteri della pagina: Inter e JetBrains Mono da Google Fonts (Google vede l'indirizzo IP di chi apre la pagina); senza internet la pagina usa i caratteri del sistema.
- Nessun altro servizio.

## Fonti

- MQTT 3.1.1 (OASIS) per il dialogo minimo con i broker su WebSocket.
- Web Crypto API (W3C): PBKDF2, HKDF, AES-GCM, HMAC nel browser; stessi calcoli del programma (.NET `System.Security.Cryptography`).
- Porte WebSocket sicure dei broker: elenco dei broker pubblici del wiki di mqtt.org (EMQX 8084, Mosquitto 8081) e pagina del broker pubblico HiveMQ (8884).
