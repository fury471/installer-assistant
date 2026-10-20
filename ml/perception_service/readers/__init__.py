"""Wire readers. Each one turns an image of one end into one ReadItem per terminal."""
from .base import Reader
from .dummy import DummyReader

__all__ = ["Reader", "DummyReader", "get_reader"]


def get_reader(name: str) -> Reader:
    """Reader by name (environment variable IA_READER). Add new readers here."""
    if name in ("dummy", "dummy-correct", "dummy-unknown", "dummy-elsewhere", "dummy-tangled"):
        return DummyReader(mode={"dummy-unknown": "unknown", "dummy-elsewhere": "elsewhere", "dummy-tangled": "tangled"}.get(name, "correct"))
    if name in ("", "classical"):
        from .classical import ClassicalReader
        return ClassicalReader()
    raise ValueError("unknown reader: " + name)
