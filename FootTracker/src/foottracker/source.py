"""Fuentes de video: webcam USB o celular presentado como webcam (especificacion 44)."""

from __future__ import annotations

from dataclasses import dataclass

import cv2
import numpy as np


class CameraError(RuntimeError):
    """La camara no pudo abrirse o dejo de entregar imagen."""


@dataclass
class CameraSource:
    """`source` es un indice de webcam (int) o una URL de red (str)."""

    source: int | str = 0
    width: int = 640
    height: int = 480
    fps: int = 30
    _capture: cv2.VideoCapture | None = None

    def open(self) -> None:
        capture = cv2.VideoCapture(self.source)
        if not capture.isOpened():
            raise CameraError(f"No se pudo abrir la camara {self.source!r}.")
        capture.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
        capture.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
        capture.set(cv2.CAP_PROP_FPS, self.fps)
        self._capture = capture

    def read(self) -> np.ndarray | None:
        if self._capture is None:
            raise CameraError("La camara no esta abierta.")
        ok, frame = self._capture.read()
        return frame if ok else None

    def close(self) -> None:
        if self._capture is not None:
            self._capture.release()
            self._capture = None

    def describe(self) -> str:
        kind = "webcam" if isinstance(self.source, int) else "red"
        return f"{kind} {self.source!r} {self.width}x{self.height}@{self.fps}"


def usb_webcam(index: int = 0, width: int = 640, height: int = 480, fps: int = 30) -> CameraSource:
    """El celular puede presentarse a Windows como webcam USB (indice)."""

    return CameraSource(int(index), width, height, fps)


def phone_camera(url: str, width: int = 640, height: int = 480, fps: int = 30) -> CameraSource:
    """Alternativa por Wi-Fi: mas latencia y mas puntos de fallo que USB."""

    return CameraSource(url, width, height, fps)


def parse_camera(value: str) -> int | str:
    text = value.strip()
    if text.isdigit():
        return int(text)
    if not text:
        raise CameraError("La camara no puede estar vacia.")
    return text


def camera(value: str, width: int = 640, height: int = 480, fps: int = 30) -> CameraSource:
    parsed = parse_camera(value)
    return CameraSource(parsed, width, height, fps)
