# FootTracker

Proceso independiente que lee la cámara, clasifica dos marcadores de color y envía
**solo el estado de los dos pedales** a `TaxiVR.exe` por UDP local. Unity no procesa
vídeo: este programa es el único que abre la cámara.

- **Rojo o rosado** = freno (`PIVOT_PEDAL_FRENO`)
- **Verde o azul** = acelerador (`PIVOT_PEDAL_ACELERADOR`)

El estado inicial de ambos es `down` (pies apoyados) y el juego **no interpreta eso como
pisar los pedales**: los pedales están *desarmados* hasta que un pie se levanta por
primera vez. Un marcador perdido o deformado es `unknown`, que nunca equivale a `up` ni a
`down`.

## Marcadores

Imprime dos cuadrados planos, uno rojo o rosado (freno) y otro verde o azul (acelerador),
de al menos 6 cm de lado, y pégalos sobre la zapatilla mirando a la cámara. La cámara debe
ver los dos cuadrados completos y sin obstrucciones. Un cuadrado muy deformado, muy
alargado o girado en exceso se rechaza como `unknown`.

El detector trabaja con **margen**: los rangos de color de fábrica son amplios (el freno
admite rojos y rosados, el acelerador del verde al azul) y la saturación mínima es baja
para tolerar luz y marcadores apagados, como los papeles pastel de las fotos de ejemplo.
Además de mirar el color, cada pedal **recuerda dónde estaba su marcador en el frame
anterior** y se queda con el candidato más cercano, de modo que un taburete azul o una
estantería del fondo no lo despistan. Cuando el marcador se pierde, el pedal pasa a
`unknown` en vez de saltar a un objeto parecido.

## Instalación

```powershell
cd FootTracker
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e ".[dev]"
```

Para el equipo de demostración no hace falta Python instalado: ver *Empaquetado*.

## Uso

```powershell
.\.venv\Scripts\python.exe -m foottracker --camera 0 --preview
```

Al arrancar, **deja los pies apoyados durante ~0,75 s** para que el sistema calibre la
altura del suelo (salvo que cargues un perfil de calibración, ver *Calibración con fotos*).
La ventana de diagnóstico muestra `calibrando` y luego `listo`.

| Opción | Descripción |
| --- | --- |
| `--camera` | Índice de webcam (`0`, `1`, …), URL del celular (`http://192.168.0.5:8080/video`) o `auto` (la primera conectada) |
| `--host`, `--port` | Destino UDP. Por defecto `127.0.0.1:5055` |
| `--rate` | Paquetes por segundo. Por defecto `30` |
| `--width`, `--height`, `--fps` | Resolución y frecuencia solicitadas a la cámara |
| `--camera-timeout` | Segundos máximos para abrir una cámara de red. Por defecto `5` |
| `--frame-stale` | Antigüedad máxima (s) del frame para considerarlo imagen. Por defecto `0.3` |
| `--reconnect-delay` | Espera inicial (s) entre reintentos de cámara. Por defecto `0.5` |
| `--list-cameras` | Sondea y lista las webcams conectadas, y sale |
| `--probe` | Mide la cámara y sale, sin enviar paquetes |
| `--capture-calibration` | Directorio de captura de fotos con el teclado (escribe `DIR/calibration.json`) |
| `--capture-photos` | Directorio donde guardar fotos numeradas (`foto_0001.png`, …) |
| `--photos` | Analiza una carpeta de fotos y sale, sin cámara |
| `--photos-out` | Guarda copias anotadas del análisis de `--photos` |
| `--calibration` | Perfil JSON de calibración a cargar en ejecución |
| `--marker-cm` | Lado real del marcador en cm. Por defecto `6` |
| `--threshold-mode` | `pixels` (por defecto) o `relative` (proporcional al tamaño del marcador) |
| `--lift-threshold` | Píxeles de levantamiento en modo `pixels` |
| `--relative-lift` | Fracción del lado del marcador en modo `relative` |
| `--debounce` | Frames estables antes de aceptar un cambio. Por defecto `3` |
| `--calibrate` | Segundos de calibración en vivo cuando no hay perfil cargado |
| `--preview` | Ventana con las máscaras, el estado y la altura detectada |
| `--json` | Imprime cada paquete enviado |
| `--json-status` | Imprime cada paquete con la altura medida de cada pie |
| `--duration` | Segundos a ejecutar; `0` = sin límite |

El celular puede presentarse a Windows como **webcam USB**, que es lo recomendado porque no
añade latencia. Por Wi-Fi también funciona: ver *Cámara por Wi-Fi*.

## Cámara por Wi-Fi

Instala en el celular una app que publique la cámara en la red local (DroidCam, IP Webcam,
CamDroid, Iriun…) y conéctalo al **mismo Wi-Fi** que el PC. La app muestra su URL; pásala tal
cual a `--camera`:

