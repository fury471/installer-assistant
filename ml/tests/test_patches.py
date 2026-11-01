"""pin_patches(): every pin gets a window of the canonical size, on a frame made from the template itself the window
looks like the reference window of that pin, and on the board (spacing 24, wire to the right) the canonical window is
the plain crop; the block check accepts a correct placement and catches one that is a whole block off."""
import cv2
import numpy as np

from ml.perception_service.locate import Template, locate
from ml.perception_service.locate import _check_blocks
from ml.perception_service.patches import patch_box, pin_patches, reference_patches


def test_patches_of_a_known_view_match_the_template():
    tpl = Template.load("A1610")
    h, w = tpl.gray.shape
    H = cv2.getPerspectiveTransform(np.float32([[0, 0], [w, 0], [w, h], [0, h]]),
                                    np.float32([[180, 260], [960, 330], [900, 1500], [140, 1380]]))
    frame = cv2.warpPerspective(tpl.rgb, H, (1080, 1920), borderValue=(30, 30, 30))
    res = locate(tpl, frame)
    assert res.found
    assert res.doubtful == {}
    patches = pin_patches(tpl, res, frame)
    refs = reference_patches(tpl, 0)
    x0, y0, x1, y1 = patch_box(tpl)
    for module, pins in tpl.modules.items():
        assert len(patches[module]) == len(pins)
        for p, patch, ref_window in zip(pins, patches[module], refs[module]):
            assert patch.shape == (y1 - y0, x1 - x0, 3) == ref_window.shape
            x, y = int(round(p["x"])), int(round(p["y"]))
            ref = tpl.rgb[y + y0:y + y1, x + x0:x + x1].astype(float)
            assert np.abs(patch.astype(float) - ref).mean() < 25     # same place, blurred by two resamplings
            assert np.abs(ref_window.astype(float) - ref).mean() < 1                # canonical = plain crop here
    # the same view with the board placed one block off (Reader 1 where Reader 2 is) is caught
    d = (pins_of(tpl, "A1610.READER2")[0] - pins_of(tpl, "A1610.READER1")[0])
    off = res.homography @ np.array([[1, 0, -d[0]], [0, 1, -d[1]], [0, 0, 1]], float)
    assert {"A1610.READER1", "A1610.READER2"} & set(_check_blocks(tpl, frame, off, {}))


def pins_of(tpl, module):
    return np.array([[p["x"], p["y"]] for p in tpl.modules[module]], float)
