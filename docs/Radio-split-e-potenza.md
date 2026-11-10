# Radio, split e potenza

Tutti i comandi verso la radio passano dal server, che usa **Hamlib** (`rigctld`). All'avvio del collegamento il server riconosce sempre il modello di radio e quali comandi supporta.

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
- pulsanti − e +, casella del valore, cursore; la rotella del mouse sposta di un passo.

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