```powershell
.\.venv\Scripts\python.exe -m foottracker --camera http://192.168.0.5:4747/video --preview
```

URLs habituales (cambia la IP por la que muestre tu app):

| App | URL |
| --- | --- |
| DroidCam | `http://IP:4747/video` |
| IP Webcam, CamDroid | `http://IP:8080/video` |
| Iriun | `http://IP:8000/video` |
| RTSP genérico | `rtsp://IP:8554/live` |

Si no sabes qué URL usar, `tools/probe_camera.py` sondea las habituales y sugiere la que
funcione:

```powershell
.\.venv\Scripts\python.exe -m tools.probe_camera --ip 192.168.0.5
```

Para comprobar una URL concreta sin arrancar el juego, `--probe` informa la resolución y los
FPS reales del stream (y usa `--duration` como tiempo de medida):

```powershell
.\.venv\Scripts\python.exe -m foottracker --camera http://192.168.0.5:4747/video --probe
```

Baja la resolución del celular a **640 × 480 a 30 fps**: el Wi-Fi añade latencia y el
clasificador no necesita más.

A diferencia del USB, una cámara Wi-Fi puede atascarse o caerse. FootTracker lo tolera: lee la
cámara en un hilo aparte y clasifica **siempre el frame más reciente**, de modo que sigue
enviando a 30 Hz aunque la red hipo. Si el stream se corta, reconecta solo —esperas de 0,5 s
que van doblando hasta 5 s— y lo avisa por consola. Mientras no hay imagen, ambos pedales van
como `unknown`, que en el juego es neutral.

Si la latencia molesta, pídele a FFMPEG que no acumule buffering antes de arrancar:

```powershell
$env:OPENCV_FFMPEG_CAPTURE_OPTIONS = "fflags;nobuffer|flags;low_delay"
```

## Tomar fotos

Para solo capturar imágenes, sin calibrar nada, apunta a una carpeta:

```powershell
.\.venv\Scripts\python.exe -m foottracker --camera auto --capture-photos fotos
```

La ventana muestra el número de la siguiente foto:

| Tecla | Acción |
| --- | --- |
| **Espacio** | Guarda una foto numerada (`fotos/foto_0001.png`, …) |
| **Esc** / **Q** | Sale |

La numeración continúa donde quedó la última vez, así que repetir el comando no pisa fotos
anteriores. El contador ignora los archivos que no sigan el patrón `foto_NNNN.png`.

## Analizar una carpeta de fotos

Para revisar una sesión de capturas sin cámara, pásale la carpeta:

```powershell
.\.venv\Scripts\python.exe -m foottracker --photos fotos
```

Recorre las imágenes en orden, sigue cada marcador foto a foto y escribe una línea por
imagen:

```
foto_0014.png   rojo down  0.6cm   verde up  6.9cm (-6.3)
```

- `rojo` es el freno; `verde` cubre también el azul del acelerador.
- `down` = pisado, `up` = levantado, `sin datos` = el marcador no se ve.
- La cifra en cm es la altura del pie sobre el suelo; entre paréntesis, cuánto subió o bajó
  respecto a la foto anterior (`+` baja/pisa, `-` sube/suelta).

La **línea del suelo y la escala se aprenden de la propia sesión**: el marcador apoyado es
el que baja más en la imagen, así que se toma un percentil alto de las posiciones como suelo
y el tamaño del marcador en esa banda como escala (con `--marker-cm` para el lado real). No
hace falta calibrar antes.

Con `--photos-out DIR` se guarda una copia anotada de cada foto (círculo sobre el marcador
seguido y el estado escrito) para revisar el resultado a ojo:

```powershell
.\.venv\Scripts\python.exe -m foottracker --photos fotos --photos-out fotos-revisadas --debounce 1
```

## Calibración con fotos

Los umbrales de color y la línea del suelo vienen de fábrica, pero con otra luz o con otros
marcadores conviene aprenderlos de fotos reales. Primero localiza la cámara:

```powershell
.\.venv\Scripts\python.exe -m foottracker --list-cameras
```

Después abre el modo de captura con un recuadro guía y guarda fotos etiquetadas:

```powershell
.\.venv\Scripts\python.exe -m foottracker --camera auto --capture-calibration calibracion
```

Con **los pies apoyados en el suelo**, encuadra un marcador dentro del recuadro y pulsa:

| Tecla | Acción |
| --- | --- |
| **R** | Guarda una muestra del marcador rojo (freno) |
| **G** | Guarda una muestra del marcador verde (acelerador) |
| **S** | Escribe `calibracion/calibration.json` |
| **Esc** / **Q** | Sale y guarda el perfil con lo capturado |

Haz varias tomas por marcador, moviéndolo dentro del recuadro, para cubrir sombras y
brillos. Cada foto se guarda junto al perfil (`calibracion/rojo_01.png`, …). Si pulsas R con
el marcador verde delante, la muestra se rechaza y lo avisa: no se aprende el color equivocado.

