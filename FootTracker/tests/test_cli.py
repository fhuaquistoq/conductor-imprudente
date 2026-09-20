import io
import json

import pytest

import foottracker.cli as cli
from foottracker.calibration import CalibrationProfile, ColorRange, MarkerCalibration
from foottracker.classifier import MarkerResult, ThresholdMode
from foottracker.cli import build_classifier, build_parser, next_photo_index, status_json
from foottracker.probe import StreamProbe
from foottracker.protocol import FootState, packet
from foottracker.source import CameraError, camera, parse_camera, phone_camera, usb_webcam


def test_parser_defaults_match_unity_port_and_rate():
    args = build_parser().parse_args([])
    assert args.host == "127.0.0.1"
    assert args.port == 5055
    assert args.rate == 30
    assert args.camera == "0"
    assert args.threshold_mode == "pixels"


def test_parser_accepts_camera_url_and_flags():
    args = build_parser().parse_args(["--camera", "http://192.168.0.5:8080/video", "--preview", "--port", "6000", "--threshold-mode", "relative"])
    assert args.camera == "http://192.168.0.5:8080/video"
    assert args.preview and args.port == 6000
    assert args.threshold_mode == "relative"


def test_build_classifier_maps_threshold_options():
    args = build_parser().parse_args(["--threshold-mode", "relative", "--relative-lift", "0.5", "--debounce", "5", "--calibrate", "1.2"])
    classifier = build_classifier(args)
    assert classifier.red.threshold_mode is ThresholdMode.RELATIVE
    assert classifier.green.relative_lift_ratio == 0.5
    assert classifier.red.debounce_frames == 5
    assert classifier.green.calibration_seconds == 1.2


def test_build_classifier_uses_separate_marker_settings():
    classifier = build_classifier(build_parser().parse_args([]))
    assert classifier.red.settings.name == "red"
    assert classifier.green.settings.name == "green"


def test_parse_camera_distinguishes_index_from_url():
    assert parse_camera("1") == 1
    assert parse_camera("http://192.168.0.5:8080/video") == "http://192.168.0.5:8080/video"
    with pytest.raises(CameraError):
        parse_camera("   ")


def test_camera_helpers_describe_the_source():
    assert "webcam" in usb_webcam(0).describe()
    assert "wifi" in phone_camera("http://192.168.0.5:8080/video").describe()
    assert camera("2", 1280, 720, 60).width == 1280


def test_build_classifier_loads_the_profile_floor_and_color():
    profile = CalibrationProfile(
        red=MarkerCalibration(ColorRange(0, 5, 175, 179, 90, 60), 300.0, 100.0, 3),
        green=None,
        marker_cm=6.0,
    )
    classifier = build_classifier(build_parser().parse_args([]), profile)
    assert classifier.red.calibrated and classifier.red.down_y == 300.0
    assert (classifier.red.settings.hue_low, classifier.red.settings.hue_high) == (0, 5)
    assert classifier.red.cm_per_px == pytest.approx(0.06)
    assert not classifier.green.calibrated, "sin muestras verdes sigue la calibracion en vivo"


def test_status_json_adds_the_measured_heights():
    message = packet(7, FootState.UP, FootState.DOWN, True, True)
    payload = json.loads(status_json(message, MarkerResult(FootState.UP, True, 3.24), MarkerResult(FootState.DOWN, True, 0.0)))
    assert payload["red"] == "up" and payload["seq"] == 7
    assert payload["redHeightCm"] == 3.2
    assert payload["greenHeightCm"] == 0.0


def test_status_json_reports_null_without_scale():
    message = packet(1, FootState.UNKNOWN, FootState.UNKNOWN, False, False)
    payload = json.loads(status_json(message, MarkerResult(FootState.UNKNOWN, False), MarkerResult(FootState.UNKNOWN, False)))
    assert payload["redHeightCm"] is None and payload["greenHeightCm"] is None


def test_parser_has_no_photo_capture_by_default():
    assert build_parser().parse_args([]).capture_photos == ""


def test_next_photo_index_starts_at_one_and_skips_used_numbers(tmp_path):
    assert next_photo_index(tmp_path) == 1
    (tmp_path / "foto_0001.png").write_bytes(b"")
    (tmp_path / "foto_0003.png").write_bytes(b"")
    (tmp_path / "otra_0009.png").write_bytes(b"")
    (tmp_path / "foto_nota.png").write_bytes(b"")
    assert next_photo_index(tmp_path) == 4


def test_next_photo_index_accepts_a_custom_prefix(tmp_path):
    (tmp_path / "shot_0007.png").write_bytes(b"")
    assert next_photo_index(tmp_path, "shot") == 8


def test_capture_photos_reports_a_camera_failure(tmp_path, monkeypatch):
    def boom(args):
        raise CameraError("sin camara")

    monkeypatch.setattr(cli, "build_source", boom)
    args = build_parser().parse_args(["--capture-photos", str(tmp_path)])
    assert cli.capture_photos_mode(args, io.StringIO()) == 2


def test_parser_defaults_for_wifi_tuning():
    args = build_parser().parse_args([])
    assert args.camera_timeout == 5.0
    assert args.frame_stale == 0.3
    assert args.reconnect_delay == 0.5
    assert args.probe is False


def test_wifi_tuning_flags_reach_the_source():
    args = build_parser().parse_args(["--camera-timeout", "2", "--frame-stale", "0.1", "--reconnect-delay", "1"])
    source = cli.build_source(args)
    assert source.open_timeout == 2.0
    assert source.stale_seconds == 0.1
    assert source.reconnect_delay == 1.0
    assert source.source == 0


def test_probe_flag_reports_the_stream_and_exits_ok(monkeypatch):
    monkeypatch.setattr(cli, "probe_stream", lambda *args, **kwargs: StreamProbe(True, 640, 480, 30, 3.0, 30.0))
    stdout = io.StringIO()
    assert cli.main(["--probe", "--camera", "http://192.168.0.5:8080/video"], stdout=stdout) == 0
    assert "640x480" in stdout.getvalue()


def test_probe_flag_reports_failure_when_there_is_no_image(monkeypatch):
    monkeypatch.setattr(cli, "probe_stream", lambda *args, **kwargs: StreamProbe(False, error="no se pudo abrir"))
    stdout = io.StringIO()
    assert cli.main(["--probe"], stdout=stdout) == 2
    assert "no se pudo abrir" in stdout.getvalue()
