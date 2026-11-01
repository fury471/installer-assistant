"""HTTP service the Unity app calls in live mode (Detection/PerceptionClient.cs).

Run from the repository root:
    python -m pip install -r ml/requirements.txt
    python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000
Reader: environment variable IA_READER = classical (default) | dummy | dummy-unknown | dummy-elsewhere | dummy-tangled.
The dummy readers give the same answer for every photo: for testing the plumbing only, never for a demo.
dummy-elsewhere puts the cable in the first other module, to try the zone check ("move the cable to Reader 1").
dummy-tangled traces the wire on pin 2 to another cable, to try the bundle check ("the wire on 12V is from another cable").
Live frames: set IA_SAVE_FRAMES to a folder (e.g. data/live/2026-10-31) to save every frame the app sends with the
request and the answer (frame_log.py); ml/tools/review_frames.py draws the answers onto them.
"""
import os
import time

from fastapi import FastAPI

from . import images
from .frame_log import FrameLog
from .readers import get_reader
from .schemas import CompareRequest, CompareResponse, ModuleItems, ReadItem, ReadRequest, ReadResponse

app = FastAPI(title="Installer Assistant perception service", version="0.1")
reader = get_reader(os.environ.get("IA_READER", "classical"))
frame_log = FrameLog.from_environment()     # None unless IA_SAVE_FRAMES is set
# Zone survey: a wire on another module counts only when the reader is at least this sure. One doubtful pin must not
# make the app say "Is this cable on Door 1?" (bench, 31 Oct). 0.8 = the app's amber line; on session 1 (row-wise
# cross-validated) it leaves 0 of 160 empty modules "holding a wire" and misses 1 of 64 modules that hold wires.
SURVEY_SURE = float(os.environ.get("IA_SURVEY_SURE", "0.8"))


@app.get("/health")
def health():
    return {"status": "ok", "model": reader.name}


@app.post("/read", response_model=ReadResponse)
def read(req: ReadRequest) -> ReadResponse:
    t0 = time.perf_counter()
    resp, size = _read(req)
    if frame_log is not None:
        frame_log.save(req, resp, (time.perf_counter() - t0) * 1000, size)
    return resp


def _read(req: ReadRequest):
    """The answer, and the decoded image's (width, height) for the frame log ((0, 0) if it could not be decoded)."""
    t0 = time.perf_counter()
    size = (0, 0)
    if not req.image.present:
        return ReadResponse(frame_id=req.frame_id, items=[ReadItem() for _ in req.terminals], model=reader.name,
                            error="no image"), size
    try:
        image = images.decode(req.image)
        size = (image.shape[1], image.shape[0])
        items = reader.read(req, image)
    except Exception as e:  # the app shows "read again"; the log says why
        return ReadResponse(frame_id=req.frame_id, items=[ReadItem() for _ in req.terminals], model=reader.name,
                            error=repr(e)), size
    if len(items) != len(req.terminals):
        return ReadResponse(frame_id=req.frame_id, items=[ReadItem() for _ in req.terminals], model=reader.name,
                            error="reader returned %d items for %d terminals" % (len(items), len(req.terminals))), size
    others = survey(req, image)
    print("read %s %s (+%d modules surveyed): %dx%d image, %.0f ms" % (req.end, req.module, len(others),
          image.shape[1], image.shape[0], (time.perf_counter() - t0) * 1000))   # width x height the phone sent
    return ReadResponse(frame_id=req.frame_id, items=items, other_blocks=others, model=reader.name), size


def survey(req: ReadRequest, image) -> list:
    """The zone survey of the controller's other modules. Any problem skips it ([]), so the read itself still counts."""
    if not req.other_modules:
        return []
    try:
        others = reader.survey(req, image)
    except Exception as e:
        print("survey failed, zone check skipped: %r" % e)
        return []
    ok = len(others) == len(req.other_modules) and all(
        o.module == m.module and (not o.located or len(o.items) == len(m.terminals)) for o, m in zip(others, req.other_modules))
    if not ok:
        print("survey does not match the request, zone check skipped")
        return []
    return [ModuleItems(module=o.module, located=True, items=[sure_or_empty(it) for it in o.items]) if o.located
            else ModuleItems(module=o.module, located=False, items=[ReadItem() for _ in m.terminals])
            for o, m in zip(others, req.other_modules)]


def sure_or_empty(item: ReadItem) -> ReadItem:
    """A surveyed wire below SURVEY_SURE is reported as no wire (with the complementary confidence)."""
    if item.colour < 0 or item.confidence >= SURVEY_SURE:
        return item
    return item.model_copy(update={"colour": -1, "confidence": round(1.0 - item.confidence, 3)})


@app.post("/compare", response_model=CompareResponse)
def compare(req: CompareRequest) -> CompareResponse:
    try:
        dev = images.decode(req.device_image) if req.device_image.present else None
        ctrl = images.decode(req.controller_image) if req.controller_image.present else None
        ops = reader.compare(req, dev, ctrl)
    except Exception as e:
        return CompareResponse(opinions=["unknown"] * req.pin_count, model=reader.name, error=repr(e))
    if len(ops) != req.pin_count:
        ops = (list(ops) + ["unknown"] * req.pin_count)[:req.pin_count]
    return CompareResponse(opinions=ops, model=reader.name)
