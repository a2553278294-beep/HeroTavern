"""Pure-function unit tests for workflow_lib.core (no Unity/Git dependency)."""

import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

from workflow_lib.core import (  # noqa: E402
    Context, WorkflowError, aggregate, canonical, decode_output, digest, redact, within,
)


class RedactTests(unittest.TestCase):
    def test_secret_keys_masked_in_dicts(self):
        data = {"token": "abc123", "nested": {"password": "p"}, "safe": "ok"}
        result = redact(data)
        self.assertEqual(result["token"], "[REDACTED]")
        self.assertEqual(result["nested"]["password"], "[REDACTED]")
        self.assertEqual(result["safe"], "ok")

    def test_bearer_and_assignment_text_masked(self):
        self.assertNotIn("s3cr3t", redact("Authorization: Bearer s3cr3t"))
        self.assertNotIn("hunter2", redact('password: "hunter2"'))

    def test_json_string_redacted_recursively(self):
        result = redact(json.dumps({"api_key": "k", "list": [{"secret": "s"}]}))
        self.assertNotIn('"k"', result)
        self.assertEqual(json.loads(result)["list"][0]["secret"], "[REDACTED]")


class PathTests(unittest.TestCase):
    def test_within_accepts_child_and_rejects_escape(self):
        with tempfile.TemporaryDirectory() as root:
            child = Path(root) / "a" / "b.txt"
            self.assertEqual(within(child, root), child.resolve())
            with self.assertRaises(WorkflowError):
                within(Path(root).parent / "outside.txt", root)

    def test_canonical_digest_deterministic(self):
        self.assertEqual(canonical({"b": 1, "a": 2}), canonical({"a": 2, "b": 1}))
        self.assertEqual(digest({"x": [1, 2]}), digest({"x": [1, 2]}))
        self.assertNotEqual(digest({"x": 1}), digest({"x": 2}))

    def test_decode_output_utf8_sig(self):
        self.assertEqual(decode_output(b"\xef\xbb\xbfhello"), "hello")


class AggregateTests(unittest.TestCase):
    def step(self, status, required=True):
        return {"status": status, "required": required}

    def test_failed_dominates(self):
        self.assertEqual(aggregate([self.step("passed"), self.step("failed", required=False)]), "failed")

    def test_required_not_passed_is_blocked(self):
        self.assertEqual(aggregate([self.step("passed"), self.step("blocked")]), "blocked")
        self.assertEqual(aggregate([]), "blocked")

    def test_all_required_passed(self):
        self.assertEqual(aggregate([self.step("passed"), self.step("skipped", required=False)]), "passed")


class ContextTests(unittest.TestCase):
    def test_workflow_paths_use_agent_neutral_directory(self):
        project = Path(__file__).resolve().parents[2]
        context = Context(project)
        self.assertEqual(context.scripts, project / ".agents" / "scripts")
        self.assertEqual(context.runs, project / ".agents" / "runs")
        self.assertNotIn(".codex", str(context.scripts))
        self.assertTrue((project / ".agents" / "skills").is_dir())
        self.assertFalse((project / ".codex").exists())

    def test_non_project_directory_blocked(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(WorkflowError) as caught:
                Context(temp)
            self.assertEqual(caught.exception.status, "blocked")


if __name__ == "__main__":
    unittest.main()
