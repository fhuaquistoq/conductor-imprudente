"""Analisis por lotes de una carpeta de fotos, sin camara.

Las fotos se generan en un directorio temporal: un pie rojo (freno) quieto y un pie
verde (acelerador) que empieza apoyado y luego se levanta.
"""

from __future__ import annotations

import io

import cv2
import numpy as np
import pytest

from foottracker.cli import build_parser, photos_mode
from foottracker.photos import analyze_folder, image_paths, rest_reference


def blank():
    return np.zeros((480, 640, 3), np.uint8)


def marker(frame, cx, cy, side, color):
    half = side // 2
    cv2.rectangle(frame, (cx - half, cy - half), (cx + half, cy + half), color, -1)
    return frame


def write_sequence(folder, frames=6, lift_at=4):
    for index in range(1, frames + 1):
        frame = blank()
        marker(frame, 200, 330, 80, (0, 0, 255))
        marker(frame, 430, 330 if index < lift_at else 270, 80, (0, 255, 0))
        cv2.imwrite(str(folder / f"foto_{index:04d}.png"), frame)


def test_image_paths_orders_by_number(tmp_path):
    for name in ["foto_0010.png", "foto_0002.png", "nota.png"]:
        (tmp_path / name).write_bytes(b"")
    assert [path.name for path in image_paths(tmp_path)] == ["foto_0002.png", "foto_0010.png", "nota.png"]


def test_analyze_folder_follows_both_markers(tmp_path):
    write_sequence(tmp_path, frames=6, lift_at=4)
    report = analyze_folder(tmp_path, debounce_frames=1)

    assert len(report.rows) == 6
    assert [row.red.state.value for row in report.rows] == ["down"] * 6
    assert [row.green.state.value for row in report.rows] == ["down", "down", "down", "up", "up", "up"]
    assert report.red_floor_y == pytest.approx(330, abs=6)
    assert report.green_floor_y == pytest.approx(330, abs=6)
    assert report.rows[-1].green.height_cm == pytest.approx(4.5, abs=0.6)


def test_analyze_folder_reports_unknown_without_markers(tmp_path):
    for index in range(1, 4):
        cv2.imwrite(str(tmp_path / f"foto_{index:04d}.png"), blank())
    report = analyze_folder(tmp_path, debounce_frames=1)
    assert report.red_floor_y is None
    assert all(not row.red.valid for row in report.rows)
    assert "sin datos" in "\n".join(report.lines())


def test_report_counts_and_lines(tmp_path):
    write_sequence(tmp_path, frames=4, lift_at=3)
    report = analyze_folder(tmp_path, debounce_frames=1)
    red = report.counts("red")
    green = report.counts("green")
    assert red == {"down": 4, "up": 0, "unknown": 0}
    assert green == {"down": 2, "up": 2, "unknown": 0}
    text = "\n".join(report.lines())
    assert "foto_0001.png" in text
    assert "resumen: rojo 4 pisado" in text


def test_rest_reference_uses_the_lowest_position(tmp_path):
    from foottracker.classifier import MarkerSample

    samples = [MarkerSample(True, y, 3600.0, 1.0, "ok", 100.0) for y in (330, 332, 328, 270, 260)]
    floor_y, side_px = rest_reference(samples)
    assert floor_y == pytest.approx(330, abs=4)
    assert side_px == pytest.approx(60, abs=1)


def test_photos_mode_prints_the_report(tmp_path):
    write_sequence(tmp_path, frames=4, lift_at=3)
    stdout = io.StringIO()
    assert photos_mode(build_parser().parse_args(["--photos", str(tmp_path), "--debounce", "1"]), stdout) == 0
    text = stdout.getvalue()
    assert "foto_0001.png" in text
    assert "resumen" in text


def test_photos_mode_reports_a_missing_folder(tmp_path):
    stdout = io.StringIO()
    assert photos_mode(build_parser().parse_args(["--photos", str(tmp_path / "no-existe")]), stdout) == 2
