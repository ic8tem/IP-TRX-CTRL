# Conexión TCI

Desde la 0.2.5 el servidor puede manejar la radio con **TCI** (Transceiver Control Interface) en lugar de Hamlib. TCI es el protocolo de Expert Electronics (ExpertSDR2, ExpertSDR3), usado también por Thetis y otros programas SDR: el programa de la radio hace de servidor WebSocket, normalmente en el mismo PC, puerto **40001**.

## Configuración

**Impostazioni › Radio (server) › Collegamento CAT** (ajustes › radio › conexión CAT) → **TCI (ExpertSDR, Thetis e altri SDR)**:

- **Indirizzo** (dirección) del programma de la radio (de serie `127.0.0.1`) y **porta TCI** (puerto TCI, de serie `40001`);
- **Audio della radio via TCI** (audio de la radio por TCI): el audio de recepción y transmisión pasa por TCI, sin tarjetas de sonido ni cables virtuales en el servidor.

En el programa de la radio hay que activar el servidor TCI (ExpertSDR: Options › TCI; Thetis: Setup › Network › TCI).

## Qué pasa por TCI

| Función | Comandos TCI |
|---|---|
| frecuencia VFO A y VFO B | `vfo` |
| modo (USB, LSB, CW, CW-R, AM, FM, DIGU, DIGL…) | `modulation` |
| ancho del filtro (filtro ancho en CW) | `rx_filter_band` |
| split | `split_enable` |
| potencia TX en % | `drive` |
| PTT («CAT» o «TCI») | `trx` (con audio TCI: `trx:0,true,tci`) |
| S-meter | `rx_smeter` / `rx_channel_sensors` |
| potencia de salida y **ROE** (también para la protección ROE) | `tx_sensors` |
| texto CW y velocidad | `cw_macros`, `cw_macros_stop`, `cw_macros_speed` |
| audio | flujos binarios: recepción, transmisión bajo petición (`TX_CHRONO`), a 8 kHz mono cuando el programa lo acepta, si no convertido |

El servidor crea un pequeño «rigctld» interno que traduce los comandos Hamlib a TCI: por eso el resto del programa funciona como con Hamlib, incluidos los **programas digitales** del cliente (WSJT-X, JTDX, fldigi…). Para los comandos que no afectan a la radio (`\dump_state`, `\chk_vfo`) el servidor inicia un `rigctld` de prueba (modelo 1, «Dummy»); si Hamlib no está instalado, TCI funciona igual, pero los programas digitales podrían no conectarse.

## Límites

- El **pitch CW** no llega por TCI: hay que ajustarlo igual en el programa y en la radio.
- La potencia se ajusta en **porcentaje** (TCI no da la potencia máxima en vatios).
- La ROE y la potencia de salida solo llegan si el programa de la radio envía `tx_sensors` (ExpertSDR con TCI 1.9 o posterior); sin ellos, la protección ROE no puede actuar.
- Con audio TCI las tarjetas del servidor son ambas «TCI · audio della radio»: no se puede tener una dirección por TCI y la otra por una tarjeta.
- En el registro `sessioni.log`, las líneas `TCI` indican cuándo la conexión se abre, está lista o se cae.

## Fuentes

No existe una especificación pública única consultable desde aquí: el programa sigue tres fuentes, comparadas entre sí. Donde no coinciden, lee formato, canales y frecuencia de la cabecera de cada bloque de audio en lugar de suponerlos.

| Fuente | Qué confirma | Dónde coincide | Dónde difiere |
|---|---|---|---|
| [Hamlib TCI Audio Sidechannel Protocol](https://raw.githubusercontent.com/jfrancis42/hamlib-tci-sidecar/HEAD/PROTOCOL.md) | cabecera de audio de 64 bytes (16 enteros de 32 bits: receptor, frecuencia, formato, códec, crc, longitud, tipo, canales), formatos 0–3, «longitud» en muestras por canal, respuesta a `TX_CHRONO` con exactamente esas muestras | tipos de flujo 1–3 iguales a JTDX | es el protocolo de un programa auxiliar para Hamlib modelado sobre TCI, no TCI en sí; añade un tipo 4 (estado del PTT) que TCI no tiene |
| [JTDX, interfaz TCI](https://deepwiki.com/jtdx-project/jtdx/4.1-tci-transceiver-interface) | tipos de flujo 0–3 (IQ, audio recibido, audio a transmitir, `TX_CHRONO`), comandos `vfo`, `modulation`, `trx`, `split_enable`, `drive`, `audio_start`, `audio_samplerate` | tipos de flujo iguales a la primera fuente; comandos iguales a la tercera | resumen generado del código de JTDX, sin el orden de los campos de la cabecera; indica audio típico a 48 kHz estéreo float32 |
| [PE5JW, panorámica del protocolo TCI](https://github.com/pe5jw/tci-protocol-overview) | sintaxis de los comandos de audio (`audio_stream_sample_type`, `audio_stream_channels`), `trx` con fuente `tci`, sensores `rx_smeter`, `tx_sensors`, alias usados por Thetis y otros | comandos y modos iguales a JTDX; ROE como 5.º campo de `tx_sensors` | describe una cabecera distinta (avisa de que varía entre programas); para `tx_sensors` indica potencia directa y reflejada donde otras descripciones indican potencia eficaz y de pico |

Consecuencias en el programa: de la línea `tx_sensors` usa solo el 3.er campo (potencia de salida) y el 5.º (ROE), en los que las fuentes coinciden; formato, canales y frecuencia del audio se leen de cada bloque.


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Collegamento-TCI) · [English](EN-TCI-connection) · [Français](FR-Connexion-TCI) · [Deutsch](DE-TCI-Verbindung)
