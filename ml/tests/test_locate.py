"""locate(): finds the board in a frame made from the template with a known homography, and says "not found"
when there is no board. Uses only the committed template, no photos."""
import cv2
import numpy as np

from ml.perception_service.locate import Template, locate


def _known_view():
    tpl = Template.load("A1610")
    h, w = tpl.gray.shape
    # a tilted, turned and scaled view of the template inside a 1080 x 1920 frame
    src = np.float32([[0, 0], [w, 0], [w, h], [0, h]])
    dst = np.float32([[180, 260], [960, 330], [900, 1500], [140, 1380]])
    H = cv2.getPerspectiveTransform(src, dst)
    frame = cv2.warpPerspective(tpl.gray, H, (1080, 1920), borderValue=30)
    return tpl, frame, H


def test_finds_every_pin_of_a_known_view():
    tpl, frame, H = _known_view()
    res = locate(tpl, frame)
    assert res.found and res.inliers >= 15
    for module, pins in tpl.modules.items():
        truth = cv2.perspectiveTransform(np.float32([[p["x"], p["y"]] for p in pins]).reshape(-1, 1, 2), H).reshape(-1, 2)
        err = np.linalg.norm(np.array(res.pins[module]) - truth, axis=1)
        assert err.max() < 4.0, (module, err.max())     # pins are about 18 px apart in this view


def test_no_board_means_not_found():
    tpl = Template.load("A1610")
    noise = np.random.default_rng(0).integers(0, 255, (1920, 1080), dtype=np.uint8)
    assert not locate(tpl, noise).found


def test_same_frame_same_answer():
    tpl, frame, _ = _known_view()
    a, b = locate(tpl, frame), locate(tpl, frame)
    assert a.pins == b.pins


def test_reader_template_uses_lightglue_and_finds_a_known_view():
    import pytest
    from ml.perception_service import locate as L
    if not L.LIGHTGLUE_MODEL.exists():
        pytest.skip("LightGlue model not downloaded (python ml/tools/get_models.py)")
    pytest.importorskip("onnxruntime")
    tpl = Template.load("A4020E")
    assert tpl.matcher == "lightglue"
    h, w = tpl.gray.shape
    H = cv2.getPerspectiveTransform(np.float32([[0, 0], [w, 0], [w, h], [0, h]]),
                                    np.float32([[300, 200], [900, 260], [860, 1300], [260, 1240]]))
    frame = cv2.warpPerspective(tpl.rgb, H, (1080, 1920))
    res = locate(tpl, frame, refine_blocks=False)
    assert res.found
    truth = cv2.perspectiveTransform(np.float32([[p["x"], p["y"]] for p in tpl.modules["A4020E"]]).reshape(-1, 1, 2), H)
    assert np.linalg.norm(np.array(res.pins["A4020E"]) - truth.reshape(-1, 2), axis=1).max() < 8.0
