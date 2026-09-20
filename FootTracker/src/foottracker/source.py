"""Fuentes de video: webcam USB o camara externa publicada por Wi-Fi (especificacion 44).

Una URL de red se abre con FFMPEG y con timeouts acotados, y la lectura vive en un hilo
que publica solo el ultimo frame. Asi un atasco de Wi-Fi no bloquea el envio UDP ni deja
el proceso sin recuperarse cuando la camara vuelve.
"""

from __future__ import annotations

import threading
import time
from dataclasses import dataclass, field
from typing import Callable

import cv2
import numpy as np


class CameraError(RuntimeError):
    """La camara no pudo abrirse o dejo de entregar imagen."""


NETWORK_SCHEMES = ("http://", "https://", "rtsp://", "rtsps://", "rtmp://", "udp://", "tcp://")
CaptureFactory = Callable[..., "cv2.VideoCapture"]
Clock = Callable[[], float]


def default_factory(*args):
    return cv2.VideoCapture(*args)


def is_network(value: object) -> bool:
    """True si la fuente es una URL de red y no un indice de webcam."""

    return isinstance(value, str) and value.lower().startswith(NETWORK_SCHEMES)


def open_capture(source: int | str, open_timeout: float, factory: CaptureFactory = default_factory):
    """Abre el capture. En red fija FFMPEG y timeouts; en USB deja el backend automatico."""

    if not is_network(source):
        return factory(source)
    seconds = max(float(open_timeout), 0.1)
    params = [
        cv2.CAP_PROP_OPEN_TIMEOUT_MSEC, int(seconds * 1000),
        cv2.CAP_PROP_READ_TIMEOUT_MSEC, int(min(seconds, 1.0) * 1000),
    ]
    return factory(source, cv2.CAP_FFMPEG, params)


@dataclass(frozen=True)
class CameraInfo:
    """Resultado de sondear un indice de webcam: si responde y a que resolucion."""

    index: int
    available: bool
    width: int = 0
    height: int = 0

    def describe(self) -> str:
        if not self.available:
            return f"camara {self.index} -> sin senal"
        if self.width and self.height:
            return f"camara {self.index} -> {self.width}x{self.height}"
        return f"camara {self.index} -> conectada"


def probe_camera(index: int, factory: CaptureFactory = default_factory) -> CameraInfo:
    """Abre el indice, intenta leer un frame y lo cierra. Nunca lanza."""

    capture = None
    try:
        capture = factory(int(index))
        if not capture.isOpened():
            return CameraInfo(int(index), False)
        ok, frame = capture.read()
        if ok and frame is not None and getattr(frame, "size", 0):
            return CameraInfo(int(index), True, int(frame.shape[1]), int(frame.shape[0]))
        return CameraInfo(int(index), True)
    except Exception:  # el sondeo diagnostica, nunca tumba el proceso
        return CameraInfo(int(index), False)
    finally:
        if capture is not None:
            capture.release()


def list_cameras(max_devices: int = 5, factory: CaptureFactory = default_factory) -> list[CameraInfo]:
    """Sondea 0..N-1 y para en el primer hueco despues de haber encontrado una camara.

    Se sigue sondeando mientras no aparezca ninguna, porque en Windows es normal que el
    indice 0 este libre y la webcam recien conectada caiga en el 1 o el 2.
    """

    found: list[CameraInfo] = []
    for index in range(max(int(max_devices), 1)):
        info = probe_camera(index, factory)
        if not info.available and any(known.available for known in found):
            break
        found.append(info)
    return found


def first_available_camera(max_devices: int = 5, *, finder: Callable[..., list[CameraInfo]] = list_cameras) -> int | None:
    """Indice de la primera webcam que responde, para `--camera auto`."""

    for info in finder(max_devices):
        if info.available:
            return info.index
    return None