Luego arranca con ese perfil:

```powershell
.\.venv\Scripts\python.exe -m foottracker --camera auto --calibration calibracion\calibration.json --preview
```

Qué aporta el perfil:

- Sustituye los rangos HSV de fábrica (`hue`, `saturation_min`, `value_min`) por los
  aprendidos; el resto de la detección (forma, área, morfología) no cambia.
- Fija la línea del suelo con la posición de los marcadores apoyados, así que **no hay que
  esperar los 0,75 s de calibración en vivo**: al arrancar ya aparece `listo`.
- Con el lado real del marcador (`--marker-cm`, `6` cm por defecto) calcula la escala
  píxeles→cm. La vista previa muestra la **altura del pie sobre el suelo** (`ROJO up ok 3.4 cm`)
  y `--json-status` la añade a cada paquete:

```json
{"version":1,"seq":1821,"red":"up","green":"down","redValid":true,"greenValid":true,"redHeightCm":3.4,"greenHeightCm":0.0}
```

La altura es una estimación: mide el desplazamiento vertical del marcador sobre la escala de
la foto de suelo, así que una perspectiva muy oblicua la exagera. El **contrato UDP hacia
Unity no cambia**: siguen siendo `up`, `down` y `unknown`, y la altura solo va al diagnóstico
local (vista previa y `--json-status`).

## Contrato UDP

Un datagrama JSON por paquete, 30 veces por segundo:

```json
{"version":1,"seq":1821,"red":"up","green":"down","redValid":true,"greenValid":true}
```

- `red`, `green`: `up`, `down` o `unknown`.
- `redValid`, `greenValid`: si el marcador se detectó con forma válida.
- `seq`: entero de 32 bits con signo que envuelve; Unity descarta paquetes repetidos o
  fuera de orden.

Los estados `unknown` van acompañados de `valid: false`, de modo que Unity distingue
"el pie está abajo" de "no se ve el marcador".

## Emisor de prueba

Para validar TaxiVR sin cámara ni marcadores:

```powershell
.\.venv\Scripts\python.exe -m tools.emit_mock --script truth-table
```

Guiones disponibles: `truth-table`, `drive`, `skid`, `loss`.

## Tests

```powershell
.\.venv\Scripts\python.exe -m pytest
```

Los tests usan imágenes sintéticas, capturas falsas y relojes inyectados, así que no
necesitan cámara ni red.

## Empaquetado para la demostración

```powershell
.\.venv\Scripts\python.exe -m pip install pyinstaller
.\.venv\Scripts\python.exe -m PyInstaller --onefile --name FootTracker --paths src --collect-submodules foottracker src/foottracker/__main__.py
```

Copia `dist/FootTracker.exe` a `TaxiVR/FootTracker/`. El ejecutable acepta los mismos
argumentos que el módulo, así que el equipo de demostración lo arranca con un acceso directo o
un `.bat`:

```powershell
FootTracker.exe --camera http://192.168.0.5:4747/video --preview
```

El equipo de demostración solo necesita Windows, Quest Link, drivers de GPU, el visor, los
mandos, un cable USB-C y la cámara. **No** necesita Unity Editor, Python ni OpenCV.

## Diagnóstico

| Síntoma | Causa probable |
| --- | --- |
| `No se pudo abrir la camara` | Índice equivocado u ocupada por otro programa. Prueba `--camera 1` |
| `No se pudo abrir la camara` con una IP que responde | El stream tarda más que `--camera-timeout`; súbelo (`--camera-timeout 10`) |
| `--probe` dice `sin imagen` con una URL | La app no está emitiendo, la IP del celular cambió o el firewall de Windows bloquea la entrada. Comprueba con `tools/probe_camera.py --ip …` |
| La cámara Wi-Fi se corta a ratos | Sube `--reconnect-delay`, baja la resolución del celular o acércalo al router |
| El pie responde con retraso | Latencia del Wi-Fi: baja a 640 × 480, usa USB, o prueba `OPENCV_FFMPEG_CAPTURE_OPTIONS` |
| Ambos pedales siempre `unknown` | Marcadores fuera de cuadro, mal iluminados o de color poco saturado |
| Todo `down` aunque levantes el pie | Umbral demasiado alto; baja `--lift-threshold` o usa `--threshold-mode relative` |
| Cambios intermitentes | Sube `--debounce` |
| `No se detecto ninguna webcam` | `--camera auto` no encontró cámaras; usa `--list-cameras` y el índice que aparezca |
| `no se vio color suficiente en el recuadro` | Acerca el marcador, mejora la luz, o pulsa la tecla del color correcto (R rojo, G verde) |
| `Perfil de calibracion invalido` | El JSON no es de FootTracker o quedó a medias; vuelve a capturar con `--capture-calibration` |
| Unity dice "sin puerto" | Otro proceso ocupa el 5055; cámbialo en ambos lados |
