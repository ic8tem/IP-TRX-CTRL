# Registri e risoluzione dei problemi

## Dove sono i file del programma

| Sistema | Cartella dei dati |
|---|---|
| Windows | `%APPDATA%\IP-TRX-CTRL` (cioè `C:\Users\<utente>\AppData\Roaming\IP-TRX-CTRL`) |
| Linux e Raspberry Pi | `~/.config/IP-TRX-CTRL` |
| macOS | `~/.config/IP-TRX-CTRL` (cartella nascosta: nel Finder Cmd+Maiusc+G e scrivi il percorso) |

La cartella si apre anche dal programma: **Impostazioni › Apri cartella dati**.

Su Windows: Win+R, scrivi `%APPDATA%\IP-TRX-CTRL` e premi Invio.

## Cosa contiene

| File | Contenuto |
|---|---|
| `errori.log` | errori imprevisti, con data, ora e punto del programma in cui sono avvenuti. Il file compare solo dopo il primo errore |
| `sessioni.log` | registro dei collegamenti: ogni passaggio sulle porte (righe `PORTE`: ricerca del router UPnP, porte aperte o rifiutate, IP pubblico, chiusura), tipo di rete (righe `RETE`: IPv4 / IPv6), abbinamento con il dialogo MQTT, connessioni, cambi di ruolo, rilasci del PTT di sicurezza. Ora in UTC |
| `settings.json` | impostazioni |
| `pairing.key` | chiave dell'abbinamento, cifrata (Windows: con l'utente di Windows; Linux/Raspberry: legata a macchina e utente). Su macOS la chiave è nel Portachiavi, voce «IP-TRX-CTRL» |
| `WAV/` | i file dei messaggi del keyer |

Su Windows Hamlib viene estratto in `%LOCALAPPDATA%\IP-TRX-CTRL\hamlib\<versione>`.

## Come segnalare un problema

Dalla 0.3.0: **Impostazioni › Abbinamento e registri › «Rapporto per un problema…»**.

1. Scrivi (se vuoi) cosa è successo.
2. **Salva il rapporto…**: un file di testo con versione, sistema, ruolo, radio, metodi PTT e CW, opzioni di sicurezza, panadapter, broker MQTT e le ultime parti di `errori.log` (200 kB) e `sessioni.log` (400 kB).
3. **Apri la pagina delle segnalazioni**: si apre la pagina **Issues** del repository; allega il file.

Nel rapporto **gli indirizzi IP pubblici sono mascherati**: ognuno diventa `[IP-1]`, `[IP-2]`… (lo stesso indirizzo ha sempre lo stesso nome, così il registro resta leggibile). Restano visibili solo gli indirizzi di rete locale (192.168.x.x, 10.x.x.x, 172.16–31.x.x, 100.64–127.x.x del CGNAT, 127.x.x.x, 169.254.x.x), che da internet non portano a nessuno; gli IPv6 sono tutti mascherati. Il rapporto non contiene chiavi né codici di abbinamento. Prima di allegarlo puoi aprirlo e leggerlo.

Senza il rapporto: apri la cartella dei dati e allega `errori.log` e `sessioni.log`, indicando sistema operativo, versione, ruolo e radio. `sessioni.log` contiene indirizzi IP e nominativi. Non allegare mai i file delle chiavi (`pairing-….key`).

## Versione nuova

Dalla 0.3.0 il programma, all'avvio, chiede a GitHub qual è l'ultima release pubblicata (`api.github.com/repos/ic8tem/IP-TRX-CTRL/releases/latest`, senza account). Se è più nuova compare una riga in alto con **Apri la pagina** (la pagina della release) e **Chiudi** (per quella versione non compare più). Ricorda di aggiornare **tutti e due i PC** (vedi *Versioni compatibili*).

Il controllo si spegne in *Impostazioni › Finestra principale*. GitHub vede l'indirizzo IP di chi chiede, come per qualunque pagina web; non viene mandato nient'altro. Il servizio è quello pubblico delle API di GitHub (limite di 60 richieste all'ora per indirizzo senza account: il programma ne fa una per avvio).

## Problemi frequenti

| Sintomo | Cosa controllare |
|---|---|
| Popup «Io mi spengo perché non riesco a funzionare» con il pulsante Exit (server) | il router non ha aperto le due porte con UPnP: attiva UPnP sul router e riavvia. Le righe `PORTE` di `sessioni.log` dicono quale passaggio è fallito |
| L'abbinamento si ferma con «la porta … non risponde» | UPnP attivo sul router del server; se l'operatore usa il CGNAT il server non è raggiungibile da internet |
| «nessun broker disponibile» | connessione internet del PC, porta 1883 non bloccata dalla rete |
| «Hamlib non trovato» (Linux, Raspberry, macOS) | `sudo apt install libhamlib-utils` oppure `brew install hamlib` |
| La radio non risponde al CAT | modello, porta seriale e velocità in Impostazioni; su Linux/Raspberry l'utente deve essere nel gruppo `dialout` |
| Nessun audio dal microfono su macOS | Impostazioni di Sistema › Privacy e sicurezza › Microfono: abilita IP-TRX-CTRL |
| Audio del server muto | riquadro «Schede audio del server»: togli il mute o attiva «Togli il mute da solo» |
