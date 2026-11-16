"""Test helper: runs the real painter worker, and appends one line per response it sends to a count file.

Usage: python3 -I counting_worker.py <worker.py> <count file>
"""
import importlib.util
import sys


def main():
    worker_py, count_file = sys.argv[1], sys.argv[2]
    spec = importlib.util.spec_from_file_location("painter_worker", worker_py)
    worker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(worker)
    respond = worker.respond

    def counting_respond(out, head, payload=b""):
        respond(out, head, payload)
        with open(count_file, "a") as f:
            f.write(f"{head.get('id')}\n")

    worker.respond = counting_respond
    worker.main()


# The painting processes import this file again under another name; only the real start runs the worker.
if __name__ == "__main__":
    main()
    sys.exit(0)