class FrameBuffer:
    """Ultimo frame leido con su marca de tiempo, compartido entre hilos."""

    def __init__(self):
        self._lock = threading.Lock()
        self._frame: np.ndarray | None = None
        self._stamp = 0.0

    def put(self, frame: np.ndarray, now: float) -> None:
        with self._lock:
            self._frame = frame
            self._stamp = now

    def take(self, now: float, stale_seconds: float) -> np.ndarray | None:
        """Copia del ultimo frame si sigue fresco; si no, no hay imagen que clasificar."""

        with self._lock:
            if self._frame is None or now - self._stamp > stale_seconds:
                return None
            return self._frame.copy()

    def age(self, now: float) -> float | None:
        with self._lock:
            if self._frame is None:
                return None
            return max(now - self._stamp, 0.0)


@dataclass(frozen=True)
class SourceStatus:
    """Estado observable de la fuente, para diagnostico en consola y en la vista previa."""

    connected: bool
    frames: int
    reconnects: int
    last_frame_age: float | None
    message: str


class FrameReader:
    """Hilo que mantiene la camara abierta, publica el ultimo frame y reconecta."""

    def __init__(
        self,
        source: int | str,
        *,
        width: int = 640,
        height: int = 480,
        fps: int = 30,
        open_timeout: float = 5.0,
        stale_seconds: float = 0.3,
        reconnect_delay: float = 0.5,
        max_reconnect_delay: float = 5.0,
        factory: CaptureFactory = default_factory,
        clock: Clock = time.perf_counter,
    ):
        self.source = source
        self.width = width
        self.height = height
        self.fps = fps
        self.open_timeout = open_timeout
        self.stale_seconds = stale_seconds
        self.reconnect_delay = reconnect_delay
        self.max_reconnect_delay = max_reconnect_delay
        self.factory = factory
        self.clock = clock
        self.buffer = FrameBuffer()
        self.capture = None
        self.connected = False
        self.frames = 0
        self.reconnects = 0
        self.last_error = ""
        self.retry_delay = reconnect_delay
        self.retry_at = 0.0
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def connect(self) -> None:
        """Apertura sincrona y de fallo rapido, para que quien llama decida sin imagen."""

        capture = open_capture(self.source, self.open_timeout, self.factory)
        if not capture.isOpened():
            capture.release()
            raise CameraError(f"No se pudo abrir la camara {self.source!r}.")
        capture.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
        capture.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
        capture.set(cv2.CAP_PROP_FPS, self.fps)
        self.capture = capture
        self.connected = True
        self.last_error = ""
        self.retry_delay = self.reconnect_delay
        self.retry_at = 0.0

    def poll(self) -> None:
        """Una iteracion del bucle: reabrir si toca, leer y publicar. No duerme."""

        if self._stop.is_set():
            return
        now = self.clock()
        capture = self.capture
        if capture is None:
            if now >= self.retry_at:
                self._reopen(now)
            return
        ok, frame = capture.read()
        if ok and frame is not None and getattr(frame, "size", 0):
            self.buffer.put(frame, now)
            self.frames += 1
            return
        capture.release()
        if self._stop.is_set():
            return
        if self.capture is capture:
            self.capture = None
        self.connected = False
        self.reconnects += 1
        self.last_error = "la camara dejo de entregar imagen"
        self.retry_delay = self.reconnect_delay
        self.retry_at = now + self.retry_delay

    def _reopen(self, now: float) -> None:
        try:
            self.connect()
        except CameraError as error:
            self.connected = False
            self.last_error = str(error)
            self.retry_at = now + self.retry_delay
            self.retry_delay = min(self.retry_delay * 2, self.max_reconnect_delay)

    def status(self) -> SourceStatus:
        now = self.clock()
        if self.connected:
            message = "conectada"
        else:
            message = f"sin senal: {self.last_error}" if self.last_error else "sin senal"
        return SourceStatus(self.connected, self.frames, self.reconnects, self.buffer.age(now), message)

    def start(self) -> None:
        if self._thread is not None:
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, name="FootTrackerCamara", daemon=True)
        self._thread.start()

    def _run(self) -> None:
        while not self._stop.is_set():
            self.poll()
            if self.capture is None:
                wait = max(min(self.retry_at - self.clock(), 0.25), 0.01)
            else:
                wait = 0.002
            self._stop.wait(wait)

    def stop(self, timeout: float = 2.0) -> None:
        """Parada acotada: si un read de FFMPEG esta atascado, no se espera indefinidamente."""

        self._stop.set()
        thread, self._thread = self._thread, None
        if thread is not None:
            thread.join(timeout)
        if self.capture is not None:
            self.capture.release()
            self.capture = None
        self.connected = False


