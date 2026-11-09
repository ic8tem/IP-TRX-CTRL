# Abbinamento e ritrovo tramite MQTT

L'abbinamento si fa una sola volta: dopo, chiavi, porte e riconnessioni sono automatiche.

## Come si abbina

1. Sul **server** si apre la finestra *Abbinamento*: compare un codice (es. `R4VGZ-TRGSF-…`).
2. Sul **client** si scrive quel codice e si preme **Abbina**.
3. I due programmi si scambiano una chiave segreta (ECDH P-256) e da quel momento tutto il traffico è cifrato con AES-256-GCM.

## Il ruolo del broker MQTT

Il server non ha un indirizzo fisso. Per farsi trovare lascia il proprio indirizzo IP e le porte, **cifrati**, su un canale di un broker MQTT pubblico. Il nome del canale è ricavato dalla chiave segreta: solo chi ha abbinato i due PC sa dove guardare e sa leggere il contenuto. Se un broker non risponde, il programma passa al successivo.

Anche la fase di abbinamento passa dal broker: il client annuncia la propria presenza sul canale ricavato dal codice, il server risponde con l'indirizzo aggiornato, poi il client apre il collegamento diretto. In questo modo l'abbinamento funziona anche se l'IP del server è cambiato dopo aver letto il codice.

## Il registro del dialogo

La finestra di abbinamento mostra, riga per riga, il dialogo con il broker e con il server:

```
20:01:02.104  ▶  test.mosquitto.org:1883  CONNECT  id=aal-3f9a21
20:01:10.106  ✗  nessuna risposta in 8 s → broker successivo
20:01:10.110  ▶  broker.hivemq.com:1883  CONNECT  id=aal-3f9a21
20:01:10.296  ◀  CONNACK  accettato
20:01:10.298  ▶  SUBSCRIBE  aal/pair/7c1e…d4/srv  QoS 1
20:01:10.417  ◀  SUBACK  QoS 1
20:01:10.419  ▶  PUBLISH  aal/pair/7c1e…d4/cli  HELLO cifrato 88 B
20:01:11.020  ◀  PUBLISH  …/srv  server presente · IP 93.41.127.18
                    porte 51234 / 51235 · UPnP ok · versione 3.1
20:01:11.022  ▶  TCP 93.41.127.18:51234  apertura canale di controllo
20:01:11.140  ◀  handshake ECDH P-256 ok · chiave AES-256 derivata
20:01:11.143  ✓  Abbinamento completato
```

`▶` = inviato, `◀` = ricevuto, `✗` = errore, `✓` = completato. Con **Copia registro** il testo va negli appunti, utile per capire dove si ferma un abbinamento non riuscito. Il registro può essere salvato anche nel log di sessione.

## Problemi tipici

| Cosa si vede nel registro | Causa probabile |
|---|---|
| Nessun broker risponde | Connessione internet assente o porta 1883 bloccata dalla rete |
| Il broker risponde ma il server no | Il programma server è chiuso, oppure non è ancora in attesa di abbinamento |
| Il server risponde ma il TCP non si apre | Porta non aperta sul router (UPnP disattivato o rete del provider con IP condiviso) |
