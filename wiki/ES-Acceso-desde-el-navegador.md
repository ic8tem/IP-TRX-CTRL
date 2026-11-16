# Acceso desde el navegador

Desde la 0.3.1 el servidor puede hacer de pequeño sitio **HTTPS**: desde el navegador de un teléfono, una tableta o un PC se maneja la radio **sin instalar nada**. El programa cliente sigue siendo la vía completa (keyer, CW, modos digitales, conexión inversa); la página web lleva lo esencial.

## Activación (en el servidor)

**Impostazioni › Accesso dal browser (server)** (ajustes › acceso desde el navegador):

- **Accesso dal browser** (acceso desde el navegador): apagado tras la instalación.
- **Porta HTTPS** (puerto HTTPS): 8443 de serie.
- **Apri la porta anche sul router (UPnP)** (abrir el puerto también en el router): para entrar desde internet sin VPN. Apagado de serie.
- **Il browser parte in solo ascolto** (el navegador empieza solo en escucha): luego se habilita en la lista de clientes (entrada «Browser»).

Se entra con el **código fijo del servidor** (12 cifras, el mismo del emparejamiento): si aún no existe, el servidor lo crea. Al cambiar el código fijo cambia también el del navegador.

## La página «Trova il server» (encontrar el servidor)

Es un solo archivo, `trova-server.html`, para guardar **en el teléfono o en el PC**: no está en ningún sitio web y no hace falta la aplicación de escritorio.

- Se guarda desde **Impostazioni › «Salva la pagina «Trova il server»…»** (en el servidor o en el cliente) o, en la red de casa, desde `https://<dirección del servidor>:8443/trova-server.html`.
- Se abre con el navegador, se escribe el código fijo y se pulsa **Trova il server**.
- La página pide a los brokers MQTT públicos (broker.hivemq.com, broker.emqx.io, test.mosquitto.org, por WebSocket seguro) el mensaje que el servidor deja cada 10 minutos. El mensaje va **cifrado** (AES-256-GCM) con una clave obtenida del código (PBKDF2-SHA256, 600 000 iteraciones): contiene indicativo, versión, puerto, direcciones (VPN, red de casa, IP pública si el puerto está abierto) y la **huella del certificado**. El nombre del canal en el broker se obtiene del código y no revela nada.
- **Apri la stazione (HTTPS)** (abrir la estación) prueba en este orden la **VPN**, luego la **red de casa**, luego la **IP pública**; las demás direcciones quedan como enlaces.

¿El servidor está detrás de **CGNAT**? Desde fuera de casa el navegador solo lo alcanza con una **VPN** (Tailscale, WireGuard, ZeroTier…) instalada en el servidor y en el teléfono: la página lo indica. El navegador no sabe hacer la conexión inversa del programa cliente.

## La primera vez: el certificado

El servidor usa un certificado HTTPS hecho por él mismo (ECDSA P-256, 800 días). El navegador avisa una vez por dirección:

1. aparece «La conexión no es privada»;
2. en los detalles del certificado se compara la huella SHA-256 con la que muestra «Trova il server» (llega cifrada: sin el código nadie puede falsificarla);
3. si coincide: «Configuración avanzada» › «Acceder»; si es distinta, no entres.

**iPhone**: el certificado debe instalarse como perfil: se descarga desde `https://<dirección>:8443/certificato.cer`, luego Ajustes › Perfil descargado › Instalar, y Ajustes › General › Información › Ajustes de confianza de certificados.

## La página de la estación

- **Acceso**: código fijo e indicativo. El código **no viaja por la red**: el servidor manda un desafío y la página responde con una prueba (HMAC-SHA256) obtenida del código. Diez intentos fallidos desde la misma dirección bloquean el acceso durante 10 minutos.
- **Teléfono**: frecuencia (tocar para escribirla en kHz), − / modo / +, bandas, S / Po / ROE / ALC, NR, NB, NOTCH, ATT, volumen, un gran **PTT de pulsar y mantener**.
- **PC**: además VFO B, A/B, SPLIT, paso, COMP, PRE, AGC, potencia, elección de micrófono y altavoz, jitter y paquetes perdidos, **panadaptador** (si el servidor lo ofrece: clic = sintonía, rueda = un paso). PTT también con la barra espaciadora.
- **Audio**: 8 kHz 16 bits como el programa, en el mismo WebSocket cifrado por HTTPS. El micrófono del navegador llega a la radio **solo** mientras la transmisión desde el navegador está permitida y en curso.
- Valen todas las seguridades del servidor: tiempo máximo, sin QSY en transmisión, prioridad para quien está en la radio, solo escucha, protección ROE. El PTT se suelta si la página pasa a segundo plano o se cae la conexión.
- **Un operador a la vez**: si hay un cliente conectado, el navegador encuentra «server occupato» (servidor ocupado), y viceversa. El navegador aparece en la lista de clientes del servidor como «Browser».

## Privacidad y servicios externos

- Brokers MQTT públicos: solo ven un canal con nombre aparentemente aleatorio y un mensaje cifrado (como con el programa cliente).
- Tipos de letra de la página: Inter y JetBrains Mono desde Google Fonts (Google ve la dirección IP de quien abre la página); sin internet la página usa las fuentes del sistema.
- Ningún otro servicio.

## Fuentes

- MQTT 3.1.1 (OASIS) para el diálogo mínimo con los brokers por WebSocket.
- Web Crypto API (W3C): PBKDF2, HKDF, AES-GCM, HMAC en el navegador; los mismos cálculos que el programa (.NET `System.Security.Cryptography`).
- Puertos WebSocket seguros de los brokers: lista de brokers públicos del wiki de mqtt.org (EMQX 8084, Mosquitto 8081) y página del broker público de HiveMQ (8884).


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Accesso-dal-browser) · [English](EN-Browser-access) · [Français](FR-Accès-depuis-le-navigateur) · [Deutsch](DE-Zugriff-über-den-Browser)
