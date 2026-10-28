from copy import deepcopy
import importlib.util
import random
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("multiplayer_analysis", Path(__file__).resolve().parents[1]
                                             / "GatheringSeason.Evaluation" / "analyze_multiplayer.py")
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)


def matrix(seed=0, extra_style=None):
    assignments = [("all-normal", {"human": "normal", "ai": "normal"})]
    for style in ["movement-heavy", "reeds-heavy"] + ([extra_style] if extra_style else []):
        for seat in ("human", "ai"):
            styles = {"human": "normal", "ai": "normal", seat: style}
            assignments.append((f"focal-{style}-{seat}", styles))
    for movement, reeds in (("human", "ai"), ("ai", "human")):
        assignments.append((f"mixed-m-{movement}-r-{reeds}", {movement: "movement-heavy", reeds: "reeds-heavy"}))
    return [{"sourceLabel": "test", "coreAssemblyVersion": "test", "rulesRevision": 8,
             "seed": seed, "playerCount": 2, "scenario": scenario, "styles": styles,
             "winnerIds": ["human", "ai"],
             "players": [{"playerId": seat, "style": style, "win": True} for seat, style in styles.items()],
             "days": [{"day": day, "eventId": f"event-{day}", "players": [{"playerId": seat} for seat in styles]}
                      for day in range(1, 11)]} for scenario, styles in assignments]


class MultiplayerAnalysisTests(unittest.TestCase):
    def test_missing_or_unsupported_rules_stamps_fail_before_grouping(self):
        for stamp in (None, 7, 9):
            records = matrix()
            for record in records:
                if stamp is None:
                    del record["rulesRevision"]
                else:
                    record["rulesRevision"] = stamp
            with self.subTest(stamp=stamp):
                with self.assertRaisesRegex(ValueError, "compatibility stamp"):
                    analysis.group_by_wish_set(records)
                with self.assertRaisesRegex(ValueError, "compatibility stamp"):
                    analysis.validate(records)

    def test_identical_seed_rotations_remain_separate_for_each_wish_set(self):
        second = [dict(record, wishSetId="set-2", wishSetVersion=1) for record in matrix()]
        groups = analysis.group_by_wish_set(matrix() + second)
        self.assertEqual({("set-1", 1), ("set-2", 1)}, set(groups))
        for cohort in groups.values():
            self.assertEqual(7, len(analysis.validate(cohort)[2][0]))
        second[0]["coreAssemblyVersion"] = "other"
        with self.assertRaisesRegex(ValueError, "Core builds"):
            analysis.group_by_wish_set(matrix() + second)

    def test_collective_ratio_pools_unequal_seed_exposures(self):
        result = analysis.ratio_interval([(1, 1), (0, 9)], random.Random(42), 1000)
        self.assertEqual("0.100 [0.000, 1.000]", result)

    def test_collective_ratio_preserves_zero_exposure_clusters(self):
        self.assertEqual("n/a", analysis.ratio_interval([(0, 0), (0, 0)], random.Random(42), 1000))
        self.assertEqual("0.500 [0.500, 0.500]",
                         analysis.ratio_interval([(0, 0), (1, 2)], random.Random(42), 1000))

    def test_split_ties_conserve_one_win_credit(self):
        match = matrix()[0]
        self.assertEqual(1, sum(analysis.credit(match, player) for player in match["players"]))
        self.assertEqual(0.5, analysis.credit(match, match["players"][0]))

    def test_complete_extended_matrix_is_accepted(self):
        result = analysis.validate(matrix(extra_style="cautious-stop") + matrix(1, "cautious-stop"))
        self.assertEqual([0, 1], sorted(result[2]))

    def test_duplicate_missing_or_wrong_focal_seat_is_rejected(self):
        for records in (matrix() + [matrix()[0]], matrix()[:-1]):
            with self.assertRaisesRegex(ValueError, "rotations"):
                analysis.validate(records)
        records = matrix()
        records[1]["scenario"] = "focal-movement-heavy-ai"
        with self.assertRaises(ValueError):
            analysis.validate(records)

    def test_mixed_builds_incomplete_days_and_repeated_weather_are_rejected(self):
        for mutate in (lambda item: item.update(coreAssemblyVersion="other"),
                       lambda item: item.update(wishSetId="set-2", wishSetVersion=1),
                       lambda item: item["days"].pop(),
                       lambda item: item["days"][1].update(eventId=item["days"][0]["eventId"])):
            records = deepcopy(matrix())
            mutate(records[0])
            with self.assertRaises(ValueError):
                analysis.validate(records)

    def test_policy_sets_must_match_across_seed_clusters(self):
        with self.assertRaises(ValueError):
            analysis.validate(matrix() + matrix(1, "cautious-stop"))


if __name__ == "__main__":
    unittest.main()
