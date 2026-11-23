# IP-TRX-CTRL

IP-TRX-CTRL sirve para usar una radio HF a distancia, por internet, como si estuviera en tu mesa. Un único programa funciona en dos PC:

- **servidor**: el PC conectado a la radio (audio, CAT mediante Hamlib/rigctld, PTT);
- **cliente**: el PC del operador, que recibe el audio de la radio, transmite con su propio micrófono y maneja la radio.

Los papeles se eligen en el primer arranque y se pueden invertir en cualquier momento.

> **Versión actual: 0.3.7.** Las imágenes son capturas reales del programa (un cliente conectado a un servidor con una radio simulada por Hamlib). La interfaz del programa está en italiano: estas páginas dan el significado de cada etiqueta.

## En resumen

- Audio bidireccional a 8 kHz, cifrado con AES-256: **Opus** entre programas 0.3.6, PCM 16 bits mono con versiones anteriores y con el navegador.
- Puertos, claves y reconexiones gestionados automáticamente; el servidor se vuelve a encontrar aunque cambie de dirección IP, mediante un broker MQTT público.
- Control CAT de la radio: frecuencia, modo, VFO A/B con split, potencia TX, lecturas de S-meter, potencia y ROE, protección ROE con bloqueo de la transmisión.
- **Keyer** integrado en el cliente (procede del programa WavPTT): en SSB ocho mensajes WAV en F1…F8, en CW ocho macros de texto transmitidas por la radio.
- En modo CW: cascada de 0–2000 Hz con la frecuencia real de las señales, decodificador en el pitch y ventanas de decodificación adicionales para las demás señales.
- Programas digitales en el cliente (WSJT-X, JTDX, fldigi…) mediante un rigctld local y cables de audio virtuales: ver [Modos digitales, pedal PTT y manipulador CW](ES-Modos-digitales-y-pedal).
- Pedal PTT y manipulador CW mecánico en un puerto serie del cliente.
- Ciclo de puertos entre servidor y cliente (UPnP comprobado desde fuera, conexión inversa, 20 vueltas) y audio por UDP cuando es posible: ver [Puertos y conexión](ES-Puertos-y-conexión).
- Desde la 0.3.0: seguridad en transmisión (tiempo máximo, sin cambios de frecuencia en transmisión, prioridad a quien está en la radio, clientes solo en escucha), mandos de la radio (NR, NB, Notch, ATT, preamplificador, AGC, compresor, squelch, ALC), bandas, paso y memorias, el [panadaptador de la radio](ES-Panadaptador), informe de problemas y aviso de versión nueva. Ver [Radio, split y potencia](ES-Radio-split-y-potencia) y [Ventana principal](ES-Ventana-principal).
- Desde la 0.3.1: [acceso desde el navegador](ES-Acceso-desde-el-navegador) de teléfonos, tabletas y PC, con la página «Trova il server» (encontrar el servidor).
- Desde la 0.3.2: actualizaciones automáticas (no para los paquetes .deb, .rpm, Arch y Slackware) y paquetes para Windows ARM, Fedora/openSUSE, Arch, Slackware, AppImage y Alpine: ver [Registros y solución de problemas](ES-Registros-y-solución-de-problemas).
- Desde la 0.3.7: reducción de ruido neuronal HamNoise, VOX con el botón junto al PTT, squelch de voz, notas entre operador y oyentes, grabación de QSO, IPv6 directo con PCP, servidor sin ventana y emulación WinKeyer: ver [Novedades de la 0.3.7](ES-Novedades-de-la-0.3.7).
- Desde la 0.3.6: audio Opus, más oyentes en el mismo servidor, manipulador CW USB y MIDI, registro de QSO con envío UDP a los programas de log y exportación ADIF, radios Icom en red sin PC al lado: ver [Novedades de la 0.3.6](ES-Novedades-de-la-0.3.6). Licencia GNU AGPL 3.0 o posterior, con el enlace al código fuente en la página web.
- Desde la 0.3.5: paquetes para Windows 7 SP1, 8 y 8.1 (`-win7-x64-`, `-win7-x86-`, con .NET 6); arriba de cada release, la tabla con el enlace a cada paquete.
- Desde la 0.3.4: puertos elegidos por ti en Ajustes (con la lista de los dos puertos que abrir); reconoce el doble NAT y el UPnP de un aparato que no es el router.
- Desde la 0.3.3: en Linux, Raspberry Pi y Mac la primera configuración, los ajustes y las licencias ya no se bloquean; etiquetas ROE y Gain visibles.
- Seis temas de color: Chiaro, Notte, Nero, Blu, Viola, Verde (claro, noche, negro, azul, violeta, verde): ver [Temas de color](ES-Temas-de-color).
- Un único código fuente para **Windows 64 bits, 32 bits y ARM, macOS, Linux (también Alpine) y Raspberry Pi**.

Antes de actualizar uno de los dos PC, mira las [Versiones compatibles](ES-Versiones-compatibles).

![Cliente conectado, tema Chiaro](https://raw.githubusercontent.com/ic8tem/IP-TRX-CTRL/video-corso/wiki-images/main.png)

## Páginas

- [Videocurso](ES-Videocurso)
- [Novedades de la 0.3.7](ES-Novedades-de-la-0.3.7)
- [Novedades de la 0.3.6](ES-Novedades-de-la-0.3.6)
- [Ventana principal](ES-Ventana-principal)
- [Emparejamiento y MQTT](ES-Emparejamiento-y-MQTT)
- [Puertos y conexión](ES-Puertos-y-conexión)
- [Radio, split y potencia](ES-Radio-split-y-potencia)
- [Audio y tarjetas de sonido del servidor](ES-Audio-y-tarjetas-de-sonido-del-servidor)
- [Keyer, mensajes WAV y macros CW](ES-Keyer-mensajes-WAV-y-macros-CW)
- [Modo CW](ES-Modo-CW)
- [Panadaptador](ES-Panadaptador)
- [Modos digitales y pedal](ES-Modos-digitales-y-pedal)
- [Conexión TCI](ES-Conexión-TCI)
- [Acceso desde el navegador](ES-Acceso-desde-el-navegador)
- [Temas de color](ES-Temas-de-color)
- [Registros y solución de problemas](ES-Registros-y-solución-de-problemas)
- [Versiones compatibles](ES-Versiones-compatibles)
- [Plataformas y compilación](ES-Plataformas-y-compilación)
- [Licencias](ES-Licencias)


---

[Índice](Español) · Otros idiomas: [Italiano](Italiano) · [English](English) · [Français](Français) · [Deutsch](Deutsch)
