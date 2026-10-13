#!/usr/bin/env python3
"""Tiny UI driver for the Android smoke test (adb + uiautomator, no extra dependencies).

  ui.py wait  <text> [timeout_s]   wait until a view whose text contains <text> is on screen
  ui.py tap   <text> [timeout_s]   scroll until found, then tap it
  ui.py field <index> <value>      tap the n-th EditText on screen, clear it, type <value> (no spaces)
  ui.py shot  <file.png>           screenshot
  ui.py dump                       print visible texts
"""
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET


def nodes():
    for _ in range(5):
        subprocess.run(["adb", "shell", "uiautomator", "dump", "/sdcard/ui.xml"], capture_output=True)
        xml = subprocess.run(["adb", "shell", "cat", "/sdcard/ui.xml"], capture_output=True).stdout.decode("utf-8", "replace")
        start = xml.find("<?xml")
        if start >= 0:
            try:
                return list(ET.fromstring(xml[start:].strip()).iter("node"))
            except ET.ParseError:
                pass
        time.sleep(1)
    return []


def center(node):
    x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.get("bounds")))
    return (x1 + x2) // 2, (y1 + y2) // 2


def find(text):
    all_nodes = nodes()
    # a slow emulator sometimes shows "<app> isn't responding" for the launcher: keep waiting
    for n in all_nodes:
        if "isn't responding" in (n.get("text") or ""):
            for w in all_nodes:
                if (w.get("text") or "") == "Wait":
                    x, y = center(w)
                    subprocess.run(["adb", "shell", "input", "tap", str(x), str(y)])
                    time.sleep(1)
                    return None
    for n in all_nodes:
        if text in (n.get("text") or "") or text in (n.get("content-desc") or ""):
            return n
    return None


def hide_keyboard():
    out = subprocess.run(["adb", "shell", "dumpsys", "input_method"], capture_output=True).stdout.decode("utf-8", "replace")
    if "mInputShown=true" in out or "isInputViewShown=true" in out:
        subprocess.run(["adb", "shell", "input", "keyevent", "KEYCODE_BACK"])
        time.sleep(1)


def swipe_up():
    subprocess.run(["adb", "shell", "input", "swipe", "540", "1700", "540", "700", "400"])
    time.sleep(1)


def wait(text, timeout=30):
    end = time.time() + timeout
    while time.time() < end:
        if find(text) is not None:
            return True
        time.sleep(1)
    report(f"TIMEOUT waiting for '{text}'")
    return False


def tap(text, timeout=30):
    end = time.time() + timeout
    swipes = 0
    while time.time() < end:
        n = find(text)
        if n is not None:
            x, y = center(n)
            subprocess.run(["adb", "shell", "input", "tap", str(x), str(y)])
            time.sleep(1)
            return True
        if swipes < 8:
            swipe_up()
            swipes += 1
        else:
            time.sleep(1)
    report(f"TIMEOUT tapping '{text}'")
    return False


def field(index, value):
    """Types into the index-th text field; verifies the text landed (a half-visible field may miss the tap)."""
    for attempt in range(12):
        if any("isn't responding" in (n.get("text") or "") for n in nodes()):
            find("\u0000no-such-text\u0000")  # taps "Wait" on the system dialog
            time.sleep(3)
            continue
        edits = []
        for _ in range(8):
            all_nodes = nodes()
            if find("\u0000no-such-text\u0000") is None and any("isn't responding" in (n.get("text") or "") for n in all_nodes):
                continue  # find() tapped "Wait" on a system dialog; look again
            edits = [n for n in all_nodes if n.get("class") == "android.widget.EditText"]
            if len(edits) > index:
                break
            swipe_up()
        if len(edits) <= index:
            report(f"no text field #{index}")
            return False
        x, y = center(edits[index])
        if y < 300 or y > 2100:  # not comfortably on screen: bring it into view first
            subprocess.run(["adb", "shell", "input", "swipe", "540", "1500", "540", "1100", "300"])
            time.sleep(1)
            continue
        subprocess.run(["adb", "shell", "input", "tap", str(x), str(y)])
        time.sleep(0.8)
        subprocess.run(["adb", "shell", "input", "keyevent", "KEYCODE_MOVE_END"])
        subprocess.run(["adb", "shell", "input", "keyevent"] + ["KEYCODE_DEL"] * 40)
        subprocess.run(["adb", "shell", "input", "text", value])
        time.sleep(0.8)
        hide_keyboard()
        edits = [n for n in nodes() if n.get("class") == "android.widget.EditText"]
        if len(edits) > index and (edits[index].get("text") or "") == value:
            return True
    report(f"could not type into text field #{index}")
    return False


def report(message):
    """GitHub annotation with what the screen showed (readable without downloading the logs)."""
    texts = [n.get("text") for n in nodes() if n.get("text")]
    print("::error::" + message + " — on screen: " + " | ".join(texts)[:1500])
    shot("shots/failure.png")


def dump(out=sys.stdout):
    for n in nodes():
        t = n.get("text")
        if t:
            print("  " + t, file=out)


def shot(path):
    png = subprocess.run(["adb", "exec-out", "screencap", "-p"], capture_output=True).stdout
    with open(path, "wb") as f:
        f.write(png)
    return True


if __name__ == "__main__":
    cmd, args = sys.argv[1], sys.argv[2:]
    if cmd == "wait":
        ok = wait(args[0], int(args[1]) if len(args) > 1 else 30)
    elif cmd == "tap":
        ok = tap(args[0], int(args[1]) if len(args) > 1 else 30)
    elif cmd == "field":
        ok = field(int(args[0]), args[1])
    elif cmd == "shot":
        ok = shot(args[0])
    elif cmd == "dump":
        dump()
        ok = True
    else:
        raise SystemExit("unknown command " + cmd)
    sys.exit(0 if ok else 1)
