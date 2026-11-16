"""The painting processes never outlive the process that started them, however it ends."""
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

import pytest

PAINTER = Path(__file__).resolve().parents[1]
SCRIPT = f"""
import sys, time
sys.path.insert(0, {str(PAINTER)!r})
from propgen_painter import floor_atlas as fa
from propgen_painter.params import defaults
if __name__ == "__main__":
    if int(sys.argv[1]):
        fa.exit_on_sigterm()
    fa.paint_floor_atlas(defaults(), 256)
    print("ready", flush=True)
    time.sleep(60)
"""


def descendants(pid):
    out = []
    for task in Path(f"/proc/{pid}/task").glob("*"):
        try:
            kids = (task / "children").read_text().split()
        except OSError:
            continue
        for k in map(int, kids):
            out += [k] + descendants(k)
    return out


def running(pid):
    try:
        state = next(l for l in Path(f"/proc/{pid}/status").read_text().splitlines() if l.startswith("State:"))
    except (OSError, StopIteration):
        return False
    return state.split()[1] != "Z"


@pytest.mark.parametrize("sig, handler", [(signal.SIGKILL, 0), (signal.SIGTERM, 0), (signal.SIGTERM, 1)])
def test_workers_end_with_their_parent(sig, handler):
    p = subprocess.Popen([sys.executable, "-I", "-c", SCRIPT, str(handler)], stdout=subprocess.PIPE,
                         stderr=subprocess.DEVNULL, text=True)
    kids = []
    try:
        assert p.stdout.readline().strip() == "ready"
        kids = descendants(p.pid)
        assert len(kids) >= 4                      # the four workers (and Python's forkserver and tracker)
        os.kill(p.pid, sig)
        p.wait(timeout=5)
        deadline = time.monotonic() + 2.0
        while time.monotonic() < deadline and any(running(k) for k in kids):
            time.sleep(0.05)
        assert [k for k in kids if running(k)] == []
    finally:
        for pid in [p.pid] + kids:                  # never leave anything behind, even when the test fails
            if running(pid):
                os.kill(pid, signal.SIGKILL)
        p.stdout.close()
        p.wait(timeout=5)