@dataclass
class CameraSource:
    """`source` es un indice de webcam (int) o una URL de red (str)."""

    source: int | str = 0
    width: int = 640
    height: int = 480
    fps: int = 30
    open_timeout: float = 5.0
    stale_seconds: float = 0.3
    reconnect_delay: float = 0.5
    max_reconnect_delay: float = 5.0
    factory: CaptureFactory = default_factory
    clock: Clock = time.perf_counter
    _reader: FrameReader | None = field(default=None, repr=False)

    def open(self) -> None:
        if self._reader is not None:
            return
        reader = FrameReader(
            self.source,
            width=self.width,
            height=self.height,
            fps=self.fps,
            open_timeout=self.open_timeout,
            stale_seconds=self.stale_seconds,
            reconnect_delay=self.reconnect_delay,
            max_reconnect_delay=self.max_reconnect_delay,
            factory=self.factory,
            clock=self.clock,
        )
        reader.connect()
        reader.start()
        self._reader = reader

    def read(self) -> np.ndarray | None:
        if self._reader is None:
            raise CameraError("La camara no esta abierta.")
        return self._reader.buffer.take(self.clock(), self.stale_seconds)

    def close(self) -> None:
        reader, self._reader = self._reader, None
        if reader is not None:
            reader.stop()

    def status(self) -> SourceStatus:
        if self._reader is None:
            return SourceStatus(False, 0, 0, None, "cerrada")
        return self._reader.status()

    def describe(self) -> str:
        kind = "wifi" if is_network(self.source) else "webcam"
        base = f"{kind} {self.source!r} {self.width}x{self.height}@{self.fps}"
        if self._reader is None:
            return base
        return f"{base} [{self.status().message}]"


def usb_webcam(index: int = 0, width: int = 640, height: int = 480, fps: int = 30) -> CameraSource:
    """El celular puede presentarse a Windows como webcam USB (indice)."""

    return CameraSource(int(index), width, height, fps)


def phone_camera(url: str, width: int = 640, height: int = 480, fps: int = 30) -> CameraSource:
    """Alternativa por Wi-Fi: mas latencia y mas puntos de fallo que USB."""

    return CameraSource(url, width, height, fps)


def parse_camera(value: str | int) -> int | str:
    if isinstance(value, int) and not isinstance(value, bool):
        return int(value)
    text = value.strip()
    if text.isdigit():
        return int(text)
    if not text:
        raise CameraError("La camara no puede estar vacia.")
    return text


def camera(
    value: str,
    width: int = 640,
    height: int = 480,
    fps: int = 30,
    *,
    open_timeout: float = 5.0,
    stale_seconds: float = 0.3,
    reconnect_delay: float = 0.5,
    max_reconnect_delay: float = 5.0,
    factory: CaptureFactory = default_factory,
    clock: Clock = time.perf_counter,
) -> CameraSource:
    return CameraSource(
        source=parse_camera(value),
        width=width,
        height=height,
        fps=fps,
        open_timeout=open_timeout,
        stale_seconds=stale_seconds,
        reconnect_delay=reconnect_delay,
        max_reconnect_delay=max_reconnect_delay,
        factory=factory,
        clock=clock,
    )
