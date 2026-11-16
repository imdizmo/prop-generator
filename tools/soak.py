#!/usr/bin/env python3
"""Drives a running Prop Generator through Claude's connection for the soak test (stdlib only).

Usage: soak.py PORT TOKEN APP_PID CSV MAPFOLDER SAVEPATH PIDLIST [SECONDS] [floor|engine|phase3]

The floor drive (the default) sends 3600 paint and 3600 tone set_control calls over SECONDS (default 1800), about 4 a
second, the values sweeping each control's range. Every 10 s it writes the app's RSS and the worker's RSS (the worker
alone, and the worker with all its descendants) to the CSV. A sampler also looks every second so the worker peak is not
missed between rows. At the end it saves and exports, then prints a SUMMARY line.

The engine drive works easy-props' kinds, which run on the engine (src/PropGenerator.Engine): it alternates the oak
(90 s) with each of the other seven engine kinds in turn (45 s), sweeping the shown kind's sliders about 4 times a
second, changes the detail level every 10 s (full, 1, 2, far away where the kind has it) and saves and exports every
minute. Every 10 s its CSV row also has the engine's V8 heap, its ArrayBuffer bytes, the textures kept and the engine
generation, read from get_state's engine block. Its SUMMARY adds the app's peak RSS, the engine's peaks, the highest
generation (1 means no reload or recycle) and the build times the status line reported.

The phase3 drive is the engine drive plus easy-props 0.4.5's features (engine phase 3), at picture size 2048 (set in
the prefs by soak.sh): the sliders it sweeps include snow, wet and moss, and mossFrom flips every 20 s; every minute a
clump move and a Break of a limb on the oak, both undone; every 30 s a new slope and downhill; every minute an export at 2048
with the rock's grain and lichen or the tree's bark pattern changed first, so upstream's texture caches see new
pictures; every 2 minutes Show me more's Damaged page on the oak; every 5 minutes Make pieces from the oak (as many as its
card offers; the piece files deleted afterwards), a two-version set from the Export many card and a three-prop set from Your props with a
colour change while its previews draw. The cards have no tool on Claude's connection, so they are driven with xdotool
clicks on the private display (DISPLAY), at the places the 1600x1000 Xvfb window puts them; each card flow is checked
by the files it writes, and a flow that writes nothing counts as a failed call.

Every pid it sees is appended to PIDLIST, one per line, as soon as it is seen. Exits 1 on FAIL.

`soak.py --judge CSV [PEAK_MB]` only judges a finished run's CSV with the same rule.
"""
import json
import os
import sys
import threading
import time
import urllib.request


