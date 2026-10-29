"""pin_reader: features have a fixed length, augmented copies keep the window size and move the content as
asked, temperature scaling keeps probabilities valid and only sharpens or softens them, and a trained model (if
present) answers one (colour, confidence) per patch."""
import numpy as np
import pytest

from ml.perception_service.pin_reader import FEATURE_SIZE, PinReader, augmented, calibrated, features


def test_features_have_a_fixed_length():
    w, h = FEATURE_SIZE["A1610"]
    for shape in [(20, 60, 3), (25, 75, 3)]:
        assert features(np.zeros(shape, np.uint8), (w, h), brightness=False).shape == (w * h * 3 + 9,)
        assert features(np.zeros(shape, np.uint8), (w, h)).shape == (w * h * 3 + 9 + w * h + w // 2 * h + 3,)


def test_augmented_copies_keep_the_window_and_shift_it():
    m = 6
    big = np.zeros((20 + 2 * m, 60 + 2 * m, 3), np.uint8)
    big[m + 10, m + 10] = 255                                     # a bright dot on the pin
    copies = augmented(big, m, (m + 10, m + 10))
    assert len(copies) == 6 and all(c.shape == (20, 60, 3) for c in copies)
    assert copies[0][10, 8].max() == 255                          # shifted 2 px right -> the dot appears 2 px left
    assert copies[4][10, 10].max() > 0                            # turning about the pin keeps the dot in place


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
