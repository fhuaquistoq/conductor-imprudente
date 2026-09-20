import cv2
import numpy as np
import pytest

from foottracker.classifier import (
    GREEN,
    RED,
    FootClassifier,
    MarkerSample,
    MarkerTracker,
    ThresholdMode,
    candidates,
    evaluate,
    mask_of,
    rectangle_confidence,
    select_candidate,
)
from foottracker.protocol import FootState


def blank(width=640, height=480):
    return np.zeros((height, width, 3), np.uint8)


def square(image, cx, cy, side, color=(0, 0, 255)):
    half = side // 2
    cv2.rectangle(image, (cx - half, cy - half), (cx + half, cy + half), color, -1)
    return image


def sample(centroid_y, area=6400.0):
    return MarkerSample(True, centroid_y, area, 1.0, "ok")


def calibrated(tracker):
    for _ in range(40):
        tracker.observe(sample(400.0), 1 / 30)
    assert tracker.calibrated
    return tracker


def test_red_square_is_valid_with_centroid():
    found = evaluate(square(blank(), 320, 300, 120), RED)
    assert found.valid
    assert abs(found.centroid_y - 300) < 6
    assert found.confidence > 0.7


def test_green_settings_do_not_match_red():
    assert not evaluate(square(blank(), 320, 300, 120), GREEN).valid
    assert evaluate(square(blank(), 320, 300, 120, (0, 255, 0)), GREEN).valid


def test_empty_frame_is_invalid():
    assert not evaluate(blank(), RED).valid
    assert not evaluate(None, RED).valid


def hsv_square(hue, sat=200, val=200, cx=320, cy=300, side=120):
    hsv = np.zeros((480, 640, 3), np.uint8)
    half = side // 2
    hsv[cy - half:cy + half, cx - half:cx + half] = (hue, sat, val)
    return cv2.cvtColor(hsv, cv2.COLOR_HSV2BGR)


def test_blue_marker_counts_as_accelerator():
    blue = hsv_square(110)
    assert evaluate(blue, GREEN).valid, "el acelerador puede ser azul"
    assert not evaluate(blue, RED).valid


def test_pastel_marker_with_little_saturation_is_detected():
    pastel = hsv_square(85, sat=45, val=200)
    assert evaluate(pastel, GREEN).valid, "el marcador verde-agua de las fotos tiene poca saturacion"
    pale_red = hsv_square(178, sat=70, val=220)
    assert evaluate(pale_red, RED).valid, "el freno rosado tambien tiene poca saturacion"


def test_candidates_are_sorted_by_area():
    frame = square(square(blank(), 200, 300, 140), 480, 300, 70)
    found = candidates(frame, RED)
    assert len(found) == 2
    assert found[0].area > found[1].area
    assert found[0].centroid_x == pytest.approx(200, abs=6)


def test_select_candidate_keeps_the_nearest_to_previous():
    near = MarkerSample(True, 102.0, 500.0, 1.0, "ok", 12.0)
    far = MarkerSample(True, 400.0, 900.0, 1.0, "ok", 600.0)
    assert select_candidate([far, near], previous=(10.0, 100.0)) is near
    assert select_candidate([far, near], previous=(600.0, 400.0)) is far


def test_select_candidate_without_previous_takes_the_largest():
    small = MarkerSample(True, 100.0, 100.0, 1.0, "ok", 10.0)
    big = MarkerSample(True, 400.0, 900.0, 1.0, "ok", 600.0)
    assert select_candidate([big, small]) is big


def test_select_candidate_gives_up_when_nothing_is_near():
    big = MarkerSample(True, 400.0, 900.0, 1.0, "ok", 600.0)
    assert not select_candidate([big], previous=(10.0, 10.0)).valid, "no debe saltar a un mueble del fondo"
    assert not select_candidate([]).valid


def test_red_hue_wraparound_is_matched():
    hsv = np.zeros((480, 640, 3), np.uint8)
    hsv[240:360, 260:380] = (175, 255, 255)
    found = evaluate(cv2.cvtColor(hsv, cv2.COLOR_HSV2BGR), RED)
    assert found.valid
    assert abs(found.centroid_y - 300) < 6


def test_shape_without_four_vertices_is_rejected():
    cross = blank()
    cv2.fillPoly(cross, [np.array([[260, 240], [380, 240], [380, 290], [340, 290], [340, 360], [300, 360], [300, 290], [260, 290]])], (0, 0, 255))
    assert not evaluate(cross, RED).valid


def test_thin_marker_is_rejected_as_wrong_orientation():
    thin = blank()
    cv2.rectangle(thin, (160, 250), (480, 350), (0, 0, 255), -1)
    assert not evaluate(thin, RED).valid


def test_rotated_square_is_accepted_because_perspective_deforms_rectangles():
    rotated = blank()
    box = cv2.boxPoints(((320, 300), (140, 140), 35))
    cv2.fillPoly(rotated, [np.intp(box)], (0, 0, 255))
    assert evaluate(rotated, RED).valid


def test_tiny_marker_is_rejected_by_area():
    assert not evaluate(square(blank(), 320, 300, 12), RED).valid


