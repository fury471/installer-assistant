"""pin_patches(): every pin gets a patch of the template's window size, and on a frame made from the template
itself the patch looks like the template around that pin."""
import cv2
import numpy as np

from ml.perception_service.locate import Template, locate
from ml.perception_service.patches import patch_box, pin_patches


def test_patches_of_a_known_view_match_the_template():
    tpl = Template.load("A1610")
    h, w = tpl.gray.shape
    H = cv2.getPerspectiveTransform(np.float32([[0, 0], [w, 0], [w, h], [0, h]]),
                                    np.float32([[180, 260], [960, 330], [900, 1500], [140, 1380]]))
    frame = cv2.warpPerspective(tpl.rgb, H, (1080, 1920), borderValue=(30, 30, 30))
    res = locate(tpl, frame)
    assert res.found
    patches = pin_patches(tpl, res, frame)
    x0, y0, x1, y1 = patch_box(tpl)
    for module, pins in tpl.modules.items():
        assert len(patches[module]) == len(pins)
        for p, patch in zip(pins, patches[module]):
            assert patch.shape == (y1 - y0, x1 - x0, 3)
            x, y = int(round(p["x"])), int(round(p["y"]))
            ref = tpl.rgb[y + y0:y + y1, x + x0:x + x1].astype(float)
            assert np.abs(patch.astype(float) - ref).mean() < 25     # same place, blurred by two resamplings
