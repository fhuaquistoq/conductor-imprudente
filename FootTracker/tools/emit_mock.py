"""Emisor UDP de prueba: valida TaxiVR sin camara ni marcadores.

Uso: python -m tools.emit_mock --script truth-table
"""

from __future__ import annotations

import argparse
import pathlib
import socket
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "src"))

from foottracker.protocol import DEFAULT_HOST, DEFAULT_PORT, FootState, Sequence, packet  # noqa: E402

SCRIPTS: dict[str, list[tuple[float, FootState, FootState]]] = {
    # (segundos, rojo, verde)
    "truth-table": [
        (1.5, FootState.DOWN, FootState.DOWN),
        (1.5, FootState.DOWN, FootState.UP),
        (1.5, FootState.UP, FootState.UP),
        (1.5, FootState.DOWN, FootState.DOWN),
        (1.5, FootState.UP, FootState.DOWN),
    ],
    "drive": [
        (1.0, FootState.DOWN, FootState.DOWN),
        (1.0, FootState.UP, FootState.DOWN),
        (6.0, FootState.UP, FootState.DOWN),
        (4.0, FootState.UP, FootState.UP),
        (2.0, FootState.DOWN, FootState.UP),
    ],
    "skid": [
        (1.0, FootState.DOWN, FootState.DOWN),
        (1.0, FootState.UP, FootState.DOWN),
        (4.0, FootState.DOWN, FootState.DOWN),
        (2.0, FootState.UP, FootState.UP),
    ],
    "loss": [
        (1.0, FootState.DOWN, FootState.DOWN),
        (1.0, FootState.UP, FootState.DOWN),
        (3.0, FootState.UNKNOWN, FootState.UNKNOWN),
    ],
}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="emit_mock", description="Emisor UDP de prueba para TaxiVR.")
    parser.add_argument("--script", choices=sorted(SCRIPTS), default="truth-table")
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--rate", type=int, default=30)
    parser.add_argument("--limit", type=float, default=0.0, help="segundos totales; 0 = guion completo")
    parser.add_argument("--json", action="store_true", dest="as_json", help="imprime cada paquete")
    return parser


def main(argv: list[str] | None = None, stdout=sys.stdout) -> int:
    args = build_parser().parse_args(argv)
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sequence = Sequence()
    interval = 1.0 / max(args.rate, 1)
    started = time.perf_counter()
    sent = 0
    print(f"emit_mock: {args.script} -> {args.host}:{args.port} a {args.rate} Hz", file=stdout)
    try:
        for seconds, red, green in SCRIPTS[args.script]:
            deadline = time.perf_counter() + seconds
            while time.perf_counter() < deadline:
                message = packet(sequence.next(), red, green, red != FootState.UNKNOWN, green != FootState.UNKNOWN)
                sender.sendto(message.encode(), (args.host, args.port))
                sent += 1
                if args.as_json:
                    print(message.to_json(), file=stdout, flush=True)
                if args.limit and time.perf_counter() - started >= args.limit:
                    print(f"emit_mock: limite alcanzado, {sent} paquetes enviados", file=stdout)
                    return 0
                time.sleep(interval)
    except KeyboardInterrupt:
        pass
    finally:
        sender.close()
    print(f"emit_mock: {sent} paquetes enviados", file=stdout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
