import pytest

from foottracker.classifier import ThresholdMode
from foottracker.cli import build_classifier, build_parser
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
    assert "red" in phone_camera("http://192.168.0.5:8080/video").describe()
    assert camera("2", 1280, 720, 60).width == 1280
