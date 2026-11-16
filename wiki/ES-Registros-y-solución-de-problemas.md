# Registros y solución de problemas

## Dónde están los archivos del programa

| Sistema | Carpeta de datos |
|---|---|
| Windows | `%APPDATA%\IP-TRX-CTRL` (es decir `C:\Users\<usuario>\AppData\Roaming\IP-TRX-CTRL`) |
| Linux y Raspberry Pi | `~/.config/IP-TRX-CTRL` |
| macOS | `~/.config/IP-TRX-CTRL` (carpeta oculta: en el Finder Cmd+Mayús+G y escribe la ruta) |

La carpeta se abre también desde el programa: **Impostazioni › Apri cartella dati** (ajustes › abrir carpeta de datos).

En Windows: Win+R, escribe `%APPDATA%\IP-TRX-CTRL` y pulsa Intro.

## Qué contiene

| Archivo | Contenido |
|---|---|
| `errori.log` | errores imprevistos, con fecha, hora y punto del programa donde ocurrieron. El archivo aparece solo tras el primer error |
| `sessioni.log` | registro de las conexiones: cada paso en los puertos (líneas `PORTE`: búsqueda del router UPnP, puertos abiertos o rechazados, IP pública, cierre), tipo de red (líneas `RETE`: IPv4 / IPv6), emparejamiento con el diálogo MQTT, conexiones, cambios de papel, liberaciones del PTT de seguridad. Hora en UTC |
| `settings.json` | ajustes |
| `pairing.key` | clave del emparejamiento, cifrada (Windows: con el usuario de Windows; Linux/Raspberry: ligada a máquina y usuario). En macOS la clave está en el Llavero, entrada «IP-TRX-CTRL» |
| `WAV/` | los archivos de los mensajes del keyer |

En Windows Hamlib se extrae en `%LOCALAPPDATA%\IP-TRX-CTRL\hamlib\<versión>`.

## Cómo informar de un problema

Desde la 0.3.0: **Impostazioni › Abbinamento e registri › «Rapporto per un problema…»** (informe de un problema).

1. Escribe (si quieres) qué ha pasado.
2. **Salva il rapporto…** (guardar el informe): un archivo de texto con versión, sistema, papel, radio, métodos PTT y CW, opciones de seguridad, panadaptador, broker MQTT y las últimas partes de `errori.log` (200 kB) y `sessioni.log` (400 kB).
3. **Apri la pagina delle segnalazioni** (abrir la página de avisos): se abre la página **Issues** del repositorio; adjunta el archivo.

En el informe **las direcciones IP públicas aparecen enmascaradas**: cada una se convierte en `[IP-1]`, `[IP-2]`… (la misma dirección tiene siempre el mismo nombre, así el registro sigue siendo legible). Solo quedan visibles las direcciones de red local (192.168.x.x, 10.x.x.x, 172.16–31.x.x, 100.64–127.x.x del CGNAT, 127.x.x.x, 169.254.x.x), que desde internet no llevan a nadie; las IPv6 se enmascaran todas. El informe no contiene claves ni códigos de emparejamiento. Antes de adjuntarlo puedes abrirlo y leerlo.

Sin el informe: abre la carpeta de datos y adjunta `errori.log` y `sessioni.log`, indicando sistema operativo, versión, papel y radio. `sessioni.log` contiene direcciones IP e indicativos. No adjuntes nunca los archivos de las claves (`pairing-….key`).

## Versión nueva y actualizaciones automáticas

Al arrancar y luego cada 6 horas el programa pregunta a GitHub cuál es la última release publicada (`api.github.com/repos/ic8tem/IP-TRX-CTRL/releases/latest`, sin cuenta).

Desde la **0.3.2**, si es más nueva, el programa se actualiza solo: descarga el paquete adecuado para el PC, comprueba su tamaño y su huella SHA-256 (la que GitHub publica para cada archivo) y se reinicia en la versión nueva, **solo cuando no hay una sesión en curso ni se transmite**. Con la sesión abierta la versión nueva queda lista y se instala en cuanto termina; la línea de arriba lo indica y ofrece **Aggiorna adesso** (actualizar ahora). En el registro `sessioni.log` cada paso se escribe con el prefijo `AGGIORNAMENTI`.

Se actualizan solos: Setup y zip portátil de Windows (desde la 0.3.2 el Setup deja la carpeta del programa con permiso de escritura para esto), tar.gz portátiles (Linux, Raspberry Pi, Alpine), AppImage, IP-TRX-CTRL.app en el Mac en una carpeta con permiso de escritura. **No** se actualizan solos los paquetes de sistema **.deb, .rpm, Arch y Slackware**: aparece la línea de la versión nueva con **Apri la pagina** (abrir la página), y el paquete nuevo se instala con el mismo comando de la primera vez.

Las actualizaciones automáticas se apagan en **Impostazioni › Finestra principale**: queda la línea de arriba con **Aggiorna adesso**, **Apri la pagina** y **Chiudi** (cerrar; para esa versión ya no aparece). Si la actualización falla (red, carpeta sin permiso de escritura) el programa sigue con la versión en uso y lo reintenta en el control siguiente.

## Problemas frecuentes

| Síntoma | Qué comprobar |
|---|---|
| Ventana «Io mi spengo perché non riesco a funzionare» (me apago porque no consigo funcionar) con el botón Exit (servidor) | el router no ha abierto los dos puertos con UPnP: activa UPnP en el router y reinicia. Las líneas `PORTE` de `sessioni.log` dicen qué paso ha fallado |
| El emparejamiento se detiene con «la porta … non risponde» (el puerto … no responde) | UPnP activo en el router del servidor; si el operador usa CGNAT el servidor no es alcanzable desde internet |
| «nessun broker disponibile» (ningún broker disponible) | conexión a internet del PC, puerto 1883 no bloqueado por la red |
| «Hamlib non trovato» (Hamlib no encontrado) (Linux, Raspberry, macOS) | `sudo apt install libhamlib-utils` o `brew install hamlib` |
| La radio no responde al CAT | modelo, puerto serie y velocidad en los ajustes; en Linux/Raspberry el usuario debe estar en el grupo `dialout` |
| Sin audio del micrófono en macOS | Ajustes del Sistema › Privacidad y seguridad › Micrófono: habilita IP-TRX-CTRL |
| Audio del servidor silenciado | recuadro «Schede audio del server»: quita el silencio o activa «Togli il mute da solo» |


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Registri-e-risoluzione-dei-problemi) · [English](EN-Logs-and-troubleshooting) · [Français](FR-Journaux-et-résolution-des-problèmes) · [Deutsch](DE-Protokolle-und-Fehlersuche)
