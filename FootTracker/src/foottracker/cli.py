"""Lanzador de FootTracker: camara -> clasificacion -> UDP local (especificacion 42-43)."""

from __future__ import annotations

import argparse
import json
import socket
import sys
import time
from pathlib import Path

from .calibration import CalibrationProfile, SampleCollector, guide_rect
from .classifier import GREEN, RED, FootClassifier, MarkerResult, MarkerTracker, ThresholdMode
from .photos import analyze_folder, write_annotated
from .probe import probe_stream
from .config import DEFAULT_CONFIG, ConfigError, apply as apply_config, load as load_config
from .protocol import DEFAULT_HOST, DEFAULT_PORT, DEFAULT_RATE, Sequence, packet, pedal_of_state
from .source import CameraError, CameraSource, camera, first_available_camera, list_cameras, parse_camera

PREVIEW_WINDOW = "FootTracker - rojo frena, verde acelera"
CAPTURE_WINDOW = "FootTracker - calibracion (R rojo, G verde, S guardar, Esc salir)"
PHOTO_WINDOW = "FootTracker - fotos (Espacio guarda, Esc salir)"
CALIBRATION_FILE = "calibration.json"
PHOTO_PREFIX = "foto"
AUTO_CAMERA = ("auto", "first")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="foottracker", description="Clasifica los pedales rojo/verde y los envia a TaxiVR por UDP local.")
    parser.add_argument("--config", default=DEFAULT_CONFIG, metavar="FILE", help="perfil TOML de destino (por defecto config.toml si existe)")
    parser.add_argument("--host", default=None, help="destino UDP; gana al perfil (por defecto 127.0.0.1)")
    parser.add_argument("--port", type=int, default=None, help="puerto UDP; gana al perfil (por defecto 5055)")
    parser.add_argument("--rate", type=int, default=None, help="paquetes por segundo; gana al perfil (por defecto 30)")
    parser.add_argument("--camera", default=None, help="indice de webcam, URL del celular o 'auto'; gana al perfil")
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=480)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--camera-timeout", type=float, default=5.0, help="segundos maximos para abrir la camara de red")
    parser.add_argument("--frame-stale", type=float, default=0.3, help="antiguedad maxima del frame para considerarlo imagen")
    parser.add_argument("--reconnect-delay", type=float, default=0.5, help="espera inicial entre reintentos de camara")
    parser.add_argument("--list-cameras", action="store_true", dest="list_cameras", help="lista las webcams conectadas y sale")
    parser.add_argument("--probe", action="store_true", help="mide la camara y sale, sin enviar paquetes")
    parser.add_argument("--capture-calibration", default="", metavar="DIR", help="captura fotos con el teclado y escribe DIR/calibration.json")
    parser.add_argument("--capture-photos", default="", metavar="DIR", help="guarda fotos numeradas con la barra espaciadora y sale con Esc")
    parser.add_argument("--photos", default="", metavar="DIR", help="analiza una carpeta de fotos y sale, sin camara")
    parser.add_argument("--photos-out", default="", metavar="DIR", help="guarda copias anotadas del analisis de --photos")
    parser.add_argument("--calibration", default="", metavar="FILE", help="perfil JSON de calibracion a cargar en ejecucion")
    parser.add_argument("--marker-cm", type=float, default=6.0, help="lado real del marcador en cm (por defecto 6)")
    parser.add_argument("--threshold-mode", choices=[mode.value for mode in ThresholdMode], default=ThresholdMode.PIXELS.value)
    parser.add_argument("--lift-threshold", type=float, default=18.0, help="pixeles de levantamiento en modo pixels")
    parser.add_argument("--relative-lift", type=float, default=0.35, help="fraccion del lado del marcador en modo relative")
    parser.add_argument("--debounce", type=int, default=3, help="frames estables antes de aceptar un cambio")
    parser.add_argument("--calibrate", type=float, default=0.75, help="segundos de calibracion con los pies apoyados")
    parser.add_argument("--preview", action="store_true", help="muestra la ventana de diagnostico")
    parser.add_argument("--duration", type=float, default=0.0, help="segundos a ejecutar; 0 = sin limite")
    parser.add_argument("--json", action="store_true", dest="as_json", help="imprime un paquete por linea")
    parser.add_argument("--json-status", action="store_true", dest="json_status", help="imprime el estado con la altura medida")
    return parser


