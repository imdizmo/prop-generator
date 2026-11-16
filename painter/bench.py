"""Time the floor atlas paint and measure its memory.

    python3 painter/bench.py --size 256 --repeat 5 [--workers 4]

Prints one line: size=<N> mean_s=<seconds per paint> peak_rss_mb=<MB>. One paint runs first, untimed, as the app's
worker stays running between paints (it also starts the painting processes). peak_rss_mb is the highest resident
memory of this process plus that of each painting process it started, added up: an upper bound on what the paint
holds at once.
"""
import argparse
import os
import resource
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from propgen_painter.floor_atlas import paint_floor_atlas, shutdown_workers   # noqa: E402
from propgen_painter.params import defaults   # noqa: E402


def _descendants(pid):
    out = []
    for task in Path(f"/proc/{pid}/task").iterdir():
        try:
            kids = (task / "children").read_text().split()
        except OSError:
            continue
        for k in kids:
            out += [int(k)] + _descendants(int(k))
    return out


def _hwm_kb(pid):
    try:
        for line in Path(f"/proc/{pid}/status").read_text().splitlines():
            if line.startswith("VmHWM:"):
                return int(line.split()[1])
    except OSError:
        pass
    return 0


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--repeat", type=int, default=5)
    ap.add_argument("--workers", type=int, default=None, help="painting processes (0: paint in this process)")
    a = ap.parse_args(argv)
    kw = {} if a.workers is None else {"workers": a.workers}
    settings = defaults()
    try:
        paint_floor_atlas(settings, a.size, **kw)
        times = []
        for _ in range(a.repeat):
            t = time.perf_counter()
            L = paint_floor_atlas(settings, a.size, **kw)
            times.append(time.perf_counter() - t)
            del L
        kb = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss + sum(_hwm_kb(p) for p in _descendants(os.getpid()))
    finally:
        shutdown_workers()
    print(f"size={a.size} mean_s={sum(times) / len(times):.3f} peak_rss_mb={kb / 1024:.0f}")


if __name__ == "__main__":
    main(sys.argv[1:])
