from copy import deepcopy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("event_analysis", Path(__file__).resolve().parents[1] / "analyze-event-probes.py")
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)


def record(event="home_before_dark", variant="policy", stars=2):
    return {"currency": "Stars", "sourceLabel": "test", "seed": 1, "swapped": False,
            "schedule": "cli", "day": 2, "sourceStateHash": "same-dawn",
            "eventDefinitionId": event, "variant": variant,
            "players": [{"playerId": seat, "policyId": "normal", "restSpace": 10,
                         "isWornOut": False,
                         "night": {"frozenReward": stars, "totalTwigsEarned": 3,
                                   "dreamTwigs": 0, "feathersAwarded": 1, "isMostRested": True}}
                        for seat in ("human", "ai")]}


class EventProbeAnalysisTests(unittest.TestCase):
    def test_contrasts_require_identical_dawn_and_use_named_reference(self):
        base = record()
        event = record("restless_night", stars=1)
        wrong_state = deepcopy(event)
        wrong_state["sourceStateHash"] = "another-dawn"
        result = analysis.analyze([base, event, wrong_state])
        contrast = next(row for row in result["eventContrasts"] if row["policy"] == "all")
        self.assertEqual(2, contrast["playerContrasts"])
        self.assertEqual(-1, contrast["meanDelta"]["stars"])
        self.assertEqual(2, result["sourceStates"])

    def test_duplicate_probe_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            analysis.analyze([record(), record()])

    def test_sunshine_holds_opponent_choice_fixed(self):
        rows = []
        for variant in ("fresh-fresh", "fresh-warm", "warm-fresh", "warm-warm"):
            item = record("glorious_sunshine", variant)
            for player, benefit in zip(item["players"], variant.split("-")):
                player["night"]["frozenReward"] = 4 if benefit == "warm" else 2
            rows.append(item)
        result = analysis.analyze(rows)
        for opponent in ("fresh", "warm"):
            self.assertEqual(2, result["sunshineFreshMinusWarm"][opponent]["playerContrasts"])
            self.assertEqual(-2, result["sunshineFreshMinusWarm"][opponent]["meanDelta"]["stars"])


if __name__ == "__main__":
    unittest.main()
