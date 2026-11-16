# Audio y tarjetas de sonido del servidor

## Audio siempre a 8 kHz

Todo el audio se trata a **8 kHz, 16 bits mono**:

- el sistema operativo abre micrófono y altavoz en modo compartido a la frecuencia de la tarjeta; el programa convierte a 8 kHz en cuanto llega la señal y vuelve a la frecuencia de la tarjeta solo para la salida;
- filtros, compresor, mezcla micrófono/mensajes, red y decodificador CW trabajan a 8 kHz;
- la banda útil llega a unos 4 kHz, suficiente para SSB y para la cascada CW de 0–2000 Hz.

## Elegir las tarjetas de sonido del servidor desde el cliente

En el recuadro **Schede audio del server** (tarjetas de sonido del servidor), las filas *Ingresso* (entrada) y *Uscita* (salida) son dos listas con las tarjetas presentes en el PC servidor:

- **Ingresso**: la tarjeta de la que el servidor toma el audio de la radio (recepción);
- **Uscita**: la tarjeta con la que el servidor envía el audio a la radio (transmisión).

Al elegir una tarjeta desde el cliente, el servidor reinicia su audio en la tarjeta nueva y guarda la elección. Si la tarjeta no se abre (desconectada, ocupada, permiso denegado), el servidor vuelve enseguida a las tarjetas anteriores y el cliente ve un aviso; en el registro `sessioni.log` del servidor queda una línea `AUDIO`. La lista de tarjetas del servidor se actualiza sola cada 30 segundos, por ejemplo tras conectar una interfaz USB.

La misma lista funciona también en el servidor: cambiar la tarjeta allí equivale a cambiarla en el recuadro del audio local.

Con el CAT por TCI (desde la 0.2.5) en las listas aparece también **«TCI · audio della radio»** (TCI · audio de la radio): elegida como entrada y salida, el audio pasa por el TCI del programa de la radio, sin tarjetas ni cables virtuales (ver *Conexión TCI*).

## Silencio (mute) de las tarjetas de sonido del servidor

El cliente ve si las dos tarjetas de sonido del servidor conectadas a la radio (recepción y transmisión) están silenciadas por el sistema operativo, junto con su volumen de sistema, y puede:

- quitar el silencio a distancia;
- ajustar el volumen de sistema;
- activar **Togli il mute da solo** (quitar el silencio automáticamente): el servidor lo desactiva en cuanto alguien lo activa y lo avisa al cliente.

Cómo se hace en cada sistema:

| Sistema | Interfaz |
|---|---|
| Windows | Core Audio |
| macOS | CoreAudio |
| Linux / Raspberry Pi | PipeWire o PulseAudio; ALSA si faltan |

Límites:

- el programa servidor debe ejecutarse en la sesión del usuario, no como servicio;
- si el sistema deniega el acceso al micrófono por privacidad, el programa lo indica pero no lo cambia: hay que desbloquearlo a mano en el servidor una vez;
- un silencio interno del driver USB de la radio no es visible para el sistema operativo.


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Audio-e-schede-audio-del-server) · [English](EN-Audio-and-server-sound-cards) · [Français](FR-Audio-et-cartes-son-du-serveur) · [Deutsch](DE-Audio-und-Soundkarten-des-Servers)
