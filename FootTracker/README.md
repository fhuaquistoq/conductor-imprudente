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
| `--threshold-mode` | `pixels` (por defecto) o `relative` (proporcional al tamaño del marcador) |
| `--lift-threshold` | Píxeles de levantamiento en modo `pixels` |
| `--relative-lift` | Fracción del lado del marcador en modo `relative` |
| `--debounce` | Frames estables antes de aceptar un cambio. Por defecto `3` |
| `--calibrate` | Segundos de calibración con los pies apoyados |
| `--preview` | Ventana con las máscaras y el estado detectado |
| `--json` | Imprime cada paquete enviado |
| `--duration` | Segundos a ejecutar; `0` = sin límite |

El celular puede presentarse a Windows como **webcam USB**, que es lo recomendado. Una
transmisión por Wi-Fi también funciona (`--camera http://…`) pero añade latencia.

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

Los tests usan imágenes sintéticas y muestras de marcador, así que no necesitan cámara.

## Empaquetado para la demostración

```powershell
.\.venv\Scripts\python.exe -m pip install pyinstaller
.\.venv\Scripts\python.exe -m PyInstaller --onefile --name FootTracker --paths src --collect-submodules foottracker src/foottracker/__main__.py
```

Copia `dist/FootTracker.exe` a `TaxiVR/FootTracker/` junto a un `config.json`:

```json
{ "camera": "0", "host": "127.0.0.1", "port": 5055, "rate": 30, "calibrate": 0.75 }
```

El equipo de demostración solo necesita Windows, Quest Link, drivers de GPU, el visor, los
mandos, un cable USB-C y la cámara. **No** necesita Unity Editor, Python ni OpenCV.

## Diagnóstico

| Síntoma | Causa probable |
| --- | --- |
| `No se pudo abrir la camara` | Índice equivocado u ocupada por otro programa. Prueba `--camera 1` |
| Ambos pedales siempre `unknown` | Marcadores fuera de cuadro, mal iluminados o de color poco saturado |
| Todo `down` aunque levantes el pie | Umbral demasiado alto; baja `--lift-threshold` o usa `--threshold-mode relative` |
| Cambios intermitentes | Sube `--debounce` |
| Unity dice "sin puerto" | Otro proceso ocupa el 5055; cámbialo en ambos lados |
