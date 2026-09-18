"""Sonda camaras Wi-Fi en la red local para encontrar la URL del celular antes de jugar.

Uso: python -m tools.probe_camera --ip 192.168.0.5
"""

from __future__ import annotations

import argparse
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "src"))

from foottracker.probe import lan_candidates, scan_lan  # noqa: E402


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="probe_camera", description="Busca la camara del celular en la red local.")
    parser.add_argument("--ip", required=True, help="IP del celular, tal como la muestra la app de la camara")
    parser.add_argument("--timeout", type=float, default=0.4, help="segundos por intento TCP")
    parser.add_argument("--seconds", type=float, default=2.0, help="segundos de medida por stream")
    return parser


def main(argv: list[str] | None = None, stdout=sys.stdout) -> int:
    args = build_parser().parse_args(argv)
    candidates = lan_candidates(args.ip)
    print(f"probe_camera: sondeando {args.ip} en {len(candidates)} URLs habituales", file=stdout)
    reports = scan_lan(args.ip, timeout=args.timeout, probe_seconds=args.seconds)
    for report in reports:
        print(f"  {report.describe()}", file=stdout)
    usable = [report for report in reports if report.usable]
    if not usable:
        print("probe_camera: sin stream. Comprueba que la app del celular este emitiendo y el firewall de Windows.", file=stdout)
        return 1
    print(f"probe_camera: usa --camera {usable[0].url}", file=stdout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
