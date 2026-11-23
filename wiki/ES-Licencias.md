# Licencias de los componentes de terceros

IP-TRX-CTRL se distribuye bajo la **GNU Affero General Public License 3.0 o posterior** (desde la 0.3.6; hasta la 0.3.5, GPL 3.0 o posterior): quien lo modifique y deje usar su versión a otros a través de la red debe ofrecerles también el código fuente.

IP-TRX-CTRL incluye o usa los componentes indicados abajo. El programa tiene una ventana **Informazioni e licenze** (información y licencias) con los textos completos; antes de cada distribución hay que verificar titulares y textos en los paquetes realmente usados.

| Componente | Licencia | Sitio |
|---|---|---|
| Hamlib (biblioteca) | GNU LGPL 2.1 o posterior | https://hamlib.github.io |
| Hamlib `rigctld`, `rigctl` | GNU GPL 2 o posterior (incluidos solo en la versión Windows) | https://github.com/Hamlib/Hamlib |
| Avalonia | MIT | https://github.com/AvaloniaUI/Avalonia |
| Inter (tipo de letra) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| MQTTnet | MIT | https://github.com/dotnet/MQTTnet |
| Open.NAT | MIT | https://github.com/lontivero/Open.NAT |
| Concentus (códec Opus, desde la 0.3.6) | BSD-3-Clause | https://github.com/lostromb/concentus |
| wfview (protocolo Icom en red, reescrito en C#, desde la 0.3.6) | GNU GPL 3.0 | https://gitlab.com/eliggett/wfview |
| .NET Runtime, System.IO.Ports | MIT | https://github.com/dotnet/runtime |

Notas:

- `rigctld` y `rigctl` se distribuyen como programas separados, sin modificar, iniciados en segundo plano y usados por TCP. Las fuentes de la versión incluida están en la página Releases del repositorio de Hamlib.
- El audio y las teclas globales usan directamente las funciones del sistema operativo (winmm/Core Audio, ALSA/libX11, CoreAudio/Carbon): ninguna biblioteca adicional.
- El código del keyer procede de WavPTT, del mismo autor, y no añade restricciones.
- El decodificador CW está escrito desde cero.
- Los nombres de los brokers MQTT públicos se usan solo como punto de encuentro; no se publica ningún dato en claro.


---

[Índice](Español) · Otros idiomas: [Italiano](IT-Licenze) · [English](EN-Licenses) · [Français](FR-Licences) · [Deutsch](DE-Lizenzen)
