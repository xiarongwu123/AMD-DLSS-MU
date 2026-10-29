#!/usr/bin/env python3
"""Fetch release-pinned large build inputs without storing them in Git history."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import tempfile
import urllib.request
import zipfile

assets = Path(__file__).resolve().parents[1] / "assets"
manifest = json.loads((assets / "bundled-resources.json").read_text())

def valid(path, pin):
    if not path.is_file() or path.stat().st_size != pin["size"]:
        return False
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest() == pin["sha256"]

def promote(pin, write):
    target = assets / pin["name"]
    if valid(target, pin):
        print("Verified", target.name)
        return target
    fd, temp = tempfile.mkstemp(prefix=target.name + ".partial-", dir=assets)
    os.close(fd)
    try:
        write(Path(temp))
        if not valid(Path(temp), pin):
            raise ValueError("Size/SHA256 mismatch: " + target.name)
        os.replace(temp, target)
    finally:
        Path(temp).unlink(missing_ok=True)
    print("Prepared", target.name)
    return target

def download(path):
    with urllib.request.urlopen(manifest["package"]["url"], timeout=120) as source, path.open("wb") as output:
        shutil.copyfileobj(source, output)

package = promote(manifest["package"], download)
def extract(path):
    with zipfile.ZipFile(package) as archive, archive.open(manifest["model"]["entry"]) as source, path.open("wb") as output:
        shutil.copyfileobj(source, output)
promote(manifest["model"], extract)
