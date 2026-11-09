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
| `sessioni.log` | registro dei collegamenti: abbinamento (compreso il dialogo con i broker MQTT), connessioni, cambi di ruolo, rilasci del PTT di sicurezza. Ora in UTC |
| `settings.json` | impostazioni |
| `pairing.key` | chiave dell'abbinamento, cifrata (Windows: con l'utente di Windows; Linux/Raspberry: legata a macchina e utente). Su macOS la chiave è nel Portachiavi, voce «IP-TRX-CTRL» |
| `WAV/` | i file dei messaggi del keyer |

Su Windows Hamlib viene estratto in `%LOCALAPPDATA%\IP-TRX-CTRL\hamlib\<versione>`.

## Come segnalare un problema

1. Apri la cartella dei dati.
2. Allega `errori.log` e, per i problemi di collegamento o abbinamento, `sessioni.log`.
3. Indica sistema operativo, versione del programma (finestra *Informazioni e licenze*), ruolo del PC (server o client) e modello della radio.

Le segnalazioni si aprono nella scheda **Issues** del repository.

`sessioni.log` contiene indirizzi IP e nominativi: se lo pubblichi, togli quello che non vuoi rendere visibile. Non allegare mai `pairing.key`.

## Problemi frequenti

| Sintomo | Cosa controllare |
|---|---|
| L'abbinamento si ferma con «la porta … non risponde» | UPnP attivo sul router del server; se l'operatore usa il CGNAT il server non è raggiungibile da internet |
| «nessun broker disponibile» | connessione internet del PC, porta 1883 non bloccata dalla rete |
| «Hamlib non trovato» (Linux, Raspberry, macOS) | `sudo apt install libhamlib-utils` oppure `brew install hamlib` |
| La radio non risponde al CAT | modello, porta seriale e velocità in Impostazioni; su Linux/Raspberry l'utente deve essere nel gruppo `dialout` |
| Nessun audio dal microfono su macOS | Impostazioni di Sistema › Privacy e sicurezza › Microfono: abilita IP-TRX-CTRL |
| Audio del server muto | riquadro «Schede audio del server»: togli il mute o attiva «Togli il mute da solo» |
