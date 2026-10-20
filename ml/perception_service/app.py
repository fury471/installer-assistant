"""HTTP service the Unity app calls in live mode (Detection/PerceptionClient.cs).

Run from the repository root:
    python -m pip install -r ml/requirements.txt
    python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000
Reader: environment variable IA_READER = classical (default) | dummy | dummy-unknown | dummy-elsewhere | dummy-tangled.
The dummy readers give the same answer for every photo: for testing the plumbing only, never for a demo.
dummy-elsewhere puts the cable in the first other module, to try the zone check ("move the cable to Reader 1").
dummy-tangled traces the wire on pin 2 to another cable, to try the bundle check ("the wire on 12V is from another cable").
"""
import os
import time

from fastapi import FastAPI

from . import images
from .readers import get_reader
from .schemas import CompareRequest, CompareResponse, ModuleItems, ReadItem, ReadRequest, ReadResponse

app = FastAPI(title="Installer Assistant perception service", version="0.1")
reader = get_reader(os.environ.get("IA_READER", "classical"))


@app.get("/health")
def health():
    return {"status": "ok", "model": reader.name}


@app.post("/read", response_model=ReadResponse)
def read(req: ReadRequest) -> ReadResponse:
    t0 = time.perf_counter()
    if not req.image.present:
        return ReadResponse(frame_id=req.frame_id, items=[ReadItem() for _ in req.terminals], model=reader.name, error="no image")
    try:
        image = images.decode(req.image)
        items = reader.read(req, image)
    except Exception as e:  # the app shows "read again"; the log says why
        return ReadResponse(frame_id=req.frame_id, items=[ReadItem() for _ in req.terminals], model=reader.name, error=repr(e))
    if len(items) != len(req.terminals):
        return ReadResponse(frame_id=req.frame_id, items=[ReadItem() for _ in req.terminals], model=reader.name,
                            error="reader returned %d items for %d terminals" % (len(items), len(req.terminals)))
    others = survey(req, image)
    print("read %s %s (+%d modules surveyed): %.0f ms" % (req.end, req.module, len(others), (time.perf_counter() - t0) * 1000))
    return ReadResponse(frame_id=req.frame_id, items=items, other_blocks=others, model=reader.name)


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
    return [o if o.located else ModuleItems(module=o.module, located=False, items=[ReadItem() for _ in m.terminals])
            for o, m in zip(others, req.other_modules)]


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
