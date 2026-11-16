# Third-party component licenses

IP-TRX-CTRL includes or uses the components listed below. The program has an **Informazioni e licenze** (about and licenses) window with the full texts; before each distribution, holders and texts must be checked against the packages actually used.

| Component | License | Website |
|---|---|---|
| Hamlib (library) | GNU LGPL 2.1 or later | https://hamlib.github.io |
| Hamlib `rigctld`, `rigctl` | GNU GPL 2 or later (included only in the Windows version) | https://github.com/Hamlib/Hamlib |
| Avalonia | MIT | https://github.com/AvaloniaUI/Avalonia |
| Inter (typeface) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| MQTTnet | MIT | https://github.com/dotnet/MQTTnet |
| Open.NAT | MIT | https://github.com/lontivero/Open.NAT |
| .NET Runtime, System.IO.Ports | MIT | https://github.com/dotnet/runtime |

Notes:

- `rigctld` and `rigctl` are distributed as separate, unmodified programs, started in the background and used over TCP. The sources of the included version are on the Releases page of the Hamlib repository.
- Audio and global keys use the operating system's functions directly (winmm/Core Audio, ALSA/libX11, CoreAudio/Carbon): no additional library.
- The keyer code comes from WavPTT, by the same author, and adds no constraints.
- The CW decoder is written from scratch.
- The names of the public MQTT brokers are used only as a meeting point; no data is published in clear.


---

[Contents](English) · Other languages: [Italiano](IT-Licenze) · [Español](ES-Licencias) · [Français](FR-Licences) · [Deutsch](DE-Lizenzen)
