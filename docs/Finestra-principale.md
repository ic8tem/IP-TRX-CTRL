# Finestra principale

Dalla 0.3.0 la finestra è più piccola: si apre a **1080 × 760** punti (prima 1240 × 816) e si può stringere fino a 900 × 520; se il contenuto non entra compare la barra di scorrimento. È organizzata così:

1. **Intestazione** su una riga: nome del programma, stato del collegamento, LED UPnP, ruolo (CLIENT o SERVER), colori, **Impostazioni**, **Inverti ruoli**, **Help**. Sotto, quando c'è, la riga della **versione nuova** (vedi *Registri e risoluzione dei problemi*).
2. **Tre colonne**: radio · strumenti e PTT · l'altro PC.
3. **Panadapter della radio**, se attivo (vedi *Panadapter*).
4. **Schede** in basso: **Keyer**, **CW** (solo in modo CW), **Audio**, **Collegamento**.
5. Riga degli **avvisi** (server occupato, trasmissione non permessa, tempo massimo…).

Le parti nuove si nascondono da **Impostazioni › Finestra principale**: pulsanti delle bande e passo, comandi della radio, memorie, strumento ALC, panadapter, controllo della versione nuova. Così la finestra resta ancora più bassa.

## Radio

- **VFO A** in grande, con il modo. **Rotella sulle cifre**: con il mouse su una cifra la rotella la fa salire o scendere, con riporto (vale anche per il VFO B).
- Casella **Nuova QRG** (Hz), modo, **Imposta**.
- **Passo di sintonia** (da 1 Hz a 25 kHz) con i pulsanti ◀ ▶ (anche con la rotella sopra i pulsanti).
- **Bande** da 160 m a 70 cm: un clic porta all'ultima frequenza e all'ultimo modo usati su quella banda (la prima volta a una frequenza tipica). La banda in uso è evidenziata.
- **VFO B** su una riga con **A/B**, **A=B**, **SPLIT** e lo scostamento (−, valore, +). La spiegazione dello split è nel suggerimento del pulsante.
- **Potenza** TX.
- **Comandi della radio** (vedi *Radio, split e potenza*): NR, NB, NOTCH, COMP, ATT, PRE, AGC, SQL.
- **Memorie** (vedi *Radio, split e potenza*).

## Strumenti e PTT

- **S**, **Po** (potenza in uscita), **ROS**, **ALC** (in trasmissione), **Gain** (guadagno TX della radio). La barra del ROS lampeggia con la protezione ROS.
- Spia **RX/TX**.
- **PTT** «premi e tieni», anche con la barra spaziatrice. Sotto il PTT una riga dice da quanto dura la trasmissione e il **tempo massimo**, oppure perché la trasmissione **non è permessa** (solo ascolto, operatore alla radio). Quando non è permessa il pulsante è sbiadito e scrive «PTT NON PERMESSO».
- Sul client: metodo PTT e ritardo impostati sul server.

## L'altro PC

- Client: elenco dei **server abbinati** e **Abbina…**.
- Nominativo, IP, ora di collegamento, latenza, livello dell'audio ricevuto.
- Riquadri richiudibili: **Reti dei due PC** (IPv4/IPv6, percorso dell'audio, versione dell'altro PC), **Metodo PTT e CW** (solo server), **Schede audio del server**.

## Schede

- **Keyer**: F1…F8 (WAV in fonia, macro in CW). Vedi *Keyer, messaggi WAV e macro CW*.
- **CW**: compare in modo CW e si apre da sola; audio RX su una riga, pitch, larghezza, velocità, soglia, filtro della radio, cascata e decodificatore. F1…F8 funzionano anche da qui. Vedi *Modo CW*.
- **Audio**: microfono e altoparlante, volumi, boost e livelli.
- **Collegamento**: stato, latenza, jitter, pacchetti persi, buffer, broker MQTT e, sul server, il codice di riserva.
