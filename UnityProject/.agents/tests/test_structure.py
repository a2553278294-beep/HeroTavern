"""Skill-tree structure guards for the agent-neutral layout."""

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

from workflow_lib.checks import SKILLS  # noqa: E402


class SkillTreeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.project = Path(__file__).resolve().parents[2]
        cls.root = cls.project / ".agents"

    def test_exactly_the_maintained_skills_have_skill_md(self):
        discovered = sorted(path.parent.name for path in (self.root / "skills").glob("*/SKILL.md"))
        self.assertEqual(discovered, sorted(SKILLS))

    def test_no_vendor_specific_agent_metadata_remains(self):
        leftovers = list(self.root.rglob("openai.yaml")) + list(self.root.rglob(".codex*"))
        self.assertEqual(leftovers, [])

    def test_workflow_entrypoints_exist_under_agents(self):
        for relative in ("scripts/workflow.py", "templates/task.md", "evals/scenarios.json"):
            self.assertTrue((self.root / relative).is_file(), relative)


if __name__ == "__main__":
    unittest.main()
