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
