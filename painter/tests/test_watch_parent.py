"""How a painting process waits for the process that started it to end, on each platform."""
import subprocess
import sys
import threading

import pytest

from propgen_painter import floor_atlas as fa


class FakeKernel32:
    """Stands in for ctypes.windll.kernel32: records the calls, never touches a real process."""

    def __init__(self, handle=1234):
        self.handle = handle
        self.calls = []

    def OpenProcess(self, access, inherit, pid):
        self.calls.append(("OpenProcess", access, inherit, pid))
        return self.handle

    def WaitForSingleObject(self, handle, ms):
        self.calls.append(("WaitForSingleObject", handle, ms))
        return 0

    def CloseHandle(self, handle):
        self.calls.append(("CloseHandle", handle))
        return 1


def no_kill(*args):
    raise AssertionError("os.kill must never be called on Windows: os.kill(pid, 0) ends the process there")


def test_windows_waits_on_the_parents_handle_and_never_signals_it(monkeypatch):
    monkeypatch.setattr(fa.os, "kill", no_kill)
    k = FakeKernel32()
    fa._parent_waiter(4242, windows=True, kernel32=k)()
    assert k.calls[0] == ("OpenProcess", fa._SYNCHRONIZE, False, 4242)
    assert ("WaitForSingleObject", 1234, fa._INFINITE) in k.calls
    assert k.calls[-1] == ("CloseHandle", 1234)


def test_windows_returns_at_once_when_the_parent_is_already_gone(monkeypatch):
    monkeypatch.setattr(fa.os, "kill", no_kill)
    k = FakeKernel32(handle=0)          # OpenProcess fails: no such process
    fa._parent_waiter(4242, windows=True, kernel32=k)()
    assert [c[0] for c in k.calls] == ["OpenProcess"]


@pytest.mark.skipif(sys.platform != "linux", reason="the POSIX path")
def test_posix_wait_returns_once_the_process_has_ended():
    p = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"])
    try:
        done = threading.Event()
        threading.Thread(target=lambda: (fa._parent_waiter(p.pid, windows=False)(), done.set()), daemon=True).start()
        assert not done.wait(0.3)
        p.kill()
        p.wait(timeout=5)
        assert done.wait(5)
    finally:
        if p.poll() is None:
            p.kill()
            p.wait(timeout=5)
