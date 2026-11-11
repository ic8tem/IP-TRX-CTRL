# Radio, split e potenza

Tutti i comandi verso la radio passano dal server, che usa **Hamlib** (`rigctld`) oppure, dalla 0.2.5, il **TCI** dei programmi SDR (vedi *Collegamento TCI*). All'avvio del collegamento il server riconosce sempre il modello di radio e quali comandi supporta.

## VFO A, VFO B e split

| Comando | Effetto |
|---|---|
| **A/B** | scambia i due VFO |
| **A=B** | copia il VFO A nel VFO B |
| **SPLIT** | ricezione su A, trasmissione su B |

Lo split si regola come **scostamento** del VFO B rispetto al VFO A:

- campo: da **−50 kHz** a **+50 kHz**;
- passo **1 kHz** nei modi fonia (SSB);
- passo **0,1 kHz** in CW;
- pulsanti − e +, casella del valore; la rotella del mouse sposta di un passo (dalla 0.3.0 non c'è più il cursore, per una finestra più piccola).

## Potenza TX

- Cursore da **1 a 100 W**.
- Se la radio, o Hamlib per quel modello, non supporta la conversione in watt, il cursore passa in automatico a **1–100 %**.
- Il comando (livello `RFPOWER`) parte al rilascio del cursore; dopo ogni modifica il valore viene riletto dalla radio e mostrato accanto, insieme al modello e all'unità usata (es. "IC-7300 · watt").

## Protezione ROS

Dalla 0.2.4 il server controlla il **ROS** (SWR) letto dalla radio mentre trasmette. La protezione è **attiva dall'installazione**.

| ROS | Cosa succede |
|---|---|
| da **1,9** | avviso: la barra del SWR lampeggia in rosso, sul server e sul client |
| da **2,5** per due letture di fila (circa un secondo) | blocco: il server toglie il PTT, ferma messaggi WAV, testo e tasto CW e l'audio verso la radio (anche col PTT a VOX); sul client compare una finestra |

La trasmissione resta bloccata finché l'operatore non preme **«Sblocca la trasmissione»** nella finestra. Se l'ha chiusa con «Lascia bloccata», un clic sulla barra del SWR la riapre. Dopo lo sblocco il PTT va premuto di nuovo: anche se il pedale o il pulsante erano rimasti premuti, la radio non torna in trasmissione da sola.

Prima di sbloccare: controllare antenna, cavo e connettori, accordatore, e che la frequenza sia nella banda dell'antenna.

**Impostazioni › Radio (server)**:

- casella **Protezione ROS** per attivarla o disattivarla (alla disattivazione il programma chiede conferma);
- **Avviso da ROS** (predefinito 1,9) e **Blocco da ROS** (predefinito 2,5); il blocco va da 1,2 a 10, l'avviso resta sotto il blocco.

Le impostazioni valgono sul server, che misura il ROS. Dal client si possono cambiare quando è collegato a un server 0.2.4 o successivo: vengono mandate al server al salvataggio. Ogni blocco, sblocco e cambio di impostazione è scritto in `sessioni.log` con il prefisso `ROS`.

La protezione funziona solo se la radio fornisce il ROS a Hamlib (livello `SWR`); se non lo fornisce, la barra resta su «—» e `sessioni.log` lo segnala una volta. Non sostituisce le protezioni interne della radio e dell'amplificatore.

## Sicurezza in trasmissione (dalla 0.3.0)

Decide il server, per tutti i client. **Impostazioni › Sicurezza in trasmissione (server)**:

| Opzione | Predefinita | Cosa fa |
|---|---|---|
| **Tempo massimo di una trasmissione** | attiva, 180 s | conta le trasmissioni comandate dal programma (PTT, messaggi WAV, tasto CW su linea seriale, programmi digitali). 15 secondi prima della fine la riga sotto il PTT diventa rossa; allo scadere il PTT si toglie e **resta tolto finché l'operatore non lo rilascia**. Da 30 a 600 s |
| **Niente cambi di frequenza in trasmissione** | attiva | mentre la radio trasmette il server rifiuta i cambi di frequenza, modo, filtro, VFO B, split e A/B; sul client compare l'avviso |
| **Precedenza a chi è alla radio** | attiva, 3 minuti | se la radio trasmette senza che il programma l'abbia comandata (microfono o PTT della radio), oppure se si preme il PTT del programma sul server, il client non può trasmettere per i minuti scelti (da 1 a 30). Il blocco si allunga finché la trasmissione locale continua; sul server un clic sulla riga sotto il PTT lo toglie prima |
| **Codice fisso in solo ascolto** | attiva | chi si abbina con il codice fisso del server entra in **solo ascolto**; lo abilita il server |

**Solo ascolto**: in **Impostazioni › Abbinamento e registri** il server sceglie per ogni client **«Può trasmettere»** oppure no. Un client in solo ascolto sente la radio, cambia frequenza e usa il panadapter, ma non trasmette (PTT, messaggi, CW, programmi digitali), non sblocca la protezione ROS, non cambia le memorie del server e non chiede lo scambio dei ruoli.

Note:

- La precedenza funziona con le radio che dicono via CAT quando sono in trasmissione (comando Hamlib `t`). Il CW mandato al keyer della radio conta come trasmissione del programma per la durata stimata del testo.
- Con il PTT a VOX il tempo massimo toglie il PTT del programma, ma è la radio a decidere quando smettere di trasmettere.
- Il tempo massimo non tocca le trasmissioni fatte dalla radio stessa (operatore alla radio).
- Ogni evento è scritto in `sessioni.log` (righe `TEMPO MASSIMO`, `QSY rifiutato`, `PRECEDENZA`, `TRASMISSIONE remota rifiutata`).

## Comandi della radio (dalla 0.3.0)

Riga di pulsanti sotto la potenza (si nasconde da *Impostazioni › Finestra principale*). Sono i comandi standard di Hamlib, quindi funzionano con le radio per cui Hamlib li supporta:

| Pulsante | Comando Hamlib | Funzione |
|---|---|---|
| **NR** | funzione `NR` | riduzione del rumore |
| **NB** | funzione `NB` | noise blanker |
| **NOTCH** | funzione `ANF` | notch automatico |
| **COMP** | funzione `COMP` | compressore del microfono della radio |
| **ATT** | livello `ATT` | attenuatore: ogni clic passa al valore successivo che la radio accetta, poi spento |
| **PRE** | livello `PREAMP` | preamplificatore, come l'attenuatore |
| **AGC** | livello `AGC` | veloce, medio, lento |
| **SQL** | livello `SQL` | squelch, 0–100 % |

Un pulsante grigio vuol dire che la radio (o Hamlib per quel modello) non fornisce quel comando. Il server legge questi valori ogni 4 secondi e subito dopo ogni comando. Con il **CAT via TCI** funzionano NR, NB, NOTCH e SQL (comandi TCI `rx_nr_enable`, `rx_nb_enable`, `rx_anf_enable`, `sql_enable` e `sql_level`).

## Bande, passo e memorie (dalla 0.3.0)

- **Bande** da 160 m a 70 cm: un clic porta all'ultima frequenza e all'ultimo modo usati su quella banda, la prima volta a una frequenza tipica (es. 14,200 MHz USB). Le ultime frequenze sono salvate in questo PC.
- **Passo di sintonia** da 1 Hz a 25 kHz con i pulsanti ◀ ▶ e la rotella sopra i pulsanti; vale anche per la rotella sul panadapter.
- **Memorie**: «Salva…» chiede il nome e dove salvarla: **in questo PC** (solo per te) o **sul server**, comune a tutti i client. Nell'elenco le memorie sono marcate «PC» o «Server»; **Vai** (o doppio clic) le richiama, **Elimina** le toglie. Sul server le memorie del server si gestiscono direttamente. Solo un client che può trasmettere cambia le memorie del server.

Fonte dei nomi dei comandi Hamlib: manuale di `rigctl`/`rigctld` (funzioni `NR`, `NB`, `ANF`, `COMP`; livelli `ATT`, `PREAMP`, `AGC`, `SQL`, `ALC`), https://hamlib.sourceforge.net/html/rigctl.1.html
