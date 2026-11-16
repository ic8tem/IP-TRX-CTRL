# Audio and the server's sound cards

## Audio always at 8 kHz

All audio is handled at **8 kHz, 16-bit mono**:

- the operating system opens microphone and speaker in shared mode at the card's own rate; the program converts to 8 kHz as soon as the signal arrives and goes back to the card's rate only for the output;
- filters, compressor, microphone/message mixing, network and CW decoder all work at 8 kHz;
- the useful bandwidth reaches about 4 kHz, enough for SSB and for the 0–2000 Hz CW waterfall.

## Choosing the server's sound cards from the client

In the **Schede audio del server** (server's sound cards) box, the *Ingresso* (input) and *Uscita* (output) rows are two lists of the cards present on the server PC:

- **Ingresso**: the card from which the server takes the radio's audio (receive);
- **Uscita**: the card through which the server sends audio to the radio (transmit).

When you choose a card from the client, the server restarts its audio on the new card and saves the choice. If the card does not open (unplugged, busy, permission denied), the server immediately goes back to the previous cards and the client sees a warning; an `AUDIO` line remains in the server's `sessioni.log`. The server's list of cards refreshes by itself every 30 seconds, for example after plugging in a USB interface.

The same list also works on the server: changing the card there is the same as changing it in the local audio box.

With CAT over TCI (since 0.2.5) the lists also show **«TCI · audio della radio»** (TCI · radio audio): chosen as input and output, audio goes through the TCI of the radio's program, with no sound cards or virtual cables (see *TCI connection*).

## Mute of the server's sound cards

The client sees whether the server's two sound cards connected to the radio (receive and transmit) are muted by the operating system, together with their system volume, and can:

- unmute them remotely;
- adjust the system volume;
- turn on **Togli il mute da solo** (unmute automatically): the server unmutes as soon as someone mutes, and tells the client.

How it is done on each system:

| System | Interface |
|---|---|
| Windows | Core Audio |
| macOS | CoreAudio |
| Linux / Raspberry Pi | PipeWire or PulseAudio; ALSA if they are missing |

Limits:

- the server program must run in the user's session, not as a service;
- if the system denies microphone access for privacy, the program reports it but does not change it: unblock it by hand on the server once;
- a mute inside the radio's USB driver is not visible to the operating system.


---

[Contents](English) · Other languages: [Italiano](IT-Audio-e-schede-audio-del-server) · [Español](ES-Audio-y-tarjetas-de-sonido-del-servidor) · [Français](FR-Audio-et-cartes-son-du-serveur) · [Deutsch](DE-Audio-und-Soundkarten-des-Servers)
