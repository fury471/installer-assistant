"""pin_reader: features have a fixed length, augmented copies keep the window size and move the content as asked,
the comparison with the empty reference sees a wire and nothing else, temperature scaling keeps probabilities valid,
and the trained general reader (if present) answers one (colour, confidence) per window and reads the empty
reference itself as "no wire" on every device."""
import numpy as np
import pytest

from ml.perception_service.locate import Template
from ml.perception_service.patches import reference_patches, window
from ml.perception_service.pin_reader import (GRID, NO_WIRE, SLACK, PinReader, augmented, calibrated, combine, compare,
                                              decide, features, window_features)


def test_features_have_a_fixed_length():
    w, h = GRID
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


def test_comparison_sees_a_wire_and_ignores_light():
    ref = reference_patches(Template.load("A1610"), SLACK)["A1610.READER1"][2]
    empty = window(ref, SLACK)
    darker = (empty.astype(float) * 0.7).astype(np.uint8)        # same pin, dimmer light
    wired = empty.copy()
    wired[8:13, 12:] = (200, 30, 30)                              # a red wire leaving the pin to the right
    D_dark, _, _ = compare(darker, ref)
    D_wire, _, matched = compare(wired, ref)
    assert (D_dark > 20).mean() < 0.05
    assert (D_wire[8:13, 14:] > 20).mean() > 0.9 and (D_wire[:5] > 20).mean() < 0.05
    assert matched[10, 30, 1] > 40                                # the wire's colour, light matched: clearly red (a*)
    p, c = window_features(wired, ref)
    assert p.ndim == 1 and c.ndim == 1


def test_temperature_scaling_keeps_the_ranking():
    p = np.array([[0.7, 0.2, 0.1], [0.4, 0.35, 0.25]])
    for t in [0.5, 1.0, 2.0]:
        q = calibrated(p, t)
        assert np.allclose(q.sum(axis=1), 1.0)
        assert (q.argmax(axis=1) == p.argmax(axis=1)).all()
    assert calibrated(p, 0.5)[0, 0] > p[0, 0] > calibrated(p, 2.0)[0, 0]


def test_a_wire_of_uncertain_colour_is_still_a_wire():
    proba = combine(np.array([0.6]), np.array([[0.4, 0.3, 0.3]]))    # 60 % a wire, colour unclear
    assert decide(proba)[0] != 0


@pytest.mark.skipif(not PinReader.available(), reason="no trained wire reader (python -m ml.eval.train_pins)")
@pytest.mark.parametrize("name", ["A1610", "A4020E"])
def test_trained_reader_reads_the_empty_reference_as_no_wire(name):
    reader = PinReader()
    for module, refs in reference_patches(Template.load(name), SLACK).items():
        reads = reader.read([window(r, SLACK) for r in refs], refs)
        assert len(reads) == len(refs) and all(0.0 <= conf <= 1.0 for _, conf in reads)
        assert [c for c, _ in reads] == [NO_WIRE] * len(refs), module
