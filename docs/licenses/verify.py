"""Verify the checked-in desktop notice inventory without network access."""
import hashlib
import json
import pathlib

root = pathlib.Path(__file__).resolve().parent
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
notices = {notice["file"]: notice for notice in manifest["notices"]}

for name, notice in notices.items():
    path = root / name
    if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != notice["sha256"]:
        raise SystemExit("Missing or changed license text: " + name)

for package in manifest["packages"]:
    for name in package["notice_files"]:
        if name not in notices:
            raise SystemExit("Unmapped dependency notice: " + package["package"] + " -> " + name)

print("PASS: " + str(len(manifest["packages"])) + " dependencies and "
      + str(len(notices)) + " complete license/notice texts verified.")
