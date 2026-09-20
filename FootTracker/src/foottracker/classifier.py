"""Clasificacion de los pedales por color (especificacion 34-37).

El pipeline es: imagen -> HSV -> mascara -> morfologia -> contornos -> validacion de
forma -> candidatos -> el mas cercano a la posicion anterior -> arriba/abajo.

El pie **rojo/rosado frena** y el pie **verde o azul acelera**. Las mascaras llevan
margen de color (saturacion y valor bajos) para tolerar luz y marcadores apagados, y se
elige el candidato mas cercano a donde estaba el marcador en el frame anterior para no
engancharse a objetos del fondo. `Unknown` nunca equivale a `Up` ni a `Down`.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from math import hypot
from statistics import median

import cv2
import numpy as np

from .protocol import FootState


class ThresholdMode(str, Enum):
    """El umbral de levantamiento puede darse en pixeles o relativo al marcador."""

    PIXELS = "pixels"
    RELATIVE = "relative"


@dataclass(frozen=True)
class MarkerSettings:
    name: str
    hue_low: int
    hue_high: int
    hue_wrap_low: int | None = None
    hue_wrap_high: int | None = None
    saturation_min: int = 90
    value_min: int = 55
    min_area_ratio: float = 0.0015
    morphology: int = 5
    rectangularity_min: float = 0.62
    aspect_min: float = 0.35
    epsilon_ratio: float = 0.035
    max_vertices: int = 5


# Freno: rojo/rosado. El marcador de las fotos mide H~178 S~75 V~225; 158..179 y 0..22 deja
# margen a los rosas claros, y el valor minimo alto descarta la madera (V~120) y las sombras.
RED = MarkerSettings("red", 0, 22, hue_wrap_low=158, hue_wrap_high=179, saturation_min=40, value_min=140)
# Acelerador: verde o azul. H~85 S~40 V~200 en las fotos; 35..140 cubre del verde al azul.
GREEN = MarkerSettings("green", 35, 140, saturation_min=25, value_min=140)
BRAKE = RED
ACCELERATOR = GREEN


@dataclass(frozen=True)
class MarkerSample:
    valid: bool
    centroid_y: float | None
    area: float
    confidence: float
    reason: str
    centroid_x: float | None = None

    @classmethod
    def invalid(cls, reason: str) -> "MarkerSample":
        return cls(False, None, 0.0, 0.0, reason)


@dataclass(frozen=True)
class MarkerResult:
    state: FootState
    valid: bool
    height_cm: float | None = None
    delta_cm: float | None = None


def mask_of(hsv: np.ndarray, settings: MarkerSettings) -> np.ndarray:
    low = np.array([settings.hue_low, settings.saturation_min, settings.value_min])
    high = np.array([settings.hue_high, 255, 255])
    mask = cv2.inRange(hsv, low, high)
    if settings.hue_wrap_low is not None and settings.hue_wrap_high is not None:
        wrap_low = np.array([settings.hue_wrap_low, settings.saturation_min, settings.value_min])
        wrap_high = np.array([settings.hue_wrap_high, 255, 255])
        mask = cv2.bitwise_or(mask, cv2.inRange(hsv, wrap_low, wrap_high))
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (settings.morphology, settings.morphology))
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    return mask


def rectangle_confidence(contour: np.ndarray, settings: MarkerSettings) -> float:
    """Confianza 0..1. Una forma que no es un cuadrilatero razonable da 0.

    Se admiten hasta `max_vertices` lados porque un papel pegado al zapato, visto de
    refilon, pierde una esquina y redondea el contorno; exigir cuatro exactos dejaba fuera
    marcadores validos de las fotos reales.
    """

    perimeter = cv2.arcLength(contour, True)
    if perimeter <= 0:
        return 0.0
    approx = cv2.approxPolyDP(contour, settings.epsilon_ratio * perimeter, True)
    if not 4 <= len(approx) <= settings.max_vertices:
        return 0.0
    if not cv2.isContourConvex(approx):
        return 0.0
    area = cv2.contourArea(approx)
    if area <= 0:
        return 0.0
    _, (width, height), _ = cv2.minAreaRect(approx)
    if width <= 0 or height <= 0:
        return 0.0
    rectangularity = area / (width * height)
    aspect = min(width, height) / max(width, height)
    if rectangularity < settings.rectangularity_min:
        return 0.0
    if not settings.aspect_min <= aspect <= 1.0:
        return 0.0
    return float(rectangularity)


def candidates(frame_bgr: np.ndarray | None, settings: MarkerSettings) -> list[MarkerSample]:
    """Todos los blobs con color y forma validos, de mayor a menor area."""

    if frame_bgr is None or frame_bgr.size == 0:
        return []
    hsv = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2HSV)
    mask = mask_of(hsv, settings)
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    frame_area = float(frame_bgr.shape[0] * frame_bgr.shape[1])
    found = []
    for contour in contours:
        area = float(cv2.contourArea(contour))
        if area < frame_area * settings.min_area_ratio:
            continue
        confidence = rectangle_confidence(contour, settings)
        if confidence <= 0:
            continue
        moments = cv2.moments(contour)
        if moments["m00"] <= 0:
            continue
        found.append(MarkerSample(True, float(moments["m01"] / moments["m00"]), area, confidence, "ok", float(moments["m10"] / moments["m00"])))
    found.sort(key=lambda item: item.area, reverse=True)
    return found


def evaluate(frame_bgr: np.ndarray | None, settings: MarkerSettings) -> MarkerSample:
    """Mejor candidato (el de mayor area); `invalid` si no hay ninguno."""

    found = candidates(frame_bgr, settings)
    return found[0] if found else MarkerSample.invalid("sin marcador")


def _distance(item: MarkerSample, point: tuple[float, float]) -> float:
    if item.centroid_x is None or item.centroid_y is None:
        return float("inf")
    return hypot(item.centroid_x - point[0], item.centroid_y - point[1])


def select_candidate(found: list[MarkerSample], previous: tuple[float, float] | None = None, max_jump_px: float = 200.0) -> MarkerSample:
    """El candidato mas cercano a la posicion anterior; si no hay, el mas grande.

    Recordar donde estaba el marcador es lo que evita saltar al taburete azul o a la
    estanteria. Si ningun candidato cae cerca del anterior, el marcador se da por perdido
    (`invalid`) en vez de saltar al blob mas grande, que suele ser un mueble del fondo.
    """

    if not found:
        return MarkerSample.invalid("sin marcador")
    if previous is None:
        return found[0]
    nearest = min(found, key=lambda item: _distance(item, previous))
    return nearest if _distance(nearest, previous) <= max_jump_px else MarkerSample.invalid("marcador perdido")


@dataclass
class MarkerTracker:
    """Calibracion del suelo, umbral de levantamiento, seguimiento y debounce temporal."""

    settings: MarkerSettings
    threshold_mode: ThresholdMode = ThresholdMode.PIXELS
    lift_threshold: float = 18.0
    relative_lift_ratio: float = 0.35
    release_ratio: float = 0.5
    debounce_frames: int = 3
    calibration_seconds: float = 0.75
    start_down_y: float | None = None
    marker_cm: float = 6.0
    ref_side_px: float | None = None
    max_jump_px: float = 200.0
    relock_after: int = 4

    state: FootState = FootState.UNKNOWN
    down_y: float | None = None
    calibrated: bool = False
    previous: tuple[float, float] | None = None
    _elapsed: float = 0.0
    _samples: list[float] = field(default_factory=list)
    _last_y: float | None = None
    _misses: int = 0
    _candidate: FootState = FootState.UNKNOWN
    _count: int = 0

    def __post_init__(self) -> None:
        # Un perfil de calibracion ya trae la linea del suelo, asi que no hace falta esperar
        # a los 0,75 s en vivo con los pies apoyados.
        if self.start_down_y is not None:
            self.down_y = float(self.start_down_y)
            self.calibrated = True

    def threshold_for(self, sample: MarkerSample) -> float:
        if self.threshold_mode == ThresholdMode.RELATIVE:
            return self.relative_lift_ratio * float(np.sqrt(max(sample.area, 1.0)))
        return self.lift_threshold

    @property
    def cm_per_px(self) -> float | None:
        """Escala derivada del lado real del marcador y de su tamano en la foto de suelo."""

        if self.ref_side_px is None or self.ref_side_px <= 0:
            return None
        return self.marker_cm / float(self.ref_side_px)

    def height_cm(self, sample: MarkerSample) -> float | None:
        """Altura del pie sobre el suelo, o None si falta la escala o la linea del suelo."""

        scale = self.cm_per_px
        if scale is None or self.down_y is None or not sample.valid or sample.centroid_y is None:
            return None
        return max(self.down_y - float(sample.centroid_y), 0.0) * scale

    def move_cm(self, sample: MarkerSample) -> float | None:
        """Cuanto se movio el marcador desde el frame anterior (positivo = baja, pisa)."""

        if not sample.valid or sample.centroid_y is None:
            return None
        change = None if self._last_y is None else float(sample.centroid_y) - self._last_y
        self._last_y = float(sample.centroid_y)
        scale = self.cm_per_px
        if change is None or scale is None:
            return None
        return change * scale

    def locate(self, frame_bgr: np.ndarray | None, delta_seconds: float) -> MarkerResult:
        """Detecta, sigue el marcador desde su posicion anterior y clasifica.

        Tras varios frames sin encontrarlo cerca del sitio anterior, se olvida la referencia
        para poder reenganchar el marcador donde haya reaparecido.
        """

        sample = select_candidate(candidates(frame_bgr, self.settings), self.previous, self.max_jump_px)
        if sample.valid:
            self.previous = (sample.centroid_x, sample.centroid_y)
            self._misses = 0
        else:
            self._misses += 1
            if self._misses >= self.relock_after:
                self.previous = None
                self._misses = 0
        return self.observe(sample, delta_seconds)

    def observe(self, sample: MarkerSample, delta_seconds: float) -> MarkerResult:
        if not self.calibrated:
            self._elapsed += max(delta_seconds, 0.0)
            if sample.valid:
                self._samples.append(float(sample.centroid_y))
            if self._elapsed >= self.calibration_seconds:
                self.calibrated = True
                self.down_y = float(median(self._samples)) if self._samples else None
            if self.down_y is None and not self._samples:
                return MarkerResult(FootState.UNKNOWN, False)
            return MarkerResult(FootState.DOWN, sample.valid)

        if not sample.valid or self.down_y is None:
            return MarkerResult(FootState.UNKNOWN, False)

        threshold = self.threshold_for(sample)
        delta = self.down_y - float(sample.centroid_y)
        if delta > threshold:
            raw = FootState.UP
        elif delta < threshold * self.release_ratio:
            raw = FootState.DOWN
        else:
            raw = self.state if self.state != FootState.UNKNOWN else FootState.DOWN
        self._debounce(raw)
        return MarkerResult(self.state, True, self.height_cm(sample), self.move_cm(sample))

    def _debounce(self, raw: FootState) -> None:
        if raw == self.state:
            self._candidate = raw
            self._count = 0
            return
        if raw != self._candidate:
            self._candidate = raw
            self._count = 1
        else:
            self._count += 1
        if self._count >= self.debounce_frames:
            self.state = raw
            self._count = 0


@dataclass
class FootClassifier:
    """Ambos pedales. Rojo frena, verde o azul acelera."""

    red: MarkerTracker
    green: MarkerTracker

    @classmethod
    def default(cls, **options) -> "FootClassifier":
        return cls(MarkerTracker(RED, **options), MarkerTracker(GREEN, **options))

    def process(self, frame_bgr: np.ndarray | None, delta_seconds: float) -> tuple[MarkerResult, MarkerResult]:
        red = self.red.locate(frame_bgr, delta_seconds)
        green = self.green.locate(frame_bgr, delta_seconds)
        return red, green
