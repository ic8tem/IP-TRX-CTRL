# Keyer: messaggi WAV e macro CW

Il keyer funziona **solo sul client**. Riprende il programma WavPTT, riscritto in C# dentro IP-TRX-CTRL. Gli stessi tasti F1…F8 cambiano funzione con il modo della radio.

Comandi comuni: **F1…F8** nella finestra, **Ctrl+Alt+F1…F8** anche da altri programmi, **Esc** interrompe.

## Modi fonia (SSB): messaggi WAV

- Ogni tasto riproduce un file WAV; l'audio va alla radio remota tramite il server, come se si parlasse al microfono, e il PTT viene gestito in automatico.
- I file vengono convertiti a 8 kHz al caricamento.
- Catena audio: conversione a 8 kHz → **normalizzazione** (picco a −1 dB dal fondo scala, così tutti i file partono dallo stesso livello) → compressore (off oppure da 3 a 13 dB) → filtro (nessuno, 300–2700, 500–2400, 500–2100 Hz) → boost del cursore VOLUME → limitatore.
- Boost del cursore VOLUME (come WavPTT): guadagno = 1000^(volume/100).

| Cursore | Guadagno |
|---|---|
| 0 | silenzio |
| 33 | ×10 (+20 dB) |
| 50 | ×31,6 (+30 dB) |
| 66 | ×100 (+40 dB) |
| 100 | ×1000 (+60 dB) |

- Il microfono non passa da questa catena: TX gain e «Boost oltre soglia» valgono solo per la voce.
- Più tasti premuti in sequenza vengono messi in coda; il PTT si rilascia alla fine dell'ultimo messaggio.
- Il pulsante **File WAV…** apre la finestra **Memorie WAV (F1 – F8)**, ripresa da WavPTT:
  - **…** sceglie il file per quel tasto;
  - **Play** / **Stop** ascolto di prova sull'uscita audio locale, senza PTT;
  - **Salva** copia i file nella cartella WAV accanto al programma; **Annulla** chiude senza salvare.

## Modo CW: macro di testo

- Premendo F1…F8 il client sostituisce le variabili e **inoltra al server il testo della macro come stringa**, insieme alla velocità scelta.
- Il server lo trasmette in uno dei due modi, scelto nel riquadro PTT del server alla voce **Invio CW**:
  - **CAT**: il testo va al keyer interno della radio (comando Hamlib `send_morse`, velocità con `KEYSPD`);
  - **tasto su linea DTR o RTS** di una porta seriale: il server alza il PTT sulla porta indicata, attende il ritardo PTT,
    batte il CW sulla linea scelta con i tempi Morse alla velocità ricevuta, attende 3 punti e rilascia il PTT.
    Più macro di fila restano sotto lo stesso PTT. Esc interrompe subito: tasto su e PTT rilasciato.
- Il pulsante **Macro…** apre la finestra **Macro CW (F1 – F8)**: per ogni tasto il nome che compare sul tasto e il testo da trasmettere. **Prova** fa ascoltare il testo in locale, senza trasmettere.
- Variabili utilizzabili nei testi:

| Variabile | Significato |
|---|---|
| `{MYCALL}` | il mio nominativo |
| `{CALL}` | il nominativo del corrispondente |
| `{NR}` | numero progressivo |
| `{RST}` | rapporto |

- Sotto i tasti si impostano la velocità di trasmissione, il mio nominativo, il nominativo del corrispondente e il numero.

### Porte seriali per PTT e CW (server)

| Impostazione | Effetto |
|---|---|
| PTT su RTS o DTR, porta X | il server comanda la linea della porta X |
| CW su DTR o RTS, porta Y (o «stessa porta del PTT») | il server batte il CW sulla linea della porta Y |
| PTT e CW sulla **stessa porta** | usano linee opposte: se il CW è su DTR il PTT va su RTS, e viceversa |
| porta PTT uguale alla porta del CAT | il PTT lo comanda rigctld (la porta è sua) |
| porta CW uguale alla porta del CAT | non si può battere il CW lì: il CW passa via CAT. Usa la seconda porta seriale della radio (per esempio la porta «Standard» delle Yaesu) o un adattatore USB-seriale |

Le scelte effettive e gli eventuali spostamenti di linea sono mostrati sotto le impostazioni e scritti in `sessioni.log` (righe `PTT/CW`).

## Sul server

Il server non ha tasti F né file WAV: tiene il metodo PTT verso la radio (RTS, DTR, CAT, VOX o TCI), il ritardo PTT e il modo di invio del CW (CAT o tasto su DTR/RTS).
