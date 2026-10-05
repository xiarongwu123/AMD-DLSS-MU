#!/usr/bin/env python3
"""Export Postfix delivery metadata; never export message bodies or Docker access."""
import datetime
import fcntl
import json
import os
from pathlib import Path
import re
import subprocess
import time

MESSAGE = re.compile(r"postfix/cleanup\[\d+\]: ([A-Za-z0-9]+): message-id=<mu\.([a-f0-9]{32})@smtp\.claude-api\.cn>")
DELIVERY = re.compile(r"postfix/(?:smtp|error|qmgr)\[\d+\]: ([A-Za-z0-9]+): .*\bstatus=(sent|deferred|bounced) \((.*)\)$")
EXPIRED = re.compile(r"postfix/qmgr\[\d+\]: ([A-Za-z0-9]+): .*\bstatus=expired")
STATES = {"sent": "delivered", "deferred": "deferred", "bounced": "bounced"}


def collect(lines, previous, now):
    cutoff = now - 172800
    mapping = {k: v for k, v in previous.get("mapping", {}).items() if v["at"] >= cutoff}
    events = {v["id"]: v for v in previous.get("events", []) if v["at"] >= cutoff}
    for line in lines:
        try:
            stamp = line.split()[0]
            # Python 3.10 accepts microseconds, while Docker timestamps include nanoseconds.
            stamp = re.sub(r"(\.\d{6})\d+", r"\1", stamp)
            at = int(datetime.datetime.fromisoformat(stamp.replace("Z", "+00:00")).timestamp())
        except (ValueError, IndexError):
            if MESSAGE.search(line) or DELIVERY.search(line) or EXPIRED.search(line):
                raise ValueError("Unrecognized timestamp on a Postfix delivery log line")
            continue
        match = MESSAGE.search(line)
        if match:
            queue, request = match.groups()
            mapping[queue] = {"id": request, "at": at}
        match = DELIVERY.search(line)
        expired = EXPIRED.search(line)
        if not match and not expired:
            continue
        queue, status, detail = match.groups() if match else (expired.group(1), "bounced", "Queue lifetime exceeded; returned to sender")
        if queue not in mapping or at < mapping[queue]["at"]:
            continue
        request = mapping[queue]["id"]
        old = events.get(request)
        if old and (old["at"] > at or old["status"] in ("delivered", "bounced")):
            continue
        events[request] = {"id": request, "queueId": queue, "status": STATES[status], "at": at, "detail": detail[:500]}
    return {"collectedAt": now, "mapping": dict(sorted(mapping.items(), key=lambda x: x[1]["at"])[-10000:]),
            "events": sorted(events.values(), key=lambda x: x["at"])[-10000:]}


def main():
    root = Path(__file__).resolve().parent
    destination = root / "data" / "smtp-events.json"
    os.umask(0o077)
    with (root / "data" / ".mail-collector.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return
        previous = json.loads(destination.read_text()) if destination.exists() else {}
        # Re-read overlap to recover from short outages. Docker logging is bounded at 3 x 5 MB.
        result = subprocess.run(["docker", "logs", "--timestamps", "--since", "24h", "amd-dlss-mu-smtp"],
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=30, check=True)
        snapshot = collect(result.stdout.splitlines(), previous, int(time.time()))
        temporary = destination.with_suffix(".next")
        with temporary.open("w") as output:
            json.dump(snapshot, output, separators=(",", ":"))
        os.replace(temporary, destination)


if __name__ == "__main__":
    main()
