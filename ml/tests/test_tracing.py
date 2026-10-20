"""The bundle check's baseline tracer: does each wire run back to the scanned label?"""
import cv2
import numpy as np

from ml.perception_service.tracing import OTHER, THIS, UNKNOWN, find_labels, label_key, trace_items

C01 = "IA|C01|D1|A1610.READER1|A4020E|CTRL"
C02 = "IA|C02|D2|A1610.READER2|A4020E|CTRL"
RED = (200, 30, 35)


def qr(payload: str, scale: int = 4) -> np.ndarray:
    q = cv2.QRCodeEncoder.create().encode(payload)
    q = cv2.resize(q, None, fx=scale, fy=scale, interpolation=cv2.INTER_NEAREST)
    return np.pad(q, 8, constant_values=255)        # quiet zone


def bench(second_label: bool = True) -> np.ndarray:
    """Two labelled bundles; both have a red wire (different cables share colours) ending on two terminals."""
    img = np.full((480, 640, 3), 128, np.uint8)
    a = qr(C01)
    img[30:30 + a.shape[0], 30:30 + a.shape[1]] = a[..., None]
    if second_label:
        b = qr(C02)
        img[30:30 + b.shape[0], 470:470 + b.shape[1]] = b[..., None]
    cv2.polylines(img, [np.array([[250, 420], [230, 300], [100, 200]], np.int32)], False, RED, 6)     # C01's red
    cv2.polylines(img, [np.array([[330, 420], [360, 300], [530, 200]], np.int32)], False, RED, 6)     # C02's red
    return img


def test_label_key_ignores_the_end_and_case():
    assert label_key(C01) == label_key("ia|c01|d1|a1610.reader1|a4020e|dev") == "C01|D1|A1610.READER1|A4020E"


def test_finds_both_labels():
    keys = sorted(lb.key for lb in find_labels(bench()))
    assert keys == [label_key(C01), label_key(C02)]


def test_same_colour_wires_are_told_apart_by_where_they_run():
    img = bench()
    h, w = img.shape[:2]
    traces = trace_items(img, [(250 / w, 420 / h), (330 / w, 420 / h), None], "IA|C01|D1|A1610.READER1|A4020E")
    assert traces == [THIS, OTHER, UNKNOWN]


def test_no_scanned_label_in_view_gives_unknown():
    img = bench()
    h, w = img.shape[:2]
    assert trace_items(img, [(250 / w, 420 / h)], "IA|C09|D9|A1610.READER1|A4020E") == [UNKNOWN]


def test_a_wire_that_reaches_no_label_is_unknown_not_foreign():
    img = bench(second_label=False)
    h, w = img.shape[:2]
    assert trace_items(img, [(330 / w, 420 / h)], C01) == [UNKNOWN]
