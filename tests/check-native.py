"""Repository policy gate: widget rendering must remain native-only."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
SKIP_PARTS = {".git", "builds", "pkgs", "obj", "bin", "__pycache__"}
TEXT_SUFFIXES = {".cs", ".cpp", ".h", ".csproj", ".cmake", ".ps1", ".py", ".yml", ".yaml"}
forbidden = re.compile(r"webview|chromium|microsoft\.web\.webview2|--backend\s+html", re.I)
failures = []

for path in ROOT.rglob("*"):
    if not path.is_file() or SKIP_PARTS.intersection(path.parts):
        continue
    relative = path.relative_to(ROOT)
    if path.suffix.lower() == ".html":
        failures.append(f"browser document remains: {relative}")
    if path.suffix.lower() in TEXT_SUFFIXES and relative.as_posix() != "tests/check-native.py":
        text = path.read_text(encoding="utf-8", errors="replace")
        if forbidden.search(text):
            failures.append(f"browser rendering reference remains: {relative}")

if failures:
    print("native-only policy FAILED")
    for failure in failures: print(" -", failure)
    sys.exit(1)
print("native-only policy: OK")
