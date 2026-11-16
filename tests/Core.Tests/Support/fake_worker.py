"""Test helper: a fake painter worker for WorkerClient's failure tests. It needs no numpy.

Usage: python3 -I fake_worker.py <mode>
    ok            answers every request with valid all-zero layers
    stall-read    reads the request, then says nothing
    stall-header  sends a valid reply header, then stalls before the bytes
    memory-error  answers {"id", "error": "MemoryError()"} and keeps running
    bad-header    answers a header whose size is 1.5
    ignore-eof    starts a child (sleep 60), never reads stdin, never exits by itself
"""
import json
import subprocess
import sys
import time

LAYERS = ["marks", "rest", "drift", "region", "basemul", "seam", "aa"]


def send(out, head, payload=b""):
    out.write((json.dumps(head) + "\n").encode())
    out.flush()
    if payload:
        out.write(payload)
        out.flush()


def main():
    mode = sys.argv[1]
    out = sys.stdout.buffer
    if mode == "ignore-eof":
        subprocess.Popen(["sleep", "60"])
        time.sleep(60)
        return
    for line in sys.stdin.buffer:
        req = json.loads(line)
        rid, size = req["id"], req["size"]
        n = len(LAYERS) * size * size * 2
        if mode == "stall-read":
            time.sleep(60)
        elif mode == "memory-error":
            send(out, {"id": rid, "error": "MemoryError()"})
        elif mode == "bad-header":
            send(out, {"id": rid, "size": 1.5, "layers": LAYERS, "dtype": "float16", "bytes": n})
        else:
            send(out, {"id": rid, "size": size, "layers": LAYERS, "dtype": "float16", "bytes": n})
            if mode == "stall-header":
                time.sleep(60)
            out.write(bytes(n))
            out.flush()


if __name__ == "__main__":
    main()
