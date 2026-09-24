"""Checks for the statistical/accounting boundary, independent of Core rules."""

from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "duck_analysis", Path(__file__).resolve().parents[1] / "analyze-duck-evaluation.py")
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)


def match(seed=1, swapped=False, winner="human"):
    seats = {"human": "policyB" if swapped else "policyA",
             "ai": "policyA" if swapped else "policyB", "swapped": swapped}
    policies = {"policyA": {"id": "normal"}, "policyB": {"id": "baseline"}}
    days = []
    for day in range(1, 11):
        players = []
        for seat in ("human", "ai"):
            players.append({
                "playerId": seat, "policyId": policies[seats[seat]]["id"],
                "start": {"effectiveStart": 0, "dawnFeathersAwarded": 0},
                "adventure": {"restSpace": 4, "drawCount": 3, "biome": "wetlands",
                              "isSafe": True, "isWornOut": False},
                "endOfNightTotalTwigs": day * 2 + (3 if seat == "human" else 0),
                "night": {"day": day, "frozenSleep": 5, "totalTwigsEarned": 2,
                          "feathersAwarded": 1, "reedsTwigs": 0, "eventTwigs": 0,
                          "flowerSleep": 0, "flockSleep": 0},
                "purchases": [], "unspentSleep": 0})
        days.append({"day": day, "players": players})
    return {
        "sourceLabel": "fixture", "seed": seed, "policies": policies,
        "seatAssignment": seats, "schedule": "cli", "days": days,
        "final": {"winnerIds": [winner], "standings": [
            {"playerId": seat, "policyId": policies[seats[seat]]["id"],
             "totalTwigs": 23 if seat == winner else 20} for seat in ("human", "ai")]}}


class AnalysisTests(unittest.TestCase):
    def test_policy_a_wins_follow_assignment_when_seats_are_swapped(self):
        records = [match(swapped=False, winner="human"), match(swapped=True, winner="ai")]
        result = analysis.summarize_matchup(analysis.comparison_key(records[0]), records)
        self.assertEqual({"policyA": 2}, result["outcomes"])
        self.assertEqual(1, result["seeds"])
        self.assertEqual(2, result["policyAScore"]["count"])
        self.assertIsNone(result["policyAScore"]["interval95"])

    def test_seed_bootstrap_does_not_treat_swapped_seats_as_independent(self):
        result = analysis.clustered_rate({1: [0, 1], 2: [0, 1], 3: [0, 1]})
        self.assertEqual(3, result["seeds"])
        self.assertEqual(6, result["count"])
        self.assertEqual([.5, .5], result["interval95"])

    def test_zero_arrivals_does_not_assert_impossibility(self):
        result = analysis.clustered_rate({seed: [0, 0] for seed in range(100)})
        self.assertEqual(0, result["rate"])
        self.assertGreater(result["interval95"][1], 0)
        self.assertAlmostEqual(1 - .05 ** .01, result["interval95"][1])

    def test_comeback_means_the_trailer_won_not_merely_a_reduced_gap(self):
        record = match(winner="ai")
        result = analysis.summarize_matchup(analysis.comparison_key(record), [record])
        day3 = next(c for c in result["comebacks"] if c["day"] == 3)
        self.assertEqual("3-6", day3["gapBand"])
        self.assertEqual("normal", day3["leaderPolicy"])
        self.assertEqual(1, day3["trailerOutrightWin"]["rate"])
        self.assertEqual(0, day3["leaderOutrightWin"]["rate"])

    def test_arrival_rates_are_per_player_match_and_split_safe_from_worn(self):
        record = match()
        player = record["days"][8]["players"][0]
        player["adventure"].update(restSpace=43, isSafe=False, isWornOut=True)
        player = record["days"][9]["players"][0]
        player["adventure"].update(restSpace=43)
        result = analysis.summarize_player([record], "normal")
        self.assertEqual(1, result["safeOasisPlayerMatches"]["count"])
        self.assertEqual(1, result["wornOasisPlayerMatches"]["rate"])
        self.assertEqual(10, result["firstSafeOasisDay"]["mean"])
        self.assertEqual(1, result["byDay"][8]["wornOasis"])

    def test_invalid_or_repeated_night_data_is_rejected(self):
        original = match()
        analysis.validate_record(original)
        bad = deepcopy(original)
        bad["days"][4]["players"][0]["night"]["day"] = 4
        with self.assertRaisesRegex(ValueError, "Previous Night"):
            analysis.validate_record(bad)
        bad = deepcopy(original)
        bad["days"].pop()
        with self.assertRaisesRegex(ValueError, "each Day"):
            analysis.validate_record(bad)

    def test_star_schema_preserves_units_and_counts_free_seeds(self):
        record = analysis.normalize_record(match())
        record.update(schemaVersion=2, rulesVersion=4, currency="Stars")
        record["days"][0]["players"][0]["purchases"] = [
            {"offerDefinitionId": "seeds", "type": "seeds", "price": 0}]
        result = analysis.summarize_matchup(analysis.comparison_key(record), [record])
        self.assertEqual(["Stars"], result["currency"])
        self.assertEqual([4], result["rulesVersions"])
        normal = next(player for player in result["players"] if player["policyId"] == "normal")
        self.assertEqual(1, normal["freeSeedPurchases"])
        self.assertEqual(5, normal["byDay"][0]["reward"]["mean"])

    def test_duplicate_inputs_cannot_inflate_sample_size(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "input.jsonl"
            path.write_text(json.dumps(match()) + "\n")
            with self.assertRaisesRegex(ValueError, "Duplicate match"):
                list(analysis.load_records([path, path]))


if __name__ == "__main__":
    unittest.main()
