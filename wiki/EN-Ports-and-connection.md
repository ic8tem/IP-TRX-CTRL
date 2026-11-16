# Ports and connection

Server and client connect directly, with no intermediate server for the audio. For this to work, at least one of the two PCs must have ports reachable from the internet. IP-TRX-CTRL opens them by itself with **UPnP** and checks that they really work.

## UPnP at start-up and the LED

At start-up, on both server and client, the program looks for the router with **UPnP** and **NAT-PMP** (three 10-second attempts) and shows a window with the result:

- **UPnP active**: the router can open ports by itself; the window shows the protocol and the external IP declared by the router;
- **UPnP not found**: the window gives the most likely cause and what to do:
  - Windows network set as «Public» (Windows blocks the router's replies: set it to «Private»);
  - VPN or other virtual network adapters active;
  - UPnP off on the router: turn it on from the router's page (on a FRITZ!Box also for the individual PC).

At the top of the main window the **«UPnP» LED** stays **flashing green** while the router answers (checked every 60 seconds), is **steady red** if UPnP is missing, and **grey** while searching. If UPnP was active and the router stops answering, the window appears again. A click on the LED reopens it at any time.

## UPnP and NAT-PMP (since 0.2.8)

The program looks for the router **with UPnP first** (two 8-second attempts) and only if UPnP does not answer, with **NAT-PMP**. Before 0.2.8 the first protocol to answer was used: some routers answer NAT-PMP but then refuse every port, and the log showed «Failed to create portmap» and «Arithmetic operation resulted in an overflow» (a limit of the Open.NAT library with NAT-PMP on ports above 32767).

If the router refuses 5 ports in a row, the program tries the other protocol. If that one also refuses, it uses a port of the PC: it works on the local network, with DMZ or manual forwarding, and during pairing the port cycle and the reverse connection go on. With NAT-PMP the chosen ports stay below 32768.

## The server

At start-up the server:

1. looks for the router with UPnP;
2. opens a random port for **control** (TCP) and one for **audio** (TCP, and the same port in UDP);
3. starts listening and asks **two independent external services** to connect to that port: **ifconfig.co** and **portchecker.io** (both open source). One of the two getting in is enough for the port to be good; if neither gets in, the server closes the port and tries another (up to 6 times per port). If neither service answers, the port is kept, and the log says so;
4. the **first two working ports** are published, encrypted, on the MQTT broker together with the public IP address.

**Router without UPnP.** The server still uses two local ports, checks them from outside and publishes them: they work if the router has a DMZ or a manual forward to the server PC, and in any case from PCs on the **same local network**. If the client still cannot get in, the server waits for the ports proposed by the client (reverse connection).

When the server's ports are not reachable from the internet, a warning window also appears: **«Da fuori casa questo server non è raggiungibile»** (this server cannot be reached from outside the home), with the port numbers, the PC's address and the three remedies (UPnP, DMZ, manual forwarding). The program keeps working for the local network and for the reverse connection.

**Same local network.** The server also publishes its local network address (for example 192.168.1.20). Many routers do not let a home PC come back in through the home's own public address: in that case the client tries the local address by itself, both during pairing and in later connections.

## The client and the port cycle

The client reads the server's address and ports on the MQTT broker and tries to connect. If it fails, the **port cycle** begins:

1. the client opens two ports with UPnP on **its own** router, checks them from outside like the server, and publishes them on the MQTT broker, with the round number and the server port it could not reach;
2. the server reads the proposal and tries to connect to the client's ports (**reverse connection**: the server opens the connection, but encryption and authentication stay the same);
3. if this also fails, the server closes its ports, opens and checks two new ones and publishes them again;
4. the client tries again with the server's new ports, and so on.

The cycle lasts **20 rounds**. After the twentieth the client shows the window **«Nessuna porta è apribile»** (no port can be opened) with the **Exit** button, which closes the program.

Every step is written in the `sessioni.log` file with the prefix `PORTE` (Impostazioni › Apri cartella dati, open data folder), for example:

```
PORTE giro 3/20: il client non raggiunge il server (93.45.x.x:41234)
PORTE IP-TRX-CTRL client controllo: porta TCP 52310 aperta sul router (tentativo 1)
PORTE verifica esterna TCP 151.x.x.x:52310: raggiungibile
PORTE giro 3/20: il client propone 151.x.x.x porte 52310/38877 sul broker MQTT (consegnato)
PORTE collegamento inverso riuscito: il server si è collegato alle porte del client 151.x.x.x:52310
```

## Networks that filter outgoing ports (since 0.2.4)

Some networks (companies, hotels, public Wi-Fi, some mobile networks) let only a few ports **out**, usually 443 and 80. From there the client cannot reach the server's random ports, even if they are open.

At start-up the client tests which ports get out of its network by connecting to **portquiz.net**, a service that answers on every TCP port: 443, 80, 8443, 8080, 993, 587, 465, 1935 and two high ports. A port counts as good only if the portquiz.net page really arrives (a captive portal or an intercepting proxy is not enough).

- If high ports also get out, the network does not filter and nothing changes.
- If the network filters, the client **immediately** sends the server, encrypted on the MQTT broker, the list of ports that get through; it repeats it in every round of the port cycle.
- The server asks the router via UPnP for **exactly those ports as external ports** (for example 443 for control and 80 for audio), forwarded to free internal ports of the PC, checks them from outside and publishes them. If the router refuses one (already used by another PC or by the router itself) it tries the next; if it cannot open two, it goes back to random ports.

**Two** outgoing ports are needed: if the client's network lets only one through, connecting from that network is not possible and the log says so. UPnP is needed on the server's router (or a manual forward of those ports). Everything is written in `sessioni.log` with the prefix `PORTE`.

## Audio over UDP

Once connected, the two PCs try to carry the audio over **UDP**, which suffers less from TCP's delays:

- each asks two **STUN** servers for its public address from the same UDP socket (opened on the audio port, which UPnP also opened in UDP);
- the candidate addresses are exchanged on the encrypted control channel;
- the two PCs «knock» towards each other (NAT hole punching); with a symmetric NAT the following ports are also tried;
- only when **both** confirm they are receiving does the audio go over UDP (encrypted AES-256-GCM).

If UDP does not get through, or if the client receives no UDP packets for 4 seconds, the audio stays on (or goes back to) TCP. The path in use is shown in the other PC's network line: «audio UDP» or «audio TCP».

## What to do if it does not connect

- Turn on **UPnP** on the router of at least one of the two PCs (better on both).
- If the provider uses **CGNAT** (shared IP, common on mobile and FWA networks), that PC cannot receive connections: the other one must open the ports.
- If both PCs are behind CGNAT or without UPnP, a direct connection is not possible.
- **Windows firewall**: even with ports open on the router, the PC's firewall can drop connections («nessuna risposta» even from the local network). Since 0.2.7 the installer allows the program on all networks; with the portable versions there is the button Impostazioni › Rete e audio › «Consenti nel firewall di Windows» (allow in Windows firewall).
- During pairing, since 0.2.7, the port cycle starts by itself (see *Pairing and meeting through MQTT*).


---

[Contents](English) · Other languages: [Italiano](IT-Porte-e-collegamento) · [Español](ES-Puertos-y-conexión) · [Français](FR-Ports-et-liaison) · [Deutsch](DE-Ports-und-Verbindung)
