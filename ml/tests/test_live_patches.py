"""ml/tools/live_patches.py: saved live frames + the wiring you know -> patches and labels for train_pins."""
import csv
import shutil

import pytest

from ml.perception_service.locate import TEMPLATES, Template
from ml.tools import live_patches


def test_a_folder_of_frames_becomes_labelled_patches(tmp_path):
    frames = tmp_path / "live" / "batch-a"
    frames.mkdir(parents=True)
    shutil.copy(TEMPLATES / "A1610.jpg", frames / "20261115-100000-000_controller_A1610.READER1_f1.jpg")
    live_patches.main([str(frames), "--name", "live_a", "--data", str(tmp_path),
                       "--wiring", "A1610.READER1=black,red,green,yellow,-,-,-,-"])
    rows = list(csv.DictReader(open(tmp_path / "patches" / "live_a" / "labels.csv", encoding="utf-8")))
    tpl = Template.load("A1610")
    assert len(rows) == sum(len(p) for p in tpl.modules.values())
    reader1 = [int(r["colour"]) for r in rows if r["module"] == "A1610.READER1"]
    assert reader1 == [1, 0, 2, 3, -1, -1, -1, -1]                      # black, red, green, yellow, then empty
    assert all(int(r["colour"]) == -1 for r in rows if r["module"] != "A1610.READER1")
    assert {r["row"] for r in rows} == {"batch-a"}                      # one wiring group: held out together
    assert all((tmp_path / "patches" / "live_a" / r["file"]).exists() for r in rows)


def test_a_wrong_wiring_is_refused():
    tpl = Template.load("A1610")
    with pytest.raises(SystemExit):
        live_patches.parse_wiring("A1610.READER1=black,red", tpl)
    with pytest.raises(SystemExit):
        live_patches.parse_wiring("A1610.READER1=pink,red,green,yellow,-,-,-,-", tpl)