def resolve_camera(value: str) -> int | str:
    """`auto` sondea las webcams conectadas; cualquier otro valor es indice o URL."""

    if value.strip().lower() in AUTO_CAMERA:
        index = first_available_camera()
        if index is None:
            raise CameraError("No se detecto ninguna webcam conectada.")
        return index
    return parse_camera(value)


def build_source(args: argparse.Namespace) -> CameraSource:
    return camera(
        resolve_camera(args.camera or "0"),
        args.width,
        args.height,
        args.fps,
        open_timeout=args.camera_timeout,
        stale_seconds=args.frame_stale,
        reconnect_delay=args.reconnect_delay,
    )


def build_classifier(args: argparse.Namespace, profile: CalibrationProfile | None = None) -> FootClassifier:
    options = dict(
        threshold_mode=ThresholdMode(args.threshold_mode),
        lift_threshold=args.lift_threshold,
        relative_lift_ratio=args.relative_lift,
        debounce_frames=args.debounce,
        calibration_seconds=args.calibrate,
    )
    if profile is None:
        return FootClassifier.default(**options)
    trackers = []
    for kind, base in (("red", RED), ("green", GREEN)):
        floor_y, side_px = profile.floor_for(kind)
        trackers.append(
            MarkerTracker(
                profile.settings_for(kind, base),
                start_down_y=floor_y,
                marker_cm=profile.marker_cm,
                ref_side_px=side_px,
                **options,
            )
        )
    return FootClassifier(trackers[0], trackers[1])


def load_profile(path: str) -> CalibrationProfile | None:
    if not path:
        return None
    try:
        return CalibrationProfile.load(path)
    except FileNotFoundError as error:
        raise CameraError(f"No se encontro el perfil de calibracion {path!r}.") from error
    except (ValueError, OSError) as error:
        raise CameraError(f"Perfil de calibracion invalido {path!r}: {error}") from error


def height_text(result: MarkerResult) -> str:
    return f"{result.height_cm:.1f} cm" if result.height_cm is not None else ""


def status_json(message, red: MarkerResult, green: MarkerResult) -> str:
    payload = message.to_dict()
    payload["brakeHeightCm"] = None if red.height_cm is None else round(red.height_cm, 1)
    payload["acceleratorHeightCm"] = None if green.height_cm is None else round(green.height_cm, 1)
    return json.dumps(payload, separators=(",", ":"))


def annotate(frame, classifier: FootClassifier, red: MarkerResult, green: MarkerResult, source: str = ""):
    import cv2

    cv2.putText(frame, f"ROJO {red.state.value} {'ok' if red.valid else 'sin datos'} {height_text(red)}", (12, 28), cv2.FONT_HERSHEY_SIMPLEX, .7, (0, 0, 255), 2)
    cv2.putText(frame, f"VERDE {green.state.value} {'ok' if green.valid else 'sin datos'} {height_text(green)}", (12, 58), cv2.FONT_HERSHEY_SIMPLEX, .7, (0, 255, 0), 2)
    aro = "listo" if classifier.red.calibrated and classifier.green.calibrated else "calibrando, deja los pies apoyados"
    cv2.putText(frame, f"suelo {aro}", (12, 88), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 1)
    if source:
        cv2.putText(frame, f"camara {source}", (12, 118), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 1)
    return frame


def list_cameras_mode(stdout=sys.stdout) -> int:
    infos = list_cameras()
    print("FootTracker: sondeo de webcams conectadas", file=stdout)
    for info in infos:
        print(f"  {info.describe()}", file=stdout)
    available = [info for info in infos if info.available]
    if not available:
        print("FootTracker: ninguna webcam responde. Revisa el cable o los drivers, o prueba --camera 1.", file=stdout)
        return 1
    print(f"FootTracker: usa --camera {available[0].index} (o --camera auto)", file=stdout)
    return 0


