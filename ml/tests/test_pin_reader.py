"""pin_reader: features have a fixed length, temperature scaling keeps probabilities valid and only sharpens or
softens them, and a trained model (if present) answers one (colour, confidence) per patch."""
import numpy as np
import pytest

from ml.perception_service.pin_reader import FEATURE_SIZE, PinReader, calibrated, features


def test_features_have_a_fixed_length():
    w, h = FEATURE_SIZE["A1610"]
    for shape in [(20, 60, 3), (25, 75, 3)]:
        f = features(np.zeros(shape, np.uint8), (w, h))
        assert f.shape == (w * h * 3 + 9,)


def test_temperature_scaling_keeps_the_ranking():
    p = np.array([[0.7, 0.2, 0.1], [0.4, 0.35, 0.25]])
    for t in [0.5, 1.0, 2.0]:
        q = calibrated(p, t)
        assert np.allclose(q.sum(axis=1), 1.0)
        assert (q.argmax(axis=1) == p.argmax(axis=1)).all()
    assert calibrated(p, 0.5)[0, 0] > p[0, 0] > calibrated(p, 2.0)[0, 0]


def test_trained_model_reads_one_answer_per_patch():
    if not PinReader.available("A1610"):
        pytest.skip("no trained pin reader (python -m ml.eval.train_pins)")
    reads = PinReader("A1610").read([np.zeros((20, 60, 3), np.uint8)] * 3)
    assert len(reads) == 3 and all(0.0 <= conf <= 1.0 for _, conf in reads)
