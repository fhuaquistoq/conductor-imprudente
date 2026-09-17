"""Lanzador de FootTracker: camara -> clasificacion -> UDP local (especificacion 42-43)."""

from __future__ import annotations

import argparse
import socket
import sys
import time

from .classifier import FootClassifier, MarkerResult, ThresholdMode
from .protocol import DEFAULT_HOST, DEFAULT_PORT, DEFAULT_RATE, Sequence, packet
from .source import CameraError, camera

PREVIEW_WINDOW = "FootTracker - rojo frena, verde acelera"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="foottracker", description="Clasifica los pedales rojo/verde y los envia a TaxiVR por UDP local.")
    parser.add_argument("--host", default=DEFAULT_HOST, help="destino UDP (por defecto 127.0.0.1)")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="puerto UDP (por defecto 5055)")
    parser.add_argument("--rate", type=int, default=DEFAULT_RATE, help="paquetes por segundo (por defecto 30)")
    parser.add_argument("--camera", default="0", help="indice de webcam o URL del celular")
    parser.add_argument("--width", type=int, default=640)
    parser.add_argument("--height", type=int, default=480)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--threshold-mode", choices=[mode.value for mode in ThresholdMode], default=ThresholdMode.PIXELS.value)
    parser.add_argument("--lift-threshold", type=float, default=18.0, help="pixeles de levantamiento en modo pixels")
    parser.add_argument("--relative-lift", type=float, default=0.35, help="fraccion del lado del marcador en modo relative")
    parser.add_argument("--debounce", type=int, default=3, help="frames estables antes de aceptar un cambio")
    parser.add_argument("--calibrate", type=float, default=0.75, help="segundos de calibracion con los pies apoyados")
    parser.add_argument("--preview", action="store_true", help="muestra la ventana de diagnostico")
    parser.add_argument("--duration", type=float, default=0.0, help="segundos a ejecutar; 0 = sin limite")
    parser.add_argument("--json", action="store_true", dest="as_json", help="imprime un paquete por linea")
    return parser


def build_classifier(args: argparse.Namespace) -> FootClassifier:
    return FootClassifier.default(
        threshold_mode=ThresholdMode(args.threshold_mode),
        lift_threshold=args.lift_threshold,
        relative_lift_ratio=args.relative_lift,
        debounce_frames=args.debounce,
        calibration_seconds=args.calibrate,
    )


def annotate(frame, classifier: FootClassifier, red: MarkerResult, green: MarkerResult):
    import cv2

    cv2.putText(frame, f"ROJO {red.state.value} {'ok' if red.valid else 'sin datos'}", (12, 28), cv2.FONT_HERSHEY_SIMPLEX, .7, (0, 0, 255), 2)
    cv2.putText(frame, f"VERDE {green.state.value} {'ok' if green.valid else 'sin datos'}", (12, 58), cv2.FONT_HERSHEY_SIMPLEX, .7, (0, 255, 0), 2)
    aro = "listo" if classifier.red.calibrated and classifier.green.calibrated else "calibrando, deja los pies apoyados"
    cv2.putText(frame, f"suelo {aro}", (12, 88), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 1)
    return frame


def run(args: argparse.Namespace, stdout=sys.stdout) -> int:
    classifier = build_classifier(args)
    device = camera(args.camera, args.width, args.height, args.fps)
    try:
        device.open()
    except CameraError as error:
        print(f"FootTracker: {error}", file=sys.stderr)
        return 2

    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sequence = Sequence()
    interval = 1.0 / max(args.rate, 1)
    previous = time.perf_counter()
    started = previous
    print(f"FootTracker: {device.describe()} -> udp://{args.host}:{args.port} a {args.rate} Hz", file=stdout)
    try:
        while True:
            now = time.perf_counter()
            delta = now - previous
            frame = device.read()
            red, green = classifier.process(frame, delta)
            previous = now
            message = packet(sequence.next(), red.state, green.state, red.valid, green.valid)
            sender.sendto(message.encode(), (args.host, args.port))
            if args.as_json:
                print(message.to_json(), file=stdout, flush=True)
            if args.preview and frame is not None:
                import cv2

                cv2.imshow(PREVIEW_WINDOW, annotate(frame, classifier, red, green))
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


def main(argv: list[str] | None = None) -> int:
    return run(build_parser().parse_args(argv))


if __name__ == "__main__":
    raise SystemExit(main())
