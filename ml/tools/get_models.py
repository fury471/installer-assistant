"""Download the model files the perception service needs (they are too big for Git) into ml/models/.

    python ml\\tools\\get_models.py

disk_lightglue_pipeline.onnx: DISK features + LightGlue matcher in one ONNX file, from LightGlue-ONNX v2.0
(github.com/fabio-sim/LightGlue-ONNX, Apache-2.0; LightGlue and DISK weights Apache-2.0). Used by locate() for
templates whose JSON says "matcher": "lightglue" (the A4020-E reader). The checksum makes sure the file is exactly
the one the numbers in ml/LOG.md were measured with.
"""
import hashlib
import urllib.request
from pathlib import Path

MODELS = Path(__file__).resolve().parents[1] / "models"
FILES = {
    "disk_lightglue_pipeline.onnx": (
        "https://github.com/fabio-sim/LightGlue-ONNX/releases/download/v2.0/disk_lightglue_pipeline.onnx",
        "23300fb62b76bca5639e61ec6b7f8579757f49b15c5fe70d93fb1f403d93c6d3"),
}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    MODELS.mkdir(parents=True, exist_ok=True)
    for name, (url, digest) in FILES.items():
        path = MODELS / name
        if path.exists() and sha256(path) == digest:
            print(f"{name}: already there")
            continue
        print(f"{name}: downloading ...")
        urllib.request.urlretrieve(url, path)
        if sha256(path) != digest:
            path.unlink()
            raise SystemExit(f"{name}: checksum does not match, file removed. Download it by hand from {url}")
        print(f"{name}: ok")


if __name__ == "__main__":
    main()
