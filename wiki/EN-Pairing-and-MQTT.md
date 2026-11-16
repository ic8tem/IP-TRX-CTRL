# Pairing and meeting through MQTT

Pairing is done once for each server: after that, keys, ports and reconnections are automatic.

## How to pair

1. On the **server** open the *Abbinamento* (pairing) window: a 10-digit number appears (e.g. `482 913 0571`, since 0.3.0; before that it was a long code such as `R4VGZ-TRGSF-…`). Alternatively the client can use the server's 12-digit fixed code, if it is enabled (see below).
2. On the **client** type that number and press **Abbina** (pair). It is valid for 10 minutes.
3. The two programs exchange a secret key (ECDH P-256) and from then on all traffic is encrypted with AES-256-GCM.

## The role of the MQTT broker

The server has no fixed address. To be found, it leaves its IP address and ports, **encrypted**, on a channel of a public MQTT broker. The channel name is derived from the secret key: only whoever paired the two PCs knows where to look and can read the content. If a broker does not answer, the program moves on to the next one.

Pairing also goes through the broker: the client announces itself on the channel derived from the number, the server replies with its current address (encrypted), then the client opens the direct connection. Since 0.3.0 the number does not contain the address: it only arrives this way.

## The dialogue log

The pairing window shows, line by line, the dialogue with the broker and with the server:

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

`▶` = sent, `◀` = received, `✗` = error, `✓` = completed. **Copia registro** (copy log) puts the text on the clipboard, useful to see where a failed pairing stops. The log can also be saved in the session log.

## Typical problems

| What the log shows | Probable cause |
|---|---|
| No broker answers | No internet connection, or port 1883 blocked by the network |
| The broker answers but the server does not | The server program is closed, or not yet waiting for pairing |
| The server answers but TCP does not open | Port not open on the router (UPnP off, or a provider network with a shared IP): since 0.2.7 the port cycle starts |
| «nessuna risposta» (no answer) on the local network with the same public IP | The server's Windows firewall drops the connections: on the server, Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (allow in Windows firewall) |
| «connessione rifiutata» (connection refused) | The server program is closed or listens on another port |
| «le porte del server NON sono raggiungibili» and «CGNAT» | The server cannot receive connections from the internet: an open port is needed on the client's router (UPnP, DMZ or forwarding), which the port cycle uses for the reverse connection |

## Port cycle during pairing (since 0.2.7)

If the direct connection fails, pairing does not stop: up to **20 rounds** of port exchange start, always through the MQTT broker and encrypted with the key derived from the pairing number.

1. The client opens a **new** port on its router (in alternate rounds a common port that often gets through filters, such as 443, 80, 8080, 8443, or a random one), checks it from the internet and offers it to the server.
2. The server tries to connect to that port: this is the **reverse connection**, the only one possible when the server is behind CGNAT.
3. If that fails, the server also opens a **new** port, checks it from the internet and tells the client its number and the result.
4. The client immediately tries the server's new port (on the local network too).

No port is tried twice, neither on the client nor on the server. If the client's network only lets some ports out, the server tries those first. A server port found reachable becomes its control port. In the end, if nothing worked, the last line of the log says why (for example: server behind CGNAT and client router without UPnP).

## 10-digit pairing number (since 0.3.0)

The server shows a **10-digit number** (for example `482 913 0571`) instead of the old long code.

- It is valid for **10 minutes** and **once only**; when it expires, press «Riprova» (retry) on the server for a new one.
- It **does not contain the server's address**: the client receives it **encrypted** (AES-256-GCM) from the MQTT broker. Without at least one reachable broker, pairing is not possible.
- The key is derived from the number with a slow **PBKDF2-SHA256** (200,000 iterations), using as salt the 10-minute window in which the number was born. Trying all 10 billion numbers would take too long, and a precomputed table is useless because each window has its own salt. The client tries the current window, the two previous ones and the next one, to allow for waiting and slightly different clocks (a few seconds of computation).
- The rest of the pairing is as before: an ECDH P-256 exchange authenticated with the key derived from the number, and a 256-bit key for the sessions.

## Paired servers (client, since 0.3.0)

The client keeps a **list of servers**, each with its own 256-bit key stored as before (DPAPI on Windows, Keychain on macOS, an encrypted file on Linux).

- In the server panel of the main window you choose the server: the client is connected to **only one at a time**, and choosing another closes the current connection.
- **Abbina nuovo…** (pair new) adds a server with its number; the previous ones stay.
- In **Impostazioni › Abbinamento e registri** (settings › pairing and logs) you rename a server or remove it from the list (its key is deleted).
- The initial name is the server's callsign, as soon as the server sends it.
- A pairing made with a 0.2.x becomes the first server in the list by itself: no need to pair again.

## Paired clients (server, since 0.3.0)

The server also keeps a **list of clients**, each with its own key: a new pairing does **not** remove access from the others.

- **One client at a time** can connect (there is one radio). If a second client tries to enter while another is operating, it receives «Il server è occupato: è collegato …» (the server is busy: … is connected) with the callsign of whoever is operating, and retries by itself every 30 seconds.
- The same client reconnecting (for example after a network change) takes its place back without waiting.
- In **Impostazioni › Abbinamento e registri** you see the list, with the callsign, how it was paired (temporary number, fixed code, role swap, earlier pairing) and its last use. You can rename a client or **revoke its access** («Togli accesso»): its key is deleted and, if it is connected, it is disconnected immediately.
- **Abbina un nuovo client** (pair a new client) opens the window with a temporary number; clients already paired stay.
- A pairing made with a 0.2.x becomes the first client in the list by itself.
- «Esporta chiavi per le autorità» (export keys for the authorities) exports the keys of all clients.

## The server's fixed code (since 0.3.0)

Besides the temporary number, the server can have a **12-digit fixed code** (for example `4829 1305 7712`), as some remote-control programs do. Enable it in **Impostazioni › Abbinamento e registri** with «Accetta nuovi client con il codice fisso del server» (accept new clients with the server's fixed code); it is **off** after installation.

- The code is **always** valid, until you change it («Cambia codice») or switch the option off. Whoever receives it can pair whenever they like, even days later, by typing it in the same box as the number; they then join the list of clients and no longer need it.
- It works only with the server program running and at least one MQTT broker reachable, like the temporary number; here too the address reaches the client only encrypted.
- The key is derived from the code with 600,000 iterations of PBKDF2-SHA256 (three times the temporary number) and the code has two more digits: trying all codes would take years of computation.
- **Security:** it is convenient but less safe than the temporary number, because whoever finds out the code can pair at any time. Give it only to people you trust, change it if it has spread too far, and remove from the list the clients that should no longer get in. Changing the code does not remove access from clients already paired.
- The code's digits are stored like the keys (DPAPI, macOS Keychain, an encrypted file on Linux), not in the settings file.

## Encrypted addresses

The server's IP address never travels in clear: on the MQTT brokers (pairing, finding the server after an IP change) it is encrypted with AES-256-GCM, and the MQTT channel names are fingerprints derived from the key, which reveal neither the address nor the callsign. The backup code for when the brokers do not answer also contains the address encrypted.


---

[Contents](English) · Other languages: [Italiano](IT-Abbinamento-e-MQTT) · [Español](ES-Emparejamiento-y-MQTT) · [Français](FR-Appairage-et-MQTT) · [Deutsch](DE-Kopplung-und-MQTT)
