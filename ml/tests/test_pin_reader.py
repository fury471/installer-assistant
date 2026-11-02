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


def test_aligned_comparison_keeps_a_thin_black_wire_near_dark_background():
    """Per-pixel shifts can reuse one nearby dark background line to erase two different lines. A physical
    whole-window alignment must keep the extra wire visible, without changing the legacy comparison mode."""
    rng = np.random.default_rng(4)
    ref = rng.integers(100, 180, (26, 66, 3), dtype=np.uint8)
    ref[SLACK + 8, SLACK + 12:SLACK + 60] = 15
    wired = window(ref, SLACK).copy()
    wired[10, 12:60] = 15
    erased, _, _ = compare(wired, ref)
    retained, _, _ = compare(wired, ref, aligned=True)
    assert (erased[10, 14:56] > 20).mean() < .1
    assert (retained[10, 14:56] > 20).mean() > .9


@pytest.mark.parametrize("wire", ["green", "yellow"])
def test_anchor_colour_samples_the_entry_wire_instead_of_shadow_or_neighbour(wire):
    from ml.perception_service.pin_reader import anchor_wire_mask, anchored_colour_features
    rgb = np.full((20, 60, 3), (70, 110, 80), np.uint8)
    rgb[7:13, 12:34] = (5, 16, 8)                 # the strong changed shadow selected by the old centre band
    rgb[0:3, 22:55] = (230, 195, 20)              # another wire crosses only farther from this entry
    if wire == "green":
        rgb[16:20, 10:36] = (8, 165, 120)
    else:
        rgb[18:20, 10:22] = (230, 190, 20)        # a bent wire leaves the crop sideways soon after its entry
    D = np.full((20, 60), 30, np.float32)
    mask = anchor_wire_mask(rgb, D)
    assert mask[16:].any() and not mask[:13].any()
    values = anchored_colour_features(rgb, D).reshape(3, 12)
    assert values.shape == (3, 12)
    if wire == "green":
        assert (values[:, 1] < -30).all()
    else:
        assert (values[:, 2] > 50).all()


@pytest.mark.skipif(not PinReader.available(), reason="no trained wire reader (python -m ml.eval.train_pins)")
@pytest.mark.parametrize("name", ["A1610", "A4020E"])
def test_trained_reader_reads_the_empty_reference_as_no_wire(name):
    reader = PinReader()
    for module, refs in reference_patches(Template.load(name), SLACK).items():
        reads = reader.read([window(r, SLACK) for r in refs], refs)
        assert len(reads) == len(refs) and all(0.0 <= conf <= 1.0 for _, conf in reads)
        assert [c for c, _ in reads] == [NO_WIRE] * len(refs), module