def test_masks_clean_speckles():
    noisy = square(blank(), 320, 300, 120)
    noisy[10:14, 10:14] = (0, 0, 255)
    mask = mask_of(cv2.cvtColor(noisy, cv2.COLOR_BGR2HSV), RED)
    assert cv2.countNonZero(mask) < 120 * 120 * 2


def test_rectangle_confidence_is_zero_for_a_circle():
    circle = blank()
    cv2.circle(circle, (320, 300), 70, (0, 0, 255), -1)
    contours, _ = cv2.findContours(mask_of(cv2.cvtColor(circle, cv2.COLOR_BGR2HSV), RED), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    assert rectangle_confidence(max(contours, key=cv2.contourArea), RED) == 0.0


def test_starts_down_while_calibrating_with_feet_on_the_floor():
    tracker = MarkerTracker(GREEN, calibration_seconds=0.5)
    result = tracker.observe(sample(400.0), 1 / 30)
    assert result.state is FootState.DOWN
    assert not tracker.calibrated


def test_calibration_uses_median_of_resting_position():
    tracker = MarkerTracker(GREEN, calibration_seconds=0.5)
    for offset in (398.0, 402.0, 400.0, 401.0):
        tracker.observe(sample(offset), 1 / 30)
    for _ in range(20):
        tracker.observe(sample(400.0), 1 / 30)
    assert tracker.calibrated
    assert abs(tracker.down_y - 400.0) < 2


def test_lift_above_threshold_becomes_up_after_debounce():
    tracker = calibrated(MarkerTracker(GREEN, lift_threshold=18, debounce_frames=3))
    results = [tracker.observe(sample(360.0), 1 / 30) for _ in range(3)]
    assert results[-1].state is FootState.UP
    assert results[-1].valid


def test_small_lift_below_threshold_stays_down():
    tracker = calibrated(MarkerTracker(GREEN, lift_threshold=18, debounce_frames=3))
    for _ in range(10):
        assert tracker.observe(sample(390.0), 1 / 30).state is FootState.DOWN


def test_lost_marker_is_unknown_and_not_down():
    tracker = calibrated(MarkerTracker(GREEN))
    result = tracker.observe(MarkerSample.invalid("sin color"), 1 / 30)
    assert result.state is FootState.UNKNOWN
    assert not result.valid


def test_relative_threshold_scales_with_marker_size():
    tracker = calibrated(MarkerTracker(GREEN, threshold_mode=ThresholdMode.RELATIVE, relative_lift_ratio=0.35, debounce_frames=1))
    assert tracker.observe(sample(370.0, area=10000.0), 1 / 30).state is FootState.DOWN
    assert tracker.observe(sample(330.0, area=10000.0), 1 / 30).state is FootState.UP


def test_debounce_ignores_brief_flicker():
    tracker = calibrated(MarkerTracker(GREEN, lift_threshold=18, debounce_frames=3))
    tracker.observe(sample(360.0), 1 / 30)
    tracker.observe(sample(360.0), 1 / 30)
    for _ in range(4):
        assert tracker.observe(sample(400.0), 1 / 30).state is FootState.DOWN


def test_profile_floor_starts_calibrated_and_reports_height():
    tracker = MarkerTracker(GREEN, start_down_y=400.0, marker_cm=6.0, ref_side_px=100.0, lift_threshold=18, debounce_frames=1)
    assert tracker.calibrated, "la linea del suelo del perfil evita la calibracion en vivo"
    assert tracker.cm_per_px == pytest.approx(0.06)
    result = tracker.observe(sample(350.0), 1 / 30)
    assert result.state is FootState.UP
    assert result.height_cm == pytest.approx(3.0)


def test_height_never_goes_negative_at_rest():
    tracker = MarkerTracker(GREEN, start_down_y=400.0, marker_cm=6.0, ref_side_px=100.0)
    assert tracker.observe(sample(410.0), 1 / 30).height_cm == 0.0


def test_height_is_none_without_a_known_marker_side():
    tracker = calibrated(MarkerTracker(GREEN))
    assert tracker.observe(sample(360.0), 1 / 30).height_cm is None


def test_lost_marker_reports_no_height():
    tracker = MarkerTracker(GREEN, start_down_y=400.0, marker_cm=6.0, ref_side_px=100.0)
    result = tracker.observe(MarkerSample.invalid("sin color"), 1 / 30)
    assert result.state is FootState.UNKNOWN
    assert result.height_cm is None


def test_classifier_reports_red_and_green_independently():
    classifier = FootClassifier.default(lift_threshold=18, debounce_frames=3, calibration_seconds=0.4)
    resting = square(square(blank(), 160, 380, 80, (0, 0, 255)), 480, 380, 80, (0, 255, 0))
    for _ in range(20):
        red, green = classifier.process(resting, 1 / 30)
    assert red.state is FootState.DOWN and green.state is FootState.DOWN

    lifted = square(square(blank(), 160, 380, 80, (0, 0, 255)), 480, 320, 80, (0, 255, 0))
    for _ in range(4):
        red, green = classifier.process(lifted, 1 / 30)
    assert green.state is FootState.UP
    assert red.state is FootState.DOWN
