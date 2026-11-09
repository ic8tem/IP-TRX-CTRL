# Keyer: messaggi WAV e macro CW

Il keyer funziona **solo sul client**. Riprende il programma WavPTT, riscritto in C# dentro IP-TRX-CTRL. Gli stessi tasti F1…F8 cambiano funzione con il modo della radio.

Comandi comuni: **F1…F8** nella finestra, **Ctrl+Alt+F1…F8** anche da altri programmi, **Esc** interrompe.

## Modi fonia (SSB): messaggi WAV

- Ogni tasto riproduce un file WAV; l'audio va alla radio remota tramite il server, come se si parlasse al microfono, e il PTT viene gestito in automatico.
- I file vengono convertiti a 8 kHz al caricamento.
- Catena audio: conversione a 8 kHz → compressore (off oppure da 3 a 13 dB) → filtro (nessuno, 300–2700, 500–2400, 500–2100 Hz) → volume → limitatore.
- Più tasti premuti in sequenza vengono messi in coda; il PTT si rilascia alla fine dell'ultimo messaggio.
- Il pulsante **File WAV…** apre la finestra **Memorie WAV (F1 – F8)**, ripresa da WavPTT:
  - **…** sceglie il file per quel tasto;
  - **Play** / **Stop** ascolto di prova sull'uscita audio locale, senza PTT;
  - **Salva** copia i file nella cartella WAV accanto al programma; **Annulla** chiude senza salvare.

## Modo CW: macro di testo

- Ogni tasto trasmette un testo in CW; la radio lo invia con il proprio keyer interno (comando Hamlib `send_morse`), alla velocità impostata.
- Il pulsante **Macro…** apre la finestra **Macro CW (F1 – F8)**: per ogni tasto il nome che compare sul tasto e il testo da trasmettere. **Prova** fa ascoltare il testo in locale, senza trasmettere.
- Variabili utilizzabili nei testi:

| Variabile | Significato |
|---|---|
| `{MYCALL}` | il mio nominativo |
| `{CALL}` | il nominativo del corrispondente |
| `{NR}` | numero progressivo |
| `{RST}` | rapporto |

- Sotto i tasti si impostano la velocità di trasmissione, il mio nominativo, il nominativo del corrispondente e il numero.

## Sul server

Il server non ha tasti F né file WAV: tiene solo il metodo PTT verso la radio (RTS, DTR, CAT o TCI) e il ritardo.
