"""Clasificacion rojo/verde de los pedales (especificacion 34-37).

El pipeline es: imagen -> HSV -> mascara -> morfologia -> contornos ->
aproxPolyDP -> validacion de forma -> centroide -> arriba/abajo.
`Unknown` nunca equivale a `Up` ni a `Down`.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
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
    rectangularity_min: float = 0.70
    aspect_min: float = 0.35
    epsilon_ratio: float = 0.035


RED = MarkerSettings("red", 0, 10, hue_wrap_low=168, hue_wrap_high=179)
GREEN = MarkerSettings("green", 40, 85)


@dataclass(frozen=True)
class MarkerSample:
    valid: bool
    centroid_y: float | None
    area: float
    confidence: float
    reason: str

    @classmethod
    def invalid(cls, reason: str) -> "MarkerSample":
        return cls(False, None, 0.0, 0.0, reason)


@dataclass(frozen=True)
class MarkerResult:
    state: FootState
    valid: bool


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
    """Confianza 0..1. Un cuadrado deformado u orientado en exceso da 0."""

    perimeter = cv2.arcLength(contour, True)
    if perimeter <= 0:
        return 0.0
    approx = cv2.approxPolyDP(contour, settings.epsilon_ratio * perimeter, True)
    if len(approx) != 4:
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


def evaluate(frame_bgr: np.ndarray | None, settings: MarkerSettings) -> MarkerSample:
    if frame_bgr is None or frame_bgr.size == 0:
        return MarkerSample.invalid("sin imagen")
    hsv = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2HSV)
    mask = mask_of(hsv, settings)
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return MarkerSample.invalid("sin color")
    contour = max(contours, key=cv2.contourArea)
    area = float(cv2.contourArea(contour))
    frame_area = float(frame_bgr.shape[0] * frame_bgr.shape[1])
    if area < frame_area * settings.min_area_ratio:
        return MarkerSample.invalid("area insuficiente")
    confidence = rectangle_confidence(contour, settings)
    if confidence <= 0:
        return MarkerSample.invalid("forma no valida")
    moments = cv2.moments(contour)
    if moments["m00"] <= 0:
        return MarkerSample.invalid("contorno degenerado")
    return MarkerSample(True, float(moments["m01"] / moments["m00"]), area, confidence, "ok")


@dataclass
class MarkerTracker:
    """Calibracion del suelo, umbral de levantamiento y debounce temporal."""

    settings: MarkerSettings
    threshold_mode: ThresholdMode = ThresholdMode.PIXELS
    lift_threshold: float = 18.0
    relative_lift_ratio: float = 0.35
    release_ratio: float = 0.5
    debounce_frames: int = 3
    calibration_seconds: float = 0.75

    state: FootState = FootState.UNKNOWN
    down_y: float | None = None
    calibrated: bool = False
    _elapsed: float = 0.0
    _samples: list[float] = field(default_factory=list)
    _candidate: FootState = FootState.UNKNOWN
    _count: int = 0

    def threshold_for(self, sample: MarkerSample) -> float:
        if self.threshold_mode == ThresholdMode.RELATIVE:
            return self.relative_lift_ratio * float(np.sqrt(max(sample.area, 1.0)))
        return self.lift_threshold

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
        return MarkerResult(self.state, True)

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
    """Ambos pedales. Rojo frena, verde acelera."""

    red: MarkerTracker
    green: MarkerTracker

    @classmethod
    def default(cls, **options) -> "FootClassifier":
        return cls(MarkerTracker(RED, **options), MarkerTracker(GREEN, **options))

    def process(self, frame_bgr: np.ndarray | None, delta_seconds: float) -> tuple[MarkerResult, MarkerResult]:
        red = self.red.observe(evaluate(frame_bgr, self.red.settings), delta_seconds)
        green = self.green.observe(evaluate(frame_bgr, self.green.settings), delta_seconds)
        return red, green
