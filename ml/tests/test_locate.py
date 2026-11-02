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


def test_a4020e_terminals_carry_the_right_names():
    """The service answers one item per terminal in the template's pin order, and the app maps item i to its own
    terminal i (AxisCatalog: +, -, A, B). Xiaoxin confirmed the reference-photo/top view's physical left-to-right
    order as -, +, A, B on 2 Nov. That physical annotation is independent of the app's array order. The old test
    repeated the template's mistaken B, A, +, - assignment instead of checking it against the physical slots."""
    tpl = Template.load("A4020E")
    pins = tpl.modules["A4020E"]
    assert [p["name"] for p in pins] == ["+", "\u2212", "A", "B"]        # the app's order, which locates the answer
    left_to_right = [p["name"] for p in sorted(pins, key=lambda p: p["x"])]
    assert left_to_right == ["\u2212", "+", "A", "B"]                  # user-confirmed physical reference view


def test_reader_names_follow_physical_slots_in_a_rotated_camera_image():
    """Known physical annotations, not coordinates derived from the template under test. A rotated camera frame
    must carry each terminal's identity with its slot; sorting a list or recolouring a marker cannot fix this."""
    tpl = Template.load("A4020E")
    tpl.matcher = "orb"
    H = np.float64([[0, .5, 400], [-.5, 0, 900], [0, 0, 1]])
    frame = cv2.warpPerspective(tpl.rgb, H, (1920, 1080))
    result = locate(tpl, frame, refine_blocks=False)
    assert result.found
    # Slots annotated in the committed reference photo, physical order -, +, A, B.
    truth = np.float32([[557, 550], [502, 550], [613, 550], [674, 550]])  # response order +, -, A, B
    expected = cv2.perspectiveTransform(truth.reshape(-1, 1, 2), H).reshape(-1, 2)
    assert np.linalg.norm(np.array(result.pins["A4020E"]) - expected, axis=1).max() < 4


def test_reader_patch_colours_are_bound_to_physical_terminal_names():
    """Paint four known wires at independently annotated physical slots, then use the production patch cutter.
    This catches mislabelling even when the set of colours and the four geometric positions look correct."""
    from ml.perception_service.locate import Located
    from ml.perception_service.patches import pin_patches
    tpl = Template.load("A4020E")
    image = tpl.rgb.copy()
    black, red, green, yellow = (15, 15, 15), (240, 30, 30), (20, 190, 40), (245, 220, 15)
    for x, colour in [(502, black), (557, red), (613, green), (674, yellow)]:
        image[560:680, x - 8:x + 9] = colour
    located = Located(True, 100, homography=np.eye(3))
    patches = pin_patches(tpl, located, image)["A4020E"]
    for patch, colour in zip(patches, [red, black, green, yellow]):
        np.testing.assert_allclose(patch[10, 30], colour, atol=2)


def test_a_block_moved_by_a_whole_pin_is_found_where_it_is():
    """The terminal blocks stand above the board, so a tilted phone moves them by up to a pin against the printed
    circuit. Stage 2 has to follow that, or every pin would be read at its neighbour's wire."""
    tpl = Template.load("A1610")
    moved = tpl.rgb.copy()
    strip = moved[600:1300, 40:240].copy()                     # the column of blocks
    shifted = np.empty_like(strip)
    shifted[25:] = strip[:-25]                                 # the blocks stand 25 px lower on the photo
    shifted[:25] = strip[:1]
    moved[600:1300, 40:240] = shifted
    res = locate(tpl, moved)
    assert res.found
    for module, (dx, dy) in res.shifts.items():
        assert abs(dy - 25) <= 6 and abs(dx) <= 6, (module, dx, dy)



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


def test_reader_refinement_follows_the_housing_instead_of_wires():
    """Four independently calibrated empty views, translated by a known amount. Changing wires below the housing
    must not move the row onto a neighbour. This exercises real correlation, without model/locator mocks."""
    from ml.perception_service.locate import _block_boxes, _block_shifts
    tpl = Template.load("A4020E")
    x0, y0, x1, y1 = _block_boxes(tpl)["A4020E"]
    dx, dy = 21, 26
    for reference in tpl.block_references["A4020E"]:
        frame = tpl.gray.copy()
        frame[y0 - 50:y1 + 50, x0 - 50:x1 + 50] = 90
        frame[y0 + dy:y1 + dy, x0 + dx:x1 + dx] = reference
        # Different wire/empty patterns are outside the rigid housing used for matching.
        for x, shade in [(502, 10), (557, 220), (613, 40), (674, 180)]:
            frame[y1 + dy:700, x + dx - 6:x + dx + 6] = shade
        actual = _block_shifts(tpl, frame, np.eye(3))["A4020E"]
        assert np.linalg.norm(np.array(actual) - (dx, dy)) <= 1, actual


def test_reader_evaluation_refines_a_shifted_housing_in_a_rotated_frame():
    """The actual evaluation path must use local refinement, including landscape camera frames. Expected slot
    identities/positions come from the physical reference annotation and the imposed transform."""
    from ml.eval.eval_locate import score_photo
    from ml.perception_service.locate import _block_boxes
    from pathlib import Path
    from tempfile import TemporaryDirectory
    tpl = Template.load("A4020E")
    tpl.matcher = "orb"
    x0, y0, x1, y1 = _block_boxes(tpl)["A4020E"]
    image = tpl.rgb.copy()
    image[y0 - 45:y1 + 45, x0 - 45:x1 + 45] = 90
    dx, dy = 18, 22
    image[y0 + dy:y1 + dy, x0 + dx:x1 + dx] = tpl.block_references["A4020E"][0][..., None]
    H = np.float64([[0, .6, 350], [-.6, 0, 950], [0, 0, 1]])
    frame = cv2.warpPerspective(image, H, (1920, 1080))
    slots = np.float32([[557, 550], [502, 550], [613, 550], [674, 550]]) + (dx, dy)
    truth = cv2.perspectiveTransform(slots.astype(np.float32).reshape(-1, 1, 2), H).reshape(-1, 2)
    ann = {"image": "reader.png", "module": "A4020E", "terminals": [
        {"name": name, "x": float(x / 1920), "y": float(y / 1080)}
        for name, (x, y) in zip(["+", "\u2212", "A", "B"], truth)]}
    # Use the project's data directory, like the real tool: OpenCV's Windows image I/O cannot read the Unicode
    # profile path used by the system temp directory. TemporaryDirectory removes only this test's own subfolder.
    with TemporaryDirectory(prefix="reader-check-", dir=Path(__file__).resolve().parents[2] / "data") as folder:
        assert cv2.imwrite(str(Path(folder) / "reader.png"), cv2.cvtColor(frame, cv2.COLOR_RGB2BGR))
        result, _, _, errors, _, _ = score_photo(tpl, ann, folder, 1920)
    assert result.found and max(errors) < .15, errors
