"""Calibracion por fotos: rango HSV, suelo, escala y perfil JSON.

Todo con imagenes sinteticas: no hace falta camara ni red.
"""

from __future__ import annotations

import numpy as np
import pytest
import cv2

from foottracker.calibration import (
    CalibrationProfile,
    ColorRange,
    SampleCollector,
    estimate_range,
    guide_rect,
    merge_ranges,
)
from foottracker.classifier import GREEN, RED, MarkerSample, evaluate
from foottracker.protocol import FootState


def hsv_patch(hue, sat=220, value=200, size=60):
    hsv = np.zeros((size, size, 3), np.uint8)
    hsv[15:45, 15:45] = (hue, sat, value)
    return cv2.cvtColor(hsv, cv2.COLOR_HSV2BGR)


def blank(width=640, height=480):
    return np.zeros((height, width, 3), np.uint8)


def sample(centroid_y, area=10000.0):
    return MarkerSample(True, centroid_y, area, 1.0, "ok")


def test_estimate_range_learns_the_dominant_hue():
    found = estimate_range(hsv_patch(60))
    assert found is not None
    assert found.hue_low <= 60 <= found.hue_high
    assert found.saturation_min <= 220
    assert found.value_min <= 200


def test_estimate_range_returns_none_without_color():
    assert estimate_range(blank(60, 60)) is None
    assert estimate_range(hsv_patch(60, sat=5)) is None


def test_estimate_range_splits_a_red_that_crosses_zero():
    found = estimate_range(hsv_patch(2))
    assert found is not None
    assert found.hue_wrap_low is not None and found.hue_wrap_high is not None


def test_estimate_range_keeps_red_near_the_top_of_the_circle():
    found = estimate_range(hsv_patch(178))
    assert found is not None
    assert found.hue_low <= 178 <= found.hue_high


def test_merge_ranges_covers_both_samples():
    merged = merge_ranges([estimate_range(hsv_patch(50)), estimate_range(hsv_patch(75))])
    assert merged is not None
    assert merged.hue_low <= 50 and merged.hue_high >= 75


def test_merge_ranges_uses_the_most_permissive_floor():
    dim = estimate_range(hsv_patch(60, sat=70, value=90))
    bright = estimate_range(hsv_patch(60, sat=220, value=230))
    merged = merge_ranges([dim, bright])
    assert merged.saturation_min <= dim.saturation_min
    assert merged.value_min <= dim.value_min


def test_apply_overrides_only_the_color_fields():
    adjusted = ColorRange(0, 5, 175, 179, 90, 60).apply(RED)
    assert (adjusted.hue_low, adjusted.hue_high) == (0, 5)
    assert (adjusted.hue_wrap_low, adjusted.hue_wrap_high) == (175, 179)
    assert (adjusted.saturation_min, adjusted.value_min) == (90, 60)
    assert adjusted.min_area_ratio == RED.min_area_ratio
    assert adjusted.rectangularity_min == RED.rectangularity_min


def test_guide_rect_is_centered_and_square():
    assert guide_rect((480, 640)) == (200, 120, 440, 360)


def test_collector_learns_color_and_floor_position():
    frame = blank()
    cv2.rectangle(frame, (270, 250), (370, 350), (0, 0, 255), -1)
    collector = SampleCollector(marker_cm=6.0)
    captured = collector.add("red", frame)
    assert captured is not None
    assert abs(captured.floor_y - 300) < 5
    assert abs(captured.side_px - 100) < 5

    profile = collector.build()
    assert profile is not None
    assert profile.red is not None
    floor_y, side_px = profile.floor_for("red")
    assert abs(floor_y - 300) < 5
    assert abs(side_px - 100) < 5
    assert profile.floor_for("green") == (None, None)


def test_collector_rejects_a_photo_without_color():
    assert SampleCollector().add("red", blank()) is None
    assert SampleCollector().build() is None


def test_collector_ignores_unknown_markers():
    with pytest.raises(ValueError):
        SampleCollector().add("blue", blank())


def test_collector_rejects_the_wrong_marker_family():
    green_frame = blank()
    cv2.rectangle(green_frame, (270, 190), (370, 290), (0, 255, 0), -1)
    collector = SampleCollector()
    assert collector.add("red", green_frame) is None, "pulsar R con el verde delante no debe aprenderlo"
    assert collector.add("green", green_frame) is not None


def test_profile_round_trips_through_json(tmp_path):
    red_frame = blank()
    cv2.rectangle(red_frame, (270, 250), (370, 350), (0, 0, 255), -1)
    green_frame = blank()
    cv2.rectangle(green_frame, (270, 250), (370, 350), (0, 255, 0), -1)
    collector = SampleCollector(marker_cm=7.0)
    collector.add("red", red_frame)
    collector.add("green", green_frame)
    profile = collector.build()

    target = profile.save(tmp_path / "calibration.json")
    restored = CalibrationProfile.load(target)
    assert restored.marker_cm == 7.0
    assert restored.red is not None and restored.green is not None
    assert restored.red.floor_y == pytest.approx(profile.red.floor_y)
    assert restored.green.color_range == profile.green.color_range


def test_profile_rejects_a_non_positive_marker_size():
    with pytest.raises(ValueError):
        CalibrationProfile.from_dict({"markerCm": 0})


def test_calibrated_color_still_matches_the_marker():
    frame = blank()
    cv2.rectangle(frame, (270, 250), (370, 350), (0, 0, 255), -1)
    collector = SampleCollector()
    collector.add("red", frame)
    settings = collector.build().settings_for("red", RED)
    assert evaluate(frame, settings).valid
    assert not evaluate(frame, GREEN).valid
