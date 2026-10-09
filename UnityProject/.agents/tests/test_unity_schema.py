"""Unity Pipeline discovery schema guards (0.3.x legacy vs >=0.6 compact listing)."""

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

from workflow_lib.core import WorkflowError  # noqa: E402
from workflow_lib.unity import command_names, decode_response  # noqa: E402


class DiscoverySchemaTests(unittest.TestCase):
    def test_compact_listing_schema(self):
        # unity command --detail compact (Pipeline 0.8.0): data unwraps to a commands array.
        envelope = {"success": True, "command": "command",
                    "data": {"target": {"host": "127.0.0.1", "port": 7800, "projectPath": "p"},
                             "server": {"version": "0.0.1", "port": 7800},
                             "commands": [{"name": "list_tests", "description": "", "tags": []},
                                          {"name": "run_tests", "description": "", "tags": []}],
                             "count": 2, "total": 160, "offset": 0}}
        names = command_names(decode_response(__import__("json").dumps(envelope)))
        self.assertEqual(names, {"list_tests", "run_tests"})

    def test_tags_only_listing_is_rejected(self):
        # Pipeline >= 0.6 default detail omits the command array; must not be mistaken for empty catalog.
        with self.assertRaises(WorkflowError) as caught:
            command_names({"target": {}, "server": {}, "tags": [{"tag": "build", "count": 10}],
                           "count": 0, "total": 160, "offset": 0})
        self.assertEqual(caught.exception.status, "blocked")

    def test_legacy_commands_and_tools_shapes(self):
        self.assertEqual(command_names({"commands": [{"name": "a"}]}), {"a"})
        self.assertEqual(command_names({"tools": [{"command": "b"}]}), {"b"})
        self.assertEqual(command_names([{"name": "c"}]), {"c"})


if __name__ == "__main__":
    unittest.main()
