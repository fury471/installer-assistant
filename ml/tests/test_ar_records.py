"""ml/tools/ar_records.py reads the phone's AR records (ArRecords.cs) and measures the mark offset at each re-read."""
import json

import pytest

from ml.tools import ar_records


def _read(frame, shift_px, outcome, w=1920, h=1080, marks=True):
    # 8 pins in a row, 24 px apart; the marks sit shift_px to the right of where the reader finds them now
    points = []
    for i in range(8):
        u, v = (800 + 24 * i) / w, 500 / h
        p = {"_": 0, "key": f"pin:{i + 1}", "u": u, "v": v, "taken": True, "agrees": True, "rayMethodPx": 0.4,
             "sightings": 2, "baselineMm": 41.0}
        if marks:
            p["markU"], p["markV"] = u + shift_px / w, v
        points.append(p)
    return {"kind": "read", "t": frame, "clock": "10:00:0%d.000" % frame, "frame": frame, "w": w, "h": h,
            "depth": 0.36, "points": points, "outcome": outcome}


def test_offsets_are_in_pitches_and_split_by_reread(tmp_path):
    events = [{"kind": "start", "device": "test phone"},
              {"kind": "camera", "cpuImage": "1920x1080", "texture": "1920x1080"},
              _read(1, 0, "first", marks=False), _read(2, 48, "fused"), _read(3, 6, "fused"),
              {"kind": "clear"}, _read(4, 0, "first", marks=False), _read(5, 12, "rejected"),
              {"kind": "shot", "file": "shot-1.png"}]
    f = tmp_path / "20261114-100000.jsonl"
    f.write_text("\n".join(json.dumps(e) for e in events) + "\n{broken", encoding="utf-8")
    assert ar_records.offsets_of(events[3]) == pytest.approx([2.0] * 8)
    s, rows = ar_records.summarise(ar_records.load(f), f.name)
    assert s["reads"] == 5
    assert s["outcomes"] == {"first": 2, "fused": 2, "rejected": 1}
    assert s["offset_pitch"]["first_reread"]["median"] == 1.25      # 2.0 (after read 1) and 0.5 (after read 4)
    assert s["offset_pitch"]["later_rereads"]["median"] == 0.25
    assert s["screenshots"] == ["shot-1.png"]
    assert len(rows) == 5 and rows[1]["offset_median_pitch"] == pytest.approx(2.0)
    ar_records.main([str(tmp_path), "--csv", str(tmp_path / "reads.csv")])
    assert (tmp_path / "summary.json").exists() and (tmp_path / "reads.csv").exists()
