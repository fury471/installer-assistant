"""HTTP service the Unity app calls in live mode (Detection/PerceptionClient.cs).

Run from the repository root:
    python -m pip install -r ml/requirements.txt
    python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000
Reader: environment variable IA_READER = classical (default) | dummy | dummy-unknown.
The dummy readers give the same answer for every photo: for testing the plumbing only, never for a demo.
"""
import os
import time

from fastapi import FastAPI

from . import images
from .readers import get_reader
from .schemas import CompareRequest, CompareResponse, ReadItem, ReadRequest, ReadResponse

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
    print("read %s %s: %.0f ms" % (req.end, req.usage, (time.perf_counter() - t0) * 1000))
    return ReadResponse(frame_id=req.frame_id, items=items, model=reader.name)


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
