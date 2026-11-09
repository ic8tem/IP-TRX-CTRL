# Prove UDP e hole punching (laboratorio locale)

Simulatore di NAT in user space (`nat.py`), prototipo del protocollo (`peer.py`), scenari (`run.py`).
Avvio: `python3 run.py` (aggiungi `-v` per il dettaglio). Serve `cryptography`.

| Scenario | NAT server | NAT client | Risultato |
|---|---|---|---|
| 1 | cono, porta ristretta | cono, porta ristretta | collegati in 0,05 s, audio 100 %, controllo 50/50 |
| 2 | come 1, 5 % perdita, 20–80 ms ritardo | | collegati in 0,2 s, audio 85 % (nessuna ritrasmissione), controllo 50/50 con 20 ritrasmissioni |
| 3 | cono pieno | simmetrico | collegati (il server impara l'indirizzo vero del client dal PUNCH) |
| 4 | cono ristretto (indirizzo) | simmetrico | collegati |
| 5 | cono, porta ristretta | simmetrico | **non collegati** |
| 5b | come 5 + previsione della porta | | collegati |
| 6 | simmetrico | simmetrico | **non collegati** |
| 6b | come 6 + previsione della porta | | **non collegati** |
| 6c | simmetrico + porta UDP aperta con UPnP | simmetrico | collegati |
| 7 | mappature che scadono in 20 s, 25 s di silenzio | | collegati: i keepalive ogni 15 s tengono aperto il NAT |

Conclusioni:
- con i router di casa (scenari 1–4) la perforazione riesce sempre, senza aprire porte;
- con un NAT simmetrico serve la previsione della porta (5b) o una porta UDP aperta con UPnP sul server (6c);
- con due NAT simmetrici senza UPnP serve un relè;
- i messaggi di controllo arrivano tutti anche con perdite; l'audio perso non viene ritrasmesso (lo copre il jitter buffer).

Limite: il NAT simmetrico simulato assegna porte con passo quasi regolare; nei router veri può essere casuale
e allora la previsione non funziona.
