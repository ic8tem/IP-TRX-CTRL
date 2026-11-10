# Versioni compatibili

Server e client devono avere versioni dello stesso **protocollo**. Il protocollo cambia solo quando i due PC vanno aggiornati insieme; dentro lo stesso protocollo le versioni si parlano.

| Versioni | Protocollo | Compatibili con |
|---|---|---|
| 0.1.0 – 0.1.3 | 1 | solo le 0.1.x |
| 0.2.0 – 0.2.6 | 2 | tutte le 0.2.x |

Una 0.1.x e una 0.2.x **non** vanno usate insieme: aggiorna tutti e due i PC.

## Funzioni che richiedono una versione minima su entrambi i PC

| Funzione | Server e client almeno |
|---|---|
| Programmi digitali, pedale/tasto CW, ciclo delle porte, audio UDP | 0.2.0 |
| Scelta delle schede audio del server dal client | 0.2.1 |
| Server senza UPnP, collegamento sulla rete locale | 0.2.2 |
| Scambio della versione e controllo automatico di compatibilità | 0.2.3 |
| Il client comunica al server le porte che escono dalla sua rete (reti filtrate) | 0.2.4 |
| Protezione ROS: barra lampeggiante, blocco della trasmissione e finestra sul client | 0.2.4 |
| CAT e audio del server via TCI (ExpertSDR, Thetis e altri SDR) | 0.2.5 (basta sul server) |

## Controllo automatico (dalla 0.2.3)

I due PC si scambiano la versione in tre momenti:

- **abbinamento**: il server la manda, cifrata, sul broker MQTT insieme a indirizzo e porte; il client la confronta prima di collegarsi. Se il protocollo è diverso l'abbinamento si ferma e lo dice nel registro della finestra;
- **indirizzo sul broker MQTT**: il server la pubblica insieme al suo indirizzo, a ogni aggiornamento;
- **inizio di ogni collegamento**: nel saluto sul canale cifrato.

Se l'altro PC ha un protocollo diverso compare la finestra **«Versioni non compatibili»**; se ha una versione più vecchia dello stesso protocollo compare un avviso che dice quali funzioni mancano. Le versioni fino alla 0.2.2 non mandano la loro versione: compaiono come «0.2.2 o precedente».

La versione dell'altro PC si vede nella riga della sua rete nella finestra principale; ogni confronto è scritto in `sessioni.log` con il prefisso `VERSIONE`. La versione di questo PC è nella finestra «Informazioni e licenze».
