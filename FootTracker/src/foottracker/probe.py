"""Diagnostico de camaras externas: medir un stream y sondear las URLs habituales.

Los puertos conocidos (4747 DroidCam, 8080 IP Webcam/CamDroid, 8554 RTSP) se prueban
primero con un TCP barato y solo se intenta decodificar lo que responde, para no llenar
la consola de errores de FFMPEG por cada puerto cerrado.
"""

from __future__ import annotations

import socket
import time
from dataclasses import dataclass
from typing import Callable
from urllib.parse import urlsplit

import cv2

from .source import CaptureFactory, Clock, default_factory, open_capture

MAX_PROBE_FRAMES = 10_000


@dataclass(frozen=True)
class StreamProbe:
    """Resultado de medir un stream. `opened` es False si no hubo imagen."""

    opened: bool
    width: int = 0
    height: int = 0
    frames: int = 0
    seconds: float = 0.0
    measured_fps: float = 0.0
    error: str = ""

    def describe(self) -> str:
        if not self.opened:
            return f"sin imagen: {self.error}"
        return f"{self.width}x{self.height} a {self.measured_fps:.1f} fps ({self.frames} frames en {self.seconds:.1f} s)"


Connect = Callable[[str, int, float], bool]
Prober = Callable[..., StreamProbe]


def probe_stream(
    value: int | str,
    *,
    width: int = 640,
    height: int = 480,
    fps: int = 30,
    seconds: float = 3.0,
    open_timeout: float = 5.0,
    factory: CaptureFactory = default_factory,
    clock: Clock = time.perf_counter,
) -> StreamProbe:
    """Cuenta frames durante `seconds` y mide los FPS reales. Nunca lanza: informa el error."""

    capture = None
    try:
        capture = open_capture(value, open_timeout, factory)
        if not capture.isOpened():
            return StreamProbe(False, error=f"no se pudo abrir {value!r}")
        capture.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        capture.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        capture.set(cv2.CAP_PROP_FPS, fps)
        started = clock()
        now = started
        frames = 0
        last = None
        while frames < MAX_PROBE_FRAMES:
            now = clock()
            if now - started >= seconds:
                break
            ok, frame = capture.read()
            if not ok or frame is None or not getattr(frame, "size", 0):
                break
            frames += 1
            last = frame
        elapsed = max(now - started, 1e-6)
        if last is None:
            return StreamProbe(False, seconds=elapsed, error="el stream no entrego ningun frame")
        return StreamProbe(True, int(last.shape[1]), int(last.shape[0]), frames, elapsed, frames / elapsed)
    except Exception as error:  # la sonda diagnostica, nunca tumba el proceso
        return StreamProbe(False, error=str(error))
    finally:
        if capture is not None:
            capture.release()


KNOWN_STREAMS: tuple[tuple[int, str], ...] = (
    (4747, "/video"),  # DroidCam
    (4747, "/mjpegfeed"),
    (8080, "/video"),  # IP Webcam y CamDroid
    (8080, "/videofeed"),
    (8000, "/video"),  # Iriun
    (8554, "/live"),  # RTSP generico
    (8554, "/h264"),
)


def lan_candidates(host: str) -> list[str]:
    """URLs habituales de las apps de camara por Wi-Fi, de mas a menos probable."""

    base = host.strip()
    return [f"{'rtsp' if port == 8554 else 'http'}://{base}:{port}{path}" for port, path in KNOWN_STREAMS]


@dataclass(frozen=True)
class CandidateReport:
    url: str
    port: int
    reachable: bool = False
    probe: StreamProbe | None = None

    @property
    def usable(self) -> bool:
        return self.probe is not None and self.probe.opened

    def describe(self) -> str:
        if self.usable:
            return f"{self.url} -> {self.probe.describe()}"
        if self.reachable:
            detail = self.probe.error if self.probe is not None else "sin stream"
            return f"{self.url} -> puerto abierto, {detail}"
        return f"{self.url} -> sin respuesta"


def connect_tcp(host: str, port: int, timeout: float) -> bool:
    with socket.create_connection((host, port), timeout):
        return True


def scan_lan(
    host: str,
    *,
    timeout: float = 0.4,
    connect: Connect = connect_tcp,
    prober: Prober = probe_stream,
    probe_seconds: float = 2.0,
    candidates: list[str] | None = None,
) -> list[CandidateReport]:
    """Sondea las URLs habituales en `host` y describe cada candidato."""

    reports = []
    for url in candidates if candidates is not None else lan_candidates(host):
        port = urlsplit(url).port or 0
        try:
            reachable = bool(connect(host, port, timeout))
        except OSError:
            reachable = False
        probe = prober(url, seconds=probe_seconds) if reachable else None
        reports.append(CandidateReport(url, port, reachable, probe))
    return reports
