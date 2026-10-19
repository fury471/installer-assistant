# ml – perception (package A)

| Folder | What |
| --- | --- |
| `perception_service/` | The HTTP service the app calls in live mode: `/read` (layer 1, wire colours and positions) and `/compare` (layer 3, independent second opinion). Contract: `perception_service/README.md`. |
| `eval/` | Accuracy report on annotated photos: colour accuracy, confusion matrix, position error, ECE, coverage above the amber threshold, latency. |
| `tests/` | Contract and metric tests: `python -m pytest ml/tests` (run from the repository root). |

Quick start, from the repository root:

```
python -m pip install -r ml/requirements.txt
python -m pytest ml/tests
python -m uvicorn ml.perception_service.app:app --host 0.0.0.0 --port 8000
```

Readers are chosen with the environment variable `IA_READER`: `classical` (default: template + CIELAB baseline, being built), `dummy` (a correctly wired bench whatever the photo, for testing the plumbing only, never for a demo), `dummy-unknown` (finds nothing). A VLM reader and a trained model go next to them in `perception_service/readers/`.

Trained models: export to `.onnx`. The service can run them with onnxruntime; later the same file can run on the phone with Unity Sentis in `unity/InstallerAssistant/Assets/_Project/Models/`, behind the same `IWireReader`.

Do not commit photos, datasets, training runs, model weights or API keys (`.gitignore` covers `data/`, `ml/runs/`, `*.pt`, `secrets*.json`, `.env`).
