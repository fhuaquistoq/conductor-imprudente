# FootTracker

Proceso independiente que lee la cámara, clasifica dos marcadores de color y envía
**solo el estado de los dos pedales** a `TaxiVR.exe` por UDP local. Unity no procesa
vídeo: este programa es el único que abre la cámara.

- **Rojo** = freno (`PIVOT_PEDAL_FRENO`)
- **Verde** = acelerador (`PIVOT_PEDAL_ACELERADOR`)

El estado inicial de ambos es `down` (pies apoyados) y el juego **no interpreta eso como
pisar los pedales**: los pedales están *desarmados* hasta que un pie se levanta por
primera vez. Un marcador perdido o deformado es `unknown`, que nunca equivale a `up` ni a
`down`.

## Marcadores

Imprime dos cuadrados planos, uno rojo y otro verde, de al menos 6 cm de lado, y pégalos
sobre la zapatilla mirando a la cámara. La cámara debe ver los dos cuadrados completos y
sin obstrucciones. Un cuadrado muy deformado, muy alargado o girado en exceso se rechaza
como `unknown`.

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
altura del suelo. La ventana de diagnóstico muestra `calibrando` y luego `listo`.

| Opción | Descripción |
| --- | --- |
| `--camera` | Índice de webcam (`0`, `1`, …) o URL del celular (`http://192.168.0.5:8080/video`) |
| `--host`, `--port` | Destino UDP. Por defecto `127.0.0.1:5055` |
| `--rate` | Paquetes por segundo. Por defecto `30` |
| `--width`, `--height`, `--fps` | Resolución y frecuencia solicitadas a la cámara |
| `--camera-timeout` | Segundos máximos para abrir una cámara de red. Por defecto `5` |
| `--frame-stale` | Antigüedad máxima (s) del frame para considerarlo imagen. Por defecto `0.3` |
| `--reconnect-delay` | Espera inicial (s) entre reintentos de cámara. Por defecto `0.5` |
| `--probe` | Mide la cámara y sale, sin enviar paquetes |
| `--threshold-mode` | `pixels` (por defecto) o `relative` (proporcional al tamaño del marcador) |
| `--lift-threshold` | Píxeles de levantamiento en modo `pixels` |
| `--relative-lift` | Fracción del lado del marcador en modo `relative` |
| `--debounce` | Frames estables antes de aceptar un cambio. Por defecto `3` |
| `--calibrate` | Segundos de calibración con los pies apoyados |
| `--preview` | Ventana con las máscaras y el estado detectado |
| `--json` | Imprime cada paquete enviado |
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
| Unity dice "sin puerto" | Otro proceso ocupa el 5055; cámbialo en ambos lados |
