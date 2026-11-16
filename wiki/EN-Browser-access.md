# Browser access

Since 0.3.1 the server can act as a small **HTTPS** website: from the browser of a phone, tablet or PC you control the radio **without installing anything**. The client program remains the complete way (keyer, CW, digital modes, reverse connection); the web page carries the essentials.

## Enabling it (on the server)

**Impostazioni › Accesso dal browser (server)** (settings › browser access):

- **Accesso dal browser** (browser access): off after installation.
- **Porta HTTPS** (HTTPS port): 8443 by default.
- **Apri la porta anche sul router (UPnP)** (also open the port on the router): to get in from the internet without a VPN. Off by default.
- **Il browser parte in solo ascolto** (the browser starts as receive only): it is then enabled in the list of clients (entry «Browser»).

You log in with the **server's fixed code** (12 digits, the same as for pairing): if there is none yet, the server creates it. Changing the fixed code also changes the one for the browser.

## The «Trova il server» (find the server) page

It is a single file, `trova-server.html`, to keep **on your phone or PC**: it is not on any website and the desktop app is not needed.

- Save it from **Impostazioni › «Salva la pagina «Trova il server»…»** (on the server or the client), or, on the home network, from `https://<server address>:8443/trova-server.html`.
- Open it in the browser, type the fixed code and press **Trova il server**.
- The page asks the public MQTT brokers (broker.hivemq.com, broker.emqx.io, test.mosquitto.org, over secure WebSocket) for the message the server leaves every 10 minutes. The message is **encrypted** (AES-256-GCM) with a key derived from the code (PBKDF2-SHA256, 600,000 iterations): it contains callsign, version, port, addresses (VPN, home network, public IP if the port is open) and the **certificate fingerprint**. The channel name on the broker is derived from the code and reveals nothing.
- **Apri la stazione (HTTPS)** (open the station) tries the **VPN** first, then the **home network**, then the **public IP**; the other addresses remain as links.

Is the server behind **CGNAT**? From outside the home the browser reaches it only through a **VPN** (Tailscale, WireGuard, ZeroTier…) installed on the server and on the phone: the page says so. The browser cannot make the reverse connection that the client program makes.

## First time: the certificate

The server uses an HTTPS certificate it made itself (ECDSA P-256, 800 days). The browser warns once per address:

1. «Your connection is not private» appears;
2. in the certificate details, compare the SHA-256 fingerprint with the one shown by «Trova il server» (it arrives encrypted: without the code nobody can forge it);
3. if it matches: «Advanced» › «Proceed»; if it differs, do not go in.

**iPhone**: the certificate must be installed as a profile: download it from `https://<address>:8443/certificato.cer`, then Settings › Profile Downloaded › Install, and Settings › General › About › Certificate Trust Settings.

## The station page

- **Login**: fixed code and callsign. The code **never travels on the network**: the server sends a challenge and the page answers with a proof (HMAC-SHA256) derived from the code. Ten wrong attempts from the same address block access for 10 minutes.
- **Phone**: frequency (tap to type it in kHz), − / mode / +, bands, S / Po / SWR / ALC, NR, NB, NOTCH, ATT, volume, a big **press-and-hold PTT**.
- **PC**: in addition VFO B, A/B, SPLIT, step, COMP, PRE, AGC, power, choice of microphone and speaker, jitter and lost packets, **panadapter** (if the server provides it: click = tune, wheel = one step). PTT with the space bar too.
- **Audio**: 8 kHz 16-bit like the program, in the same WebSocket encrypted by HTTPS. The browser's microphone reaches the radio **only** while transmitting from the browser is allowed and in progress.
- All the server's safety rules apply: maximum TX time, no QSY while transmitting, priority to whoever is at the radio, receive only, SWR protection. PTT is released if the page goes to the background or the link drops.
- **One operator at a time**: if a client is connected, the browser finds «server occupato» (server busy), and vice versa. The browser appears in the server's list of clients as «Browser».

## Privacy and external services

- Public MQTT brokers: they only see a channel with a random-looking name and an encrypted message (as for the client program).
- Page fonts: Inter and JetBrains Mono from Google Fonts (Google sees the IP address of whoever opens the page); without internet the page uses the system fonts.
- No other service.

## Sources

- MQTT 3.1.1 (OASIS) for the minimal dialogue with the brokers over WebSocket.
- Web Crypto API (W3C): PBKDF2, HKDF, AES-GCM, HMAC in the browser; the same computations as the program (.NET `System.Security.Cryptography`).
- Brokers' secure WebSocket ports: list of public brokers on the mqtt.org wiki (EMQX 8084, Mosquitto 8081) and the HiveMQ public broker page (8884).


---

[Contents](English) · Other languages: [Italiano](IT-Accesso-dal-browser) · [Español](ES-Acceso-desde-el-navegador) · [Français](FR-Accès-depuis-le-navigateur) · [Deutsch](DE-Zugriff-über-den-Browser)