def capture_calibration_mode(args: argparse.Namespace, stdout=sys.stdout) -> int:
    """Captura fotos con el teclado y escribe un perfil de calibracion."""

    import cv2

    outdir = Path(args.capture_calibration)
    outdir.mkdir(parents=True, exist_ok=True)
    try:
        device = build_source(args)
        device.open()
    except CameraError as error:
        print(f"FootTracker: {error}", file=sys.stderr)
        return 2
    collector = SampleCollector(marker_cm=args.marker_cm)
    print("FootTracker: con los pies apoyados en el suelo, encuadra un marcador en el recuadro y pulsa R (rojo) o G (verde).", file=stdout)
    print(f"FootTracker: S guarda el perfil en {outdir / CALIBRATION_FILE}; Esc sale y guarda.", file=stdout)
    try:
        while True:
            frame = device.read()
            if frame is None:
                if cv2.waitKey(10) & 0xFF == 27:
                    break
                continue
            x0, y0, x1, y1 = guide_rect(frame.shape)
            view = frame.copy()
            cv2.rectangle(view, (x0, y0), (x1, y1), (255, 255, 255), 2)
            cv2.putText(view, f"rojo {collector.count('red')}   verde {collector.count('green')}", (12, 28), cv2.FONT_HERSHEY_SIMPLEX, .7, (255, 255, 255), 2)
            cv2.imshow(CAPTURE_WINDOW, view)
            key = cv2.waitKey(1) & 0xFF
            if key in (ord("r"), ord("g")):
                kind = "red" if key == ord("r") else "green"
                sample = collector.add(kind, frame, (x0, y0, x1, y1))
                if sample is None:
                    print("FootTracker: no se vio color suficiente en el recuadro; acerca el marcador.", file=stdout)
                    continue
                cv2.imwrite(str(outdir / f"{kind}_{collector.count(kind):02d}.png"), frame)
                print(f"FootTracker: {kind} {collector.count(kind)} guardada (suelo y={sample.floor_y:.0f}, lado {sample.side_px:.0f}px)", file=stdout)
            elif key == ord("s"):
                profile = collector.build()
                if profile is None:
                    print("FootTracker: aun no hay muestras que guardar.", file=stdout)
                    continue
                print(f"FootTracker: perfil guardado en {profile.save(outdir / CALIBRATION_FILE)}", file=stdout)
            elif key in (27, ord("q")):
                break
    except KeyboardInterrupt:
        pass
    finally:
        device.close()
        cv2.destroyAllWindows()
    profile = collector.build()
    if profile is None:
        print("FootTracker: sin muestras; no se escribio ningun perfil.", file=stdout)
        return 1
    print(f"FootTracker: perfil guardado en {profile.save(outdir / CALIBRATION_FILE)} ({collector.count('red')} rojas, {collector.count('green')} verdes)", file=stdout)
    return 0


def next_photo_index(folder: Path, prefix: str = PHOTO_PREFIX) -> int:
    """Primer numero libre para no pisar las fotos de una sesion anterior."""

    numbers = []
    for path in Path(folder).glob(f"{prefix}_*.png"):
        suffix = path.stem[len(prefix) + 1:]
        if suffix.isdigit():
            numbers.append(int(suffix))
    return max(numbers, default=0) + 1


def capture_photos_mode(args: argparse.Namespace, stdout=sys.stdout) -> int:
    """Solo fotos: la barra espaciadora guarda una imagen numerada y Esc sale."""

    import cv2

    outdir = Path(args.capture_photos)
    outdir.mkdir(parents=True, exist_ok=True)
    try:
        device = build_source(args)
        device.open()
    except CameraError as error:
        print(f"FootTracker: {error}", file=sys.stderr)
        return 2
    index = next_photo_index(outdir)
    taken = 0
    print(f"FootTracker: Espacio guarda una foto numerada en {outdir}; Esc sale.", file=stdout)
    try:
        while True:
            frame = device.read()
            if frame is None:
                if cv2.waitKey(10) & 0xFF == 27:
                    break
                continue
            view = frame.copy()
            cv2.putText(view, f"foto {index + taken}", (12, 28), cv2.FONT_HERSHEY_SIMPLEX, .7, (255, 255, 255), 2)
            cv2.imshow(PHOTO_WINDOW, view)
            key = cv2.waitKey(1) & 0xFF
            if key == ord(" "):
                target = outdir / f"{PHOTO_PREFIX}_{index + taken:04d}.png"
                cv2.imwrite(str(target), frame)
                taken += 1
                print(f"FootTracker: foto {taken} guardada en {target}", file=stdout)
            elif key in (27, ord("q")):
                break
    except KeyboardInterrupt:
        pass
    finally:
        device.close()
        cv2.destroyAllWindows()
    print(f"FootTracker: {taken} fotos guardadas en {outdir} ({index}-{index + taken - 1})." if taken else f"FootTracker: sin fotos nuevas en {outdir}.", file=stdout)
    return 0


