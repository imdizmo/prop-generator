"""The painter worker: the app starts this once and keeps it running, sending it paint requests.

Protocol (the C# side reads exactly this). Everything is on stdin and stdout; stderr is only for logs, and nothing
else is ever written to stdout.

Request, one JSON line on stdin:
    {"id": int, "size": 256 | 512 | 1024, "settings": {...}}

Response, one JSON line on stdout, followed at once by exactly `bytes` raw bytes:
    {"id": int, "size": int, "layers": ["marks", "rest", "drift", "region", "basemul", "seam", "aa"],
     "dtype": "float16", "bytes": N}

Byte layout: the layers in the order of the "layers" list; each layer is size x size values, little-endian float16,
row-major, rows bottom-up (row 0 is the bottom of the texture, as the painter holds it). N = 7 * size * size * 2.

If a request fails (bad JSON, unknown setting, bad size, any error while painting) the response is one JSON line
{"id": int, "error": "message"} with no bytes, and the worker keeps running. "id" is null if the request had none.

When stdin closes, the worker stops its painting processes and exits with 0.
"""
import json
import sys
from pathlib import Path


def respond(out, head, payload=b""):
    out.write((json.dumps(head) + "\n").encode())
    if payload:
        out.write(payload)
    out.flush()


def handle(line, out, paint, validate, layer_names, sizes):
    req_id = None
    try:
        req = json.loads(line)
        req_id = req.get("id")
        size = req.get("size")
        if size not in sizes:
            raise ValueError(f"size must be one of {list(sizes)}")
        settings = validate(req.get("settings") or {})
        layers = paint(settings, size)
        payload = b"".join(a.astype("<f2").tobytes() for a in layers.arrays())
        del layers
    except Exception as e:
        print(f"request {req_id} failed: {e!r}", file=sys.stderr, flush=True)
        respond(out, {"id": req_id, "error": str(e) or type(e).__name__})
        return
    respond(out, {"id": req_id, "size": size, "layers": list(layer_names), "dtype": "float16",
                  "bytes": len(payload)}, payload)


def main():
    # -I (isolated mode) leaves the script's folder off sys.path, so add it.
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from propgen_painter.floor_atlas import SIZES, exit_on_sigterm, paint_floor_atlas, shutdown_workers
    from propgen_painter.layers import LAYER_NAMES
    from propgen_painter.params import validate

    exit_on_sigterm()
    out = sys.stdout.buffer
    try:
        for line in sys.stdin.buffer:
            if line.strip():
                handle(line, out, paint_floor_atlas, validate, LAYER_NAMES, SIZES)
    finally:
        shutdown_workers()


if __name__ == "__main__":
    main()
    sys.exit(0)