def median_at(rows, t, w):
    v = sorted(r[1] for r in rows if abs(r[0] - t) <= w)
    return v[len(v) // 2]


def memory_verdict(rows, total_s, peak_mb):
    """Rows are (t_s, app_rss_mb). Growth is judged on the medians of the minute around 5 min and around the end.

    Passes when the app's median grew by 10 % or less (a drop is fine) and the worker's peak stays under 2 GB.
    Short runs (under 30 min) use the point one sixth of the way in instead of 5 min.
    """
    t5 = min(300.0, total_s / 6)
    w = min(60.0, t5)
    m5, m_end = median_at(rows, t5, w), median_at(rows, total_s, w)
    growth = (m_end - m5) / m5 * 100
    ok = growth <= 10 and peak_mb < 2048
    return ok, (f"app_median_5min_mb={m5:.1f} app_median_end_mb={m_end:.1f} app_growth_pct={growth:+.1f} "
                f"worker_peak_mb={peak_mb:.1f}")


if len(sys.argv) > 2 and sys.argv[1] == "--judge":
    # soak.py --judge CSV [PEAK_MB]: judge a finished run's CSV. PEAK_MB defaults to the CSV's highest worker-tree row.
    with open(sys.argv[2]) as f:
        table = [line.strip().split(",") for line in f.read().splitlines()[1:] if line.strip()]
    csv_rows = [(float(r[0]), float(r[1])) for r in table]
    peak = float(sys.argv[3]) if len(sys.argv) > 3 else max(float(r[3]) for r in table)
    ok, text = memory_verdict(csv_rows, csv_rows[-1][0], peak)
    print(f"SUMMARY verdict={'PASS' if ok else 'FAIL'} {text} calls={table[-1][4]}")
    sys.exit(0 if ok else 1)

port, token, app_pid, csv_path, map_folder, save_path, pid_list = sys.argv[1:8]
total_s = float(sys.argv[8]) if len(sys.argv) > 8 else 1800.0
drive = sys.argv[9] if len(sys.argv) > 9 else "floor"
if drive not in ("floor", "engine", "phase3"):
    sys.exit(f"unknown drive {drive!r}: floor, engine or phase3")
app_pid = int(app_pid)
URL = f"http://127.0.0.1:{port}/mcp"
CALLS_EACH = 3600


def rpc(method, params, timeout=120):
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": method, "params": params}).encode()
    req = urllib.request.Request(URL, data=body, headers={
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
        "Authorization": "Bearer " + token,
    })
    with urllib.request.urlopen(req, timeout=timeout) as r:
        text = r.read().decode()
    for line in text.splitlines():
        if line.startswith("data:"):
            text = line[5:].strip()
            break
    return json.loads(text)


def call(tool, args, timeout=120):
    msg = rpc("tools/call", {"name": tool, "arguments": args}, timeout)
    if "error" in msg:
        return False, json.dumps(msg["error"])
    res = msg["result"]
    return not res.get("isError", False), "".join(c.get("text", "") for c in res.get("content", []) if c.get("type") == "text")


def rss_kb(pid):
    try:
        with open(f"/proc/{pid}/status") as f:
            for line in f:
                if line.startswith("VmRSS:"):
                    return int(line.split()[1])
    except OSError:
        pass
    return 0


def children_map():
    kids = {}
    for name in os.listdir("/proc"):
        if not name.isdigit():
            continue
        try:
            with open(f"/proc/{name}/stat") as f:
                stat = f.read()
            ppid = int(stat[stat.rindex(")") + 2:].split()[1])
        except (OSError, ValueError):
            continue
        kids.setdefault(ppid, []).append(int(name))
    return kids


def descendants(pid, kids):
    out, todo = [], [pid]
    while todo:
        for c in kids.get(todo.pop(), []):
            out.append(c)
            todo.append(c)
    return out


def cmdline(pid):
    try:
        with open(f"/proc/{pid}/cmdline", "rb") as f:
            return f.read().replace(b"\0", b" ").decode(errors="replace")
    except OSError:
        return ""


seen = set()
recorded = set()


def record(pids):
    """Appends pids not yet in PIDLIST (so a crash or kill later still leaves the list for the cleanup)."""
    new = set(pids) - recorded
    if new:
        with open(pid_list, "a") as f:
            f.write("".join(f"{p}\n" for p in sorted(new)))
        recorded.update(new)
state = {"worker": None, "peak_kb": 0, "app_peak_kb": 0, "stop": False}


def sample():
    """Returns (worker_pid, worker_kb, worker_tree_kb)."""
    kids = children_map()
    tree = descendants(app_pid, kids)
    seen.update(tree)
    seen.add(app_pid)
    record(seen)
    worker = state["worker"]
    if worker is None or not os.path.exists(f"/proc/{worker}"):
        worker = next((p for p in tree if "worker.py" in cmdline(p) and "systemd-run" not in cmdline(p)), None)
        state["worker"] = worker
    if worker is None:
        return None, 0, 0
    sub = [worker] + descendants(worker, kids)
    kb = sum(rss_kb(p) for p in sub)
    return worker, rss_kb(worker), kb


def watch():
    while not state["stop"]:
        _, _, kb = sample()
        state["peak_kb"] = max(state["peak_kb"], kb)
        state["app_peak_kb"] = max(state["app_peak_kb"], rss_kb(app_pid))
        time.sleep(1)


threading.Thread(target=watch, daemon=True).start()

# The engine drive: (recipe, how to start it). The oak alternates with the other seven, one at a time.
ENGINE_OAK = ("painted-tree", "oak")
ENGINE_OTHERS = [("bush", "round-bush"), ("cut-rock", "boulder"), ("grass", "long-grass"), ("plant", "flower-clump"),
                 ("cactus", "saguaro"), ("branching-tree", "round-canopy"), ("palm-tree", None)]
LEVELS = ["lod-0", "lod-1", "lod-2", "lod-far"]


def engine_state():
    ok, text = call("get_state", {})
    return json.loads(text) if ok else {}


def built_ms(status):
    """The milliseconds of a status line such as 'Tree built in 506 ms.', or None."""
    words = status.split()
    if "built" in words and words[-1].startswith("ms") and words[-2].isdigit():
        return int(words[-2])
    return None


def sliders():
    ok, text = call("list_controls", {})
    if not ok:
        return []
    return [c for c in json.loads(text)
            if c["type"] in ("float", "int") and "min" in c and c.get("kind") == "paint"]


def engine_drive():
    errors = []

    def fail(what, text):
        errors.append(f"{what}: {text[:200]}")
        print("FAIL", what, text[:300], flush=True)

    def path_of(kind):
        # One work file per kind: an old-style tree and a painted tree share the model name pg_<file>_tree, and the
        # app rightly refuses to export one over the other's catalog entry.
        root, ext = os.path.splitext(save_path)
        return f"{root}-{kind[0]}{ext or '.propgen'}"

    def switch(kind, before):
        recipe, preset = kind
        ok, text = call("save", {"path": path_of(before), "overwrite": True})
        if not ok:
            fail("save before switch", text)
        ok, text = (call("start_from_preset", {"id": preset}) if preset
                    else call("run_command", {"id": "new-palm"}))
        if not ok:
            fail(f"start {recipe}", text)
        return sliders()

    start = time.time()
    rows, builds = [], []
    peaks = {"heap": 0, "external": 0, "textures": 0, "generation": 0}
    calls = sets = 0
    with open(csv_path, "w") as csv:
        csv.write("t_s,app_rss_mb,worker_rss_mb,worker_tree_rss_mb,calls,engine_heap_mb,engine_external_mb,"
                  "texture_mb,generation,recipe,status\n")

        def row(recipe):
            st = engine_state()
            e = st.get("engine", {})
            heap, ext, tex, gen = (e.get("heap_bytes", 0), e.get("external_bytes", 0), e.get("texture_bytes", 0),
                                   e.get("generation", 0))
            peaks.update(heap=max(peaks["heap"], heap), external=max(peaks["external"], ext),
                         textures=max(peaks["textures"], tex), generation=max(peaks["generation"], gen))
            status = st.get("status_line", "")
            ms = built_ms(status)
            if ms is not None:
                builds.append((st.get("recipe"), ms))
            if "error" in e:
                fail("engine state", e["error"])
            t = time.time() - start
            app_mb = rss_kb(app_pid) / 1024
            _, wk, tk = sample()
            line = (f"{t:.0f},{app_mb:.1f},{wk / 1024:.1f},{tk / 1024:.1f},{calls},{heap / 2**20:.1f},"
                    f"{ext / 2**20:.1f},{tex / 2**20:.1f},{gen},{recipe},\"{status.replace(chr(34), chr(39))}\"")
            csv.write(line + "\n")
            csv.flush()
            rows.append((t, app_mb))
            print(line, flush=True)

        phase, other = 0, 0
        kind = ENGINE_OAK
        controls = switch(kind, kind)
        phase_end = start + 90
        next_row, next_level, next_export, level = start, start + 10, start + 60, 0
        n = 0
        while time.time() - start < total_s:
            now = time.time()
            if now >= phase_end:
                phase += 1
                before = kind
                if phase % 2:
                    kind = ENGINE_OTHERS[other % len(ENGINE_OTHERS)]
                    other += 1
                else:
                    kind = ENGINE_OAK
                controls = switch(kind, before)
                calls += 2
                phase_end = now + (90 if kind is ENGINE_OAK else 45)
                level = 0
            if now >= next_row:
                row(kind[0])
                next_row += 10
            if now >= next_level:
                level = (level + 1) % len(LEVELS)
                ok, text = call("run_command", {"id": LEVELS[level]})
                calls += 1
                if not ok and "cannot run now" not in text and "no command" not in text:
                    fail(LEVELS[level], text)
                next_level += 10
            if now >= next_export:
                ok, text = call("save", {"path": path_of(kind), "overwrite": True})
                if not ok:
                    fail("save", text)
                ok, text = call("export", {"mapFolder": map_folder}, timeout=300)
                if not ok:
                    fail("export", text)
                calls += 2
                next_export += 60
            if controls:
                c = controls[n % len(controls)]
                ok, text = call("set_control", {"key": c["key"], "value": sweep(c, n // len(controls))})
                calls += 1
                sets += 1
                if not ok:
                    fail(f"set {kind[0]}.{c['key']}", text)
                n += 1
            time.sleep(0.25)
        row(kind[0])
        ok_save, t1 = call("save", {"path": path_of(kind), "overwrite": True})
        ok_exp, t2 = call("export", {"mapFolder": map_folder}, timeout=300)
        print("save:", ok_save, t1[:200], "\nexport:", ok_exp, t2[:300], flush=True)
        time.sleep(2)
        row(kind[0])

    state["stop"] = True
    mem_ok, mem_text = memory_verdict(rows, total_s, state["peak_kb"] / 1024)
    times = sorted(ms for _, ms in builds)
    slowest = max(builds, key=lambda b: b[1]) if builds else ("-", 0)
    build_text = (f"builds_seen={len(times)} build_median_ms={times[len(times) // 2] if times else 0} "
                  f"build_max_ms={slowest[1]} ({slowest[0]})")
    verdict = "PASS" if mem_ok and ok_save and ok_exp and not errors else "FAIL"
    print(f"SUMMARY verdict={verdict} drive=engine calls={calls} set_control={sets} failed={len(errors)} {mem_text} "
          f"app_peak_mb={state['app_peak_kb'] / 1024:.1f} engine_heap_peak_mb={peaks['heap'] / 2**20:.1f} "
          f"engine_external_peak_mb={peaks['external'] / 2**20:.1f} texture_peak_mb={peaks['textures'] / 2**20:.1f} "
          f"generation_max={peaks['generation']} {build_text} save_ok={ok_save} export_ok={ok_exp} "
          f"first_fail={(errors[0] if errors else '')!r}")
    return 0 if verdict == "PASS" else 1

# The phase3 drive's card flows: places in the 1600x1000 Xvfb window (the app's window opens at the same place each run,
# its top left at 10,10). Measured on feat/common-ui (top bar 40, the File menu after the app's name, the view's tools
# under the history strip, cards in the shared design) with soak.sh's own folders, whose length sets how the card's
# "The set goes in" line wraps and so where the set cards' buttons sit.
# The slope tool is clicked with the history strip showing (slope() makes a change first): the strip moves the tools
# down 32.
SLOPE_TOOL, SLOPE_BOX, DOWNHILL_BOX = (926, 109), (887, 148), (887, 184)
PIECES_MAKE = (894, 435)
SHOWMORE_DAMAGED = (698, 178)
MANY_COUNT, MANY_SET, MANY_EXPORT = (722, 227), (683, 315), (907, 643)
FILE_MENU, MY_PROPS = (181, 30), (225, 203)
PROPS_ROWS = [(366, 309), (366, 355), (366, 401)]
PROPS_AS_SET = (955, 392)  # below Export… and Send to Groundwork
SETCARD_PALETTE, SETCARD_EXPORT = (765, 428), (927, 674)
SET_NAMES = [("aaa-1-oak", "oak"), ("aaa-2-pear", "pear"), ("aaa-3-boulder", "boulder")]
SHORT = {"painted-tree": "oak", "bush": "bush", "cut-rock": "rock", "grass": "grass", "plant": "plant",
         "cactus": "cactus", "branching-tree": "oldtree", "palm-tree": "palm"}


def xdo(*args):
    import subprocess
    subprocess.run(["xdotool", *[str(a) for a in args]], check=False, capture_output=True)


def click(xy, pause=0.6):
    xdo("mousemove", xy[0], xy[1], "click", "1")
    time.sleep(pause)


def type_into(xy, text, end="Return"):
    xdo("mousemove", xy[0], xy[1], "click", "--repeat", "3", "1")
    xdo("key", "ctrl+a")
    xdo("type", text)
    xdo("key", end)
    time.sleep(0.8)


def escape(times=1):
    for _ in range(times):
        xdo("key", "Escape")
        time.sleep(0.4)


def newest(folder, suffix):
    """The newest mtime of a file ending in suffix under folder (recursive), or 0."""
    best = 0.0
    for root, _, files in os.walk(folder):
        for f in files:
            if f.endswith(suffix):
                best = max(best, os.path.getmtime(os.path.join(root, f)))
    return best


def phase3_drive():
    errors = []
    work = os.path.dirname(save_path)
    set_folder = os.path.join(work, "sets")
    counts = {"pieces": 0, "breaks": 0, "slopes": 0, "exports": 0, "damaged": 0, "card_sets": 0, "props_sets": 0,
              "mossfrom": 0, "piece_files": 0}
    flow_s = {k: [] for k in ("pieces", "card_sets", "props_sets", "exports")}

    def fail(what, text):
        errors.append(f"{what}: {text[:200]}")
        print("FAIL", what, text[:300], flush=True)

    def path_of(kind):
        return os.path.join(work, SHORT[kind[0]] + ".propgen")

    def must(what, ok_text):
        ok, text = ok_text
        if not ok:
            fail(what, text)
        return ok, text

    # The three props of Your props' set, named so they sort first by name (soak.sh sets propsSort to name).
    for name, preset in SET_NAMES:
        must(f"start {preset}", call("start_from_preset", {"id": preset}))
        time.sleep(2)
        must(f"save {name}", call("save", {"path": os.path.join(work, name + ".propgen"), "overwrite": True}))

    def switch(kind, before):
        recipe, preset = kind
        if before is not None:
            must("save before switch", call("save", {"path": path_of(before), "overwrite": True}))
        must(f"start {recipe}", call("start_from_preset", {"id": preset}) if preset
             else call("run_command", {"id": "new-palm"}))
        time.sleep(1)
        ok, text = call("list_controls", {})
        controls = json.loads(text) if ok else []
        must(f"save {recipe}", call("save", {"path": path_of(kind), "overwrite": True}))
        return controls

    def slope(controls):
        n = counts["slopes"]
        # One change first, so the history strip shows (a piece just started has a single step and no strip) and the
        # slope tool is where SLOPE_TOOL says.
        sliders_now = [c for c in controls if c["type"] in ("float", "int") and "min" in c and c.get("kind") == "paint"]
        if sliders_now:
            c = sliders_now[n % len(sliders_now)]
            must("change before slope", call("set_control", {"key": c["key"], "value": sweep(c, n + 7)}))
            time.sleep(0.5)
        click(SLOPE_TOOL)
        type_into(SLOPE_BOX, str((n * 7) % 36))
        type_into(DOWNHILL_BOX, str((n * 45) % 360))
        escape()
        counts["slopes"] += 1

    def new_textures(kind, controls):
        """Changes what the 2048 pictures depend on, so each export asks upstream's caches for new ones."""
        keys = {c["key"]: c for c in controls}
        n = counts["exports"]
        if kind[0] == "cut-rock":
            for key, step in (("grain", 0.25), ("lichen", 0.25)):
                if key in keys:
                    v = round(((n * step) if key == "grain" else ((n // 5) * step)) % 1.25, 2)
                    must(f"set {key}", call("set_control", {"key": key, "value": min(v, 1.0)}))
        elif kind[0] == "painted-tree" and "barkPattern" in keys:
            opts = [o["value"] for o in keys["barkPattern"].get("options", [])]
            if opts:
                must("set barkPattern", call("set_control", {"key": "barkPattern", "value": opts[n % len(opts)]}))

    def export(kind, controls):
        new_textures(kind, controls)
        must("save", call("save", {"path": path_of(kind), "overwrite": True}))
        t0 = time.time()
        if must("export", call("export", {"mapFolder": map_folder}, timeout=300))[0]:
            flow_s["exports"].append(time.time() - t0)
        counts["exports"] += 1

    def part_and_break():
        ok, text = call("list_parts", {})
        if not ok:
            fail("list_parts", text)
            return
        parts = json.loads(text)
        clumps = [p["id"] for p in parts if p["type"] == "foliage-cluster"]
        if clumps:
            must("pick", call("pick_part", {"id": clumps[counts["breaks"] % len(clumps)]}))
            if must("move", call("move_part", {"x": 0.2, "y": 0, "z": 0.1}))[0]:
                must("undo move", call("undo", {}))
        limbs = [p["id"] for p in parts if p["type"] == "branch"]
        for limb in limbs[:6]:
            must("pick", call("pick_part", {"id": limb}))
            ok, text = call("run_command", {"id": "break-part"})
            if ok:
                must("undo break", call("undo", {}))
                counts["breaks"] += 1
                break
            if "cannot run now" not in text:
                fail("break", text)
                break
        call("pick_part", {"id": ""})

    def show_damaged():
        must("show-more", call("run_command", {"id": "show-more"}))
        time.sleep(2)
        click(SHOWMORE_DAMAGED)
        time.sleep(14)
        escape()
        counts["damaged"] += 1

    def pieces(kind):
        must("save", call("save", {"path": path_of(kind), "overwrite": True}))
        stem = SHORT[kind[0]]
        before = set(os.listdir(work))
        t0 = time.time()
        if not must("make-pieces", call("run_command", {"id": "make-pieces"}))[0]:
            return
        time.sleep(2)
        click(PIECES_MAKE)
        # The card offers only the pieces this tree has (a tree swept to no heavy branch has no fallen branch), so
        # the flow waits until the files stop coming and needs at least one.
        made, still = [], 0
        for _ in range(60):
            now_made = [f for f in set(os.listdir(work)) - before if f.startswith(stem + " ")]
            still = still + 1 if now_made and len(now_made) == len(made) else 0
            made = now_made
            if len(made) >= 4 or still >= 4:
                break
            time.sleep(1)
        time.sleep(3)
        escape()
        if not made:
            fail("make pieces", "no piece file written")
        else:
            flow_s["pieces"].append(time.time() - t0)
            counts["pieces"] += 1
            counts["piece_files"] += len(made)
        for f in made:
            os.remove(os.path.join(work, f))

    def card_set():
        before = newest(set_folder, ".set.json")
        t0 = time.time()
        if not must("export-many", call("run_command", {"id": "export-many"}))[0]:
            return
        time.sleep(2)
        type_into(MANY_COUNT, "2", "Tab")
        click(MANY_SET)
        time.sleep(12)
        click(MANY_EXPORT)
        for _ in range(180):
            if newest(set_folder, ".set.json") > before:
                break
            time.sleep(1)
        else:
            fail("card set", "no new .set.json in " + set_folder)
            escape(2)
            return
        flow_s["card_sets"].append(time.time() - t0)
        counts["card_sets"] += 1
        time.sleep(1)

    def props_set():
        before = newest(set_folder, ".set.json")
        t0 = time.time()
        click(FILE_MENU, 0.8)
        click(MY_PROPS, 2.5)
        for row in PROPS_ROWS:
            click(row, 0.4)
        click(PROPS_AS_SET, 3)
        # A colour change while the previews draw.
        click(SETCARD_PALETTE, 0.6)
        xdo("key", "Down", "Down", "Return")
        time.sleep(1.5)
        click(SETCARD_EXPORT)
        for _ in range(180):
            if newest(set_folder, ".set.json") > before:
                break
            time.sleep(1)
        else:
            fail("props set", "no new .set.json in " + set_folder)
            escape(3)
            return
        time.sleep(1)
        escape(2)
        flow_s["props_sets"].append(time.time() - t0)
        counts["props_sets"] += 1

    start = time.time()
    rows, builds = [], []
    peaks = {"heap": 0, "external": 0, "textures": 0, "generation": 0}
    gens = []
    calls = sets = 0
    with open(csv_path, "w") as csv:
        csv.write("t_s,app_rss_mb,worker_rss_mb,worker_tree_rss_mb,calls,engine_heap_mb,engine_external_mb,"
                  "texture_mb,generation,recipe,status\n")

        def row(recipe):
            st = engine_state()
            e = st.get("engine", {})
            heap, ext, tex, gen = (e.get("heap_bytes", 0), e.get("external_bytes", 0), e.get("texture_bytes", 0),
                                   e.get("generation", 0))
            peaks.update(heap=max(peaks["heap"], heap), external=max(peaks["external"], ext),
                         textures=max(peaks["textures"], tex), generation=max(peaks["generation"], gen))
            gens.append(gen)
            status = st.get("status_line", "")
            ms = built_ms(status)
            if ms is not None:
                builds.append((st.get("recipe"), ms))
            if "error" in e:
                fail("engine state", e["error"])
            t = time.time() - start
            app_mb = rss_kb(app_pid) / 1024
            _, wk, tk = sample()
            line = (f"{t:.0f},{app_mb:.1f},{wk / 1024:.1f},{tk / 1024:.1f},{calls},{heap / 2**20:.1f},"
                    f"{ext / 2**20:.1f},{tex / 2**20:.1f},{gen},{recipe},\"{status.replace(chr(34), chr(39))}\"")
            csv.write(line + "\n")
            csv.flush()
            rows.append((t, app_mb))
            print(line, flush=True)

        phase, other = 0, 0
        kind = ENGINE_OAK
        controls = switch(kind, None)
        now = time.time()
        phase_end = now + 90
        due = {"row": start, "level": start + 10, "export": start + 60, "slope": start + 30, "moss": start + 20,
               "break": start + 40, "damaged": start + 120, "pieces": start + 150, "sets": start + 240}
        level = 0
        n = 0
        while time.time() - start < total_s:
            now = time.time()
            oak = kind is ENGINE_OAK
            if now >= phase_end:
                phase += 1
                before = kind
                if phase % 2:
                    kind = ENGINE_OTHERS[other % len(ENGINE_OTHERS)]
                    other += 1
                else:
                    kind = ENGINE_OAK
                controls = switch(kind, before)
                calls += 3
                phase_end = time.time() + (90 if kind is ENGINE_OAK else 45)
                level = 0
                continue
            if now >= due["row"]:
                row(kind[0])
                due["row"] += 10
            if now >= due["level"]:
                level = (level + 1) % len(LEVELS)
                ok, text = call("run_command", {"id": LEVELS[level]})
                calls += 1
                if not ok and "cannot run now" not in text and "no command" not in text:
                    fail(LEVELS[level], text)
                due["level"] += 10
            if now >= due["moss"]:
                if any(c["key"] == "mossFrom" for c in controls):
                    must("mossFrom", call("set_control", {"key": "mossFrom",
                                                          "value": "shade" if counts["mossfrom"] % 2 == 0 else "sky"}))
                    counts["mossfrom"] += 1
                    calls += 1
                due["moss"] += 20
            if now >= due["slope"]:
                slope(controls)
                due["slope"] += 30
            if now >= due["export"]:
                export(kind, controls)
                calls += 4
                due["export"] += 60
            if oak and now >= due["break"]:
                part_and_break()
                calls += 6
                due["break"] = max(due["break"] + 60, now)
            if oak and now >= due["damaged"]:
                show_damaged()
                calls += 1
                due["damaged"] = max(due["damaged"] + 120, now)
            if oak and now >= due["pieces"]:
                pieces(kind)
                calls += 2
                due["pieces"] = max(due["pieces"] + 300, now)
            if oak and now >= due["sets"]:
                card_set()
                props_set()
                calls += 1
                due["sets"] = max(due["sets"] + 300, now)
            sliders_now = [c for c in controls
                           if c["type"] in ("float", "int") and "min" in c and c.get("kind") == "paint"]
            if sliders_now:
                c = sliders_now[n % len(sliders_now)]
                ok, text = call("set_control", {"key": c["key"], "value": sweep(c, n // len(sliders_now))})
                calls += 1
                sets += 1
                if not ok:
                    fail(f"set {kind[0]}.{c['key']}", text)
                n += 1
            time.sleep(0.25)
        row(kind[0])
        ok_save, t1 = call("save", {"path": path_of(kind), "overwrite": True})
        ok_exp, t2 = call("export", {"mapFolder": map_folder}, timeout=300)
        print("save:", ok_save, t1[:200], "\nexport:", ok_exp, t2[:300], flush=True)
        time.sleep(2)
        row(kind[0])

    state["stop"] = True
    mem_ok, mem_text = memory_verdict(rows, total_s, state["peak_kb"] / 1024)
    times = sorted(ms for _, ms in builds)
    slowest = max(builds, key=lambda b: b[1]) if builds else ("-", 0)
    recycles = sum(1 for a, b in zip(gens, gens[1:]) if b > a)

    def med(v):
        v = sorted(v)
        return f"{v[len(v) // 2]:.1f}" if v else "-"

    flows = " ".join(f"{k}_median_s={med(v)} {k}_max_s={max(v) if v else 0:.1f}" for k, v in flow_s.items())
    verdict = "PASS" if mem_ok and ok_save and ok_exp and not errors else "FAIL"
    print(f"SUMMARY verdict={verdict} drive=phase3 calls={calls} set_control={sets} failed={len(errors)} {mem_text} "
          f"app_peak_mb={state['app_peak_kb'] / 1024:.1f} engine_heap_peak_mb={peaks['heap'] / 2**20:.1f} "
          f"engine_external_peak_mb={peaks['external'] / 2**20:.1f} texture_peak_mb={peaks['textures'] / 2**20:.1f} "
          f"generation_max={peaks['generation']} generation_steps_seen={recycles} "
          f"builds_seen={len(times)} build_median_ms={times[len(times) // 2] if times else 0} "
          f"build_max_ms={slowest[1]} ({slowest[0]}) {' '.join(f'{k}={v}' for k, v in counts.items())} {flows} "
          f"save_ok={ok_save} export_ok={ok_exp} first_fail={(errors[0] if errors else '')!r}")
    return 0 if verdict == "PASS" else 1


ok, text = call("list_controls", {})
if not ok:
    sys.exit("list_controls failed: " + text)
controls = json.loads(text)
if isinstance(controls, dict):
    controls = controls.get("controls", list(controls.values())[0])
numeric = [c for c in controls if c["type"] in ("float", "int") and "min" in c]
paint = [c for c in numeric if c["kind"] == "paint"]
tone = [c for c in numeric if c["kind"] == "tone"]
print(f"paint controls: {[c['key'] for c in paint]}; tone controls: {[c['key'] for c in tone]}", flush=True)


def sweep(c, n):
    lo, hi = float(c["min"]), float(c["max"])
    phase = (n % 40) / 20.0
    f = phase if phase <= 1 else 2 - phase
    v = lo + (hi - lo) * f
    if c["type"] == "int":
        return int(round(v))
    step = float(c.get("step") or 0)
    return round(round(v / step) * step, 6) if step else round(v, 6)


if drive == "engine":
    sys.exit(engine_drive())
if drive == "phase3":
    sys.exit(phase3_drive())

start = time.time()
fails = 0
first_fail = ""
rows = []
next_row = start
with open(csv_path, "w") as csv:
    csv.write("t_s,app_rss_mb,worker_rss_mb,worker_tree_rss_mb,calls\n")

    def row(calls):
        w, wk, tk = sample()
        t = time.time() - start
        line = f"{t:.0f},{rss_kb(app_pid) / 1024:.1f},{wk / 1024:.1f},{tk / 1024:.1f},{calls}"
        csv.write(line + "\n")
        csv.flush()
        rows.append((t, rss_kb(app_pid) / 1024))
        print(line, flush=True)

    calls = 0
    for i in range(CALLS_EACH * 2):
        due = start + i * (total_s / (CALLS_EACH * 2))
        delay = due - time.time()
        if delay > 0:
            time.sleep(delay)
        if time.time() >= next_row:
            row(calls)
            next_row += 10
        group = paint if i % 2 == 0 else tone
        c = group[(i // 2) % len(group)]
        ok, text = call("set_control", {"key": c["key"], "value": sweep(c, i // 2 // len(group))})
        calls += 1
        if not ok:
            fails += 1
            first_fail = first_fail or f"{c['key']}: {text[:200]}"
    row(calls)

    ok1, t1 = call("save", {"path": save_path, "overwrite": True})
    ok2, t2 = call("export", {"mapFolder": map_folder}, timeout=300)
    print("save:", ok1, t1[:200], "\nexport:", ok2, t2[:300], flush=True)
    time.sleep(2)
    row(calls)

state["stop"] = True
mem_ok, mem_text = memory_verdict(rows, total_s, state["peak_kb"] / 1024)
verdict = "PASS" if mem_ok and ok1 and ok2 and fails == 0 else "FAIL"
print(f"SUMMARY verdict={verdict} calls={calls} failed={fails} {mem_text} "
      f"save_ok={ok1} export_ok={ok2} first_fail={first_fail!r}")
sys.exit(0 if verdict == "PASS" else 1)
