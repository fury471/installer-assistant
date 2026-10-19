# Perception service – JSON contract

Mirrors `unity/InstallerAssistant/Assets/_Project/Scripts/Detection/PerceptionClient.cs` and `schemas.py`. Change all three together.

Positions are normalised to the image: `x` 0 (left) to 1 (right), `y` 0 (top) to 1 (bottom).
Colour codes: 0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange, -1 no wire.
An image is `{"width", "height", "format": "jpeg", "data_b64", "intrinsics": [fx, fy, cx, cy]}`; an empty `data_b64` means "no image" (Unity's JsonUtility cannot send null).

## GET /health

`{"status": "ok", "model": "classical-0"}` (or `dummy-correct` when started with `IA_READER=dummy`)

## POST /read

```json
{
  "frame_id": 12,
  "end": "controller",
  "usage": "READER",
  "terminals": ["−", "12V", "A", "B", "O1", "O2", "IO7", "IO8"],
  "image": {"width": 1280, "height": 720, "format": "jpeg", "data_b64": "...", "intrinsics": []}
}
```

`terminals` are the names as printed, in the order the answer must use: device end `+, −, A, B`; controller pins 1 to 8.

```json
{
  "frame_id": 12,
  "items": [{"colour": 1, "confidence": 0.93, "x": 0.41, "y": 0.53, "found": true}, "..."],
  "model": "classical-0",
  "error": ""
}
```

One item per terminal, same order. `confidence` must be calibrated: the app turns a terminal amber below 0.80. `found: false` means the terminal was not located (the app hides its marker). A non-empty `error` makes the app treat the whole reading as failed.

## POST /compare

```json
{
  "usage": "READER",
  "pin_count": 8,
  "pairs": [{"terminal": "+", "pin": 2}, {"terminal": "−", "pin": 1}, {"terminal": "A", "pin": 3}, {"terminal": "B", "pin": 4}],
  "device_image": {"data_b64": "..."},
  "controller_image": {"data_b64": "..."}
}
```

```json
{"opinions": ["match", "match", "mismatch", "mismatch", "match", "match", "match", "match"], "model": "...", "error": ""}
```

One opinion per pin (index = pin − 1): does the wire on this pin match what the pairing rule and the device end say? `unknown` makes the app ask the installer. This call must not use `/read`'s answer: it is the independent second opinion (layer 3).
