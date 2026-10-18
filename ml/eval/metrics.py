"""Metrics for the perception evaluation (report: accuracy test).

colour accuracy and confusion matrix   per terminal, over terminals that carry a wire or should be empty
decode rate                            labels found / labels shown
position error                         distance between predicted and annotated terminal, in image widths
ECE                                    expected calibration error (Guo et al. 2017), 10 equal-width bins
coverage / selective accuracy          share of terminals above the amber threshold, and accuracy on them
"""
from typing import Dict, List, Sequence

import numpy as np

N_COLOURS = 8
NO_WIRE = -1


def confusion(true: Sequence[int], pred: Sequence[int]) -> np.ndarray:
    """(N+1) x (N+1) counts; the last row/column is 'no wire'."""
    m = np.zeros((N_COLOURS + 1, N_COLOURS + 1), dtype=int)
    for t, p in zip(true, pred):
        m[t if t >= 0 else N_COLOURS, p if p >= 0 else N_COLOURS] += 1
    return m


def accuracy(true: Sequence[int], pred: Sequence[int]) -> float:
    t, p = np.asarray(true), np.asarray(pred)
    return float((t == p).mean()) if len(t) else float("nan")


def ece(confidence: Sequence[float], correct: Sequence[bool], bins: int = 10) -> float:
    c, ok = np.asarray(confidence, dtype=float), np.asarray(correct, dtype=float)
    if len(c) == 0:
        return float("nan")
    edges = np.linspace(0, 1, bins + 1)
    total = 0.0
    for lo, hi in zip(edges[:-1], edges[1:]):
        sel = (c > lo) & (c <= hi) if lo > 0 else (c >= lo) & (c <= hi)
        if sel.any():
            total += sel.mean() * abs(ok[sel].mean() - c[sel].mean())
    return float(total)


def position_error(pred_xy: Sequence[Sequence[float]], true_xy: Sequence[Sequence[float]], aspect: Sequence[float]) -> List[float]:
    """Euclidean error in units of image width; 'aspect' = height / width per sample (y is scaled by it)."""
    out = []
    for (px, py), (tx, ty), a in zip(pred_xy, true_xy, aspect):
        out.append(float(np.hypot(px - tx, (py - ty) * a)))
    return out


def selective(confidence: Sequence[float], correct: Sequence[bool], threshold: float) -> Dict[str, float]:
    c, ok = np.asarray(confidence, dtype=float), np.asarray(correct, dtype=bool)
    keep = c >= threshold
    return {
        "threshold": threshold,
        "coverage": float(keep.mean()) if len(c) else float("nan"),
        "accuracy_above": float(ok[keep].mean()) if keep.any() else float("nan"),
        "errors_above": int((~ok[keep]).sum()),   # wrong AND confident: the dangerous case
    }
