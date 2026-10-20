import numpy as np

from ml.eval import metrics
from ml.perception_service.colour import classify_patch, srgb_to_lab


def test_ece_is_zero_when_calibrated():
    assert metrics.ece([1.0, 1.0, 0.0001], [True, True, False]) < 1e-3


def test_confusion_counts_no_wire_in_the_last_bin():
    m = metrics.confusion([0, -1, 3], [0, -1, 2])
    assert m[0, 0] == 1 and m[8, 8] == 1 and m[3, 2] == 1


def test_lab_of_white_is_100():
    assert abs(srgb_to_lab(np.array([255, 255, 255]))[0] - 100) < 0.1


def test_pure_red_patch_is_red():
    colour, conf, p = classify_patch(np.tile(np.array([205, 30, 35], np.uint8), (10, 10, 1)))
    assert colour == 0 and abs(p.sum() - 1) < 1e-9 and 0 < conf <= 1


def test_holds_wires():
    assert metrics.holds_wires([-1, -1, 3]) and not metrics.holds_wires([-1, -1])
