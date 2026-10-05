import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("collector", Path(__file__).with_name("collect-mail-events.py"))
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)


class CollectorTests(unittest.TestCase):
    def test_correlation_and_replay(self):
        identifier = "a" * 32
        lines = [
            f"2026-10-05T06:00:00.000000000Z INFO postfix/cleanup[1]: ABC123: message-id=<mu.{identifier}@smtp.claude-api.cn>",
            "2026-10-05T06:00:01Z INFO postfix/smtp[2]: OTHER: to=<x@example.test>, status=sent (250 accepted)",
            "2026-10-05T06:00:02Z INFO postfix/smtp[2]: ABC123: to=<x@example.test>, status=deferred (451 try later)",
            "2026-10-05T06:00:03Z INFO postfix/qmgr[2]: ABC123: to=<x@example.test>, status=sent (250 accepted)",
        ]
        now = 1791180100
        result = collector.collect(lines, {}, now)
        self.assertEqual(len(result["events"]), 1)
        self.assertEqual(result["events"][0]["status"], "delivered")
        self.assertEqual(result["events"][0]["id"], identifier)
        self.assertNotIn("x@example.test", str(result))
        replay = collector.collect(lines, result, now)
        self.assertEqual(result, replay)

    def test_failure_and_unrelated_messages(self):
        identifier = "b" * 32
        lines = [
            f"2026-10-05T06:00:00Z INFO postfix/cleanup[1]: DEF123: message-id=<mu.{identifier}@smtp.claude-api.cn>",
            "2026-10-05T06:00:03Z INFO postfix/error[2]: DEF123: to=<x@example.test>, status=bounced (550 rejected)",
            "unparseable input",
        ]
        result = collector.collect(lines, {}, 1791180100)
        self.assertEqual(result["events"][0]["status"], "bounced")
        self.assertEqual(collector.collect([], result, 1791180100 + 172801)["events"], [])

    def test_invalid_delivery_timestamp_does_not_report_freshness(self):
        with self.assertRaises(ValueError):
            collector.collect(["invalid INFO postfix/smtp[1]: ABC: to=<x@y.test>, status=sent (250 ok)"], {}, 1791180100)


if __name__ == "__main__":
    unittest.main()
