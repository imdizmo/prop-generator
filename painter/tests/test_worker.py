import json
import os
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

from propgen_painter.layers import LAYER_NAMES, Layers, compose_v5

WORKER = Path(__file__).resolve().parent.parent / "worker.py"
GOLDEN = Path(__file__).parent / "golden" / "floor_atlas_v5.png"


def children(pid):
    out = []
    for p in Path("/proc").glob("[0-9]*"):
        try:
            stat = (p / "stat").read_text()
            if int(stat.rsplit(")", 1)[1].split()[1]) == pid:
                out.append(int(p.name))
        except (OSError, ValueError, IndexError):
            pass
    return out


class Worker:
    def __init__(self):
        self.proc = subprocess.Popen([sys.executable, "-I", str(WORKER)], stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE)

    def ask(self, id, size, settings):
        req = {"id": id, "size": size, "settings": settings}
        self.proc.stdin.write((json.dumps(req) + "\n").encode())
        self.proc.stdin.flush()
        head = json.loads(self.proc.stdout.readline())
        if "error" in head:
            return head, None
        data = self.proc.stdout.read(head["bytes"])
        assert len(data) == head["bytes"]
        a = np.frombuffer(data, dtype="<f2").reshape(len(head["layers"]), head["size"], head["size"])
        return head, a


def gone(pid):
    try:
        return Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()[0] == "Z"
    except OSError:
        return True


def with_worker(fn):
    """Run fn(worker); then kill the worker by PID and check its painting processes follow it."""
    w = Worker()
    kids = []
    try:
        fn(w)
        kids = children(w.proc.pid)
    finally:
        w.proc.kill()
        w.proc.wait()
        deadline = time.time() + 5
        while time.time() < deadline and not all(gone(k) for k in kids):
            time.sleep(0.1)
        left = [k for k in kids if not gone(k)]
        for k in left:
            os.kill(k, 9)
    assert not left, f"painting processes outlived the killed worker: {left}"


def test_256_request_returns_seven_float16_layers():
    def run(w):
        head, a = w.ask(7, 256, {})
        assert head == {"id": 7, "size": 256, "layers": list(LAYER_NAMES), "dtype": "float16",
                        "bytes": 7 * 256 * 256 * 2}
        assert a.shape == (7, 256, 256) and a.dtype == np.dtype("<f2")
        assert np.isfinite(a).all()
    with_worker(run)


def test_error_then_next_request_works():
    def run(w):
        head, a = w.ask(1, 256, {"nonsense": 1})
        assert head["id"] == 1 and "error" in head and a is None
        head, a = w.ask(2, 256, {})
        assert head["id"] == 2 and a.shape == (7, 256, 256)
        head, a = w.ask(3, 100, {})
        assert head["id"] == 3 and "error" in head
        assert w.proc.poll() is None
    with_worker(run)


def test_stdin_eof_ends_process_and_pool():
    w = Worker()
    try:
        w.ask(1, 256, {})
        kids = children(w.proc.pid)
        assert kids
        w.proc.stdin.close()
        assert w.proc.wait(timeout=2) == 0
        time.sleep(0.5)
        for k in kids:
            assert not Path(f"/proc/{k}").exists()
    finally:
        if w.proc.poll() is None:
            w.proc.kill()
            w.proc.wait()


def test_float16_layers_compose_within_one_of_golden():
    def run(w):
        head, a = w.ask(1, 1024, {})
        L = Layers(*[a[i].astype(np.float32) for i in range(7)])
        got = compose_v5(L, contrast=0.65, drift=0.6).astype(int)
        want = np.asarray(Image.open(GOLDEN))[..., :3].astype(int)
        d = np.abs(got - want)
        print(f"float16 round trip: max channel difference {d.max()}, {int((d.max(axis=-1) > 1).sum())} pixels over 1")
        assert d.max() <= 1
    with_worker(run)