def photos_mode(args: argparse.Namespace, stdout=sys.stdout) -> int:
    """Analiza una carpeta de fotos sin camara: freno rojo y acelerador verde o azul."""

    folder = Path(args.photos)
    if not folder.is_dir():
        print(f"FootTracker: la carpeta {args.photos!r} no existe.", file=sys.stderr)
        return 2
    report = analyze_folder(
        folder,
        marker_cm=args.marker_cm,
        lift_threshold=args.lift_threshold,
        debounce_frames=args.debounce,
    )
    print(f"FootTracker: analisis de {folder} (rojo frena, verde o azul acelera)", file=stdout)
    for line in report.lines():
        print(line, file=stdout)
    if args.photos_out:
        written = write_annotated(report, args.photos_out)
        print(f"FootTracker: {len(written)} fotos anotadas en {args.photos_out}", file=stdout)
    return 0


def run(args: argparse.Namespace, stdout=sys.stdout) -> int:
    try:
        profile = load_profile(args.calibration)
        classifier = build_classifier(args, profile)
        device = build_source(args)
        device.open()
    except CameraError as error:
        print(f"FootTracker: {error}", file=sys.stderr)
        return 2

    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sequence = Sequence()
    interval = 1.0 / max(args.rate, 1)
    previous = time.perf_counter()
    started = previous
    reported: tuple[bool, int] | None = None
    if profile is not None:
        print(f"FootTracker: calibracion {args.calibration} cargada (marcador {profile.marker_cm:g} cm)", file=stdout)
    print(f"FootTracker: {device.describe()} -> udp://{args.host}:{args.port} a {args.rate} Hz", file=stdout)
    try:
        while True:
            now = time.perf_counter()
            delta = now - previous
            frame = device.read()
            status = device.status()
            state = (status.connected, status.reconnects)
            if reported is not None and state != reported:
                print(f"FootTracker: camara {status.message}, {status.reconnects} reconexiones", file=sys.stderr)
            reported = state
            # La marca de tiempo es la del frame que se acaba de clasificar, para que el visor pueda
            # medir latencia desde el movimiento real y no solo lo que tarda el datagrama.
            stamp = time.time()
            red, green = classifier.process(frame, delta)
            previous = now
            message = packet(
                sequence.next(),
                pedal_of_state(red.state, red.confidence, red.value),
                pedal_of_state(green.state, green.confidence, green.value),
                timestamp=stamp,
                calibrated=classifier.red.calibrated and classifier.green.calibrated,
            )
            sender.sendto(message.encode(), (args.host, args.port))
            if args.as_json:
                print(message.to_json(), file=stdout, flush=True)
            if args.json_status:
                print(status_json(message, red, green), file=stdout, flush=True)
            if args.preview and frame is not None:
                import cv2

                cv2.imshow(PREVIEW_WINDOW, annotate(frame, classifier, red, green, status.message))
                if cv2.waitKey(1) & 0xFF == 27:
                    break
            if args.duration and now - started >= args.duration:
                break
            remaining = interval - (time.perf_counter() - now)
            if remaining > 0:
                time.sleep(remaining)
    except KeyboardInterrupt:
        pass
    finally:
        sender.close()
        device.close()
        if args.preview:
            import cv2

            cv2.destroyAllWindows()
    return 0


def probe_source(args: argparse.Namespace, stdout=sys.stdout) -> int:
    """Mide la camara y sale: sirve para comprobar una camara Wi-Fi antes de jugar."""

    try:
        value = resolve_camera(args.camera)
    except CameraError as error:
        print(f"FootTracker: {error}", file=sys.stderr)
        return 2
    seconds = args.duration if args.duration > 0 else 3.0
    report = probe_stream(
        value,
        width=args.width,
        height=args.height,
        fps=args.fps,
        seconds=seconds,
        open_timeout=args.camera_timeout,
    )
    print(f"FootTracker: sonda de {value!r}: {report.describe()}", file=stdout)
    return 0 if report.opened else 2


def main(argv: list[str] | None = None, stdout=sys.stdout) -> int:
    args = build_parser().parse_args(argv)
    try:
        # El fichero por defecto es opcional; uno pedido a mano que no exista si es error.
        apply_config(args, load_config(args.config, required=args.config != DEFAULT_CONFIG))
    except ConfigError as error:
        print(f"FootTracker: {error}", file=sys.stderr)
        return 2
    if args.list_cameras:
        return list_cameras_mode(stdout)
    if args.capture_calibration:
        return capture_calibration_mode(args, stdout)
    if args.capture_photos:
        return capture_photos_mode(args, stdout)
    if args.photos:
        return photos_mode(args, stdout)
    if args.probe:
        return probe_source(args, stdout)
    return run(args, stdout)


if __name__ == "__main__":
    raise SystemExit(main())
