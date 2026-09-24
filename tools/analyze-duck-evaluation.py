#!/usr/bin/env python3
"""Summarize actual Core evaluation records without simulating any game rules.

Inputs are the JSONL files emitted by GatheringSeason.Evaluation (plain or gzip).
Comparisons remain separated by source, strategy pair and scheduler. Confidence
intervals resample whole seeds so swapped seats are not independent samples.
"""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import gzip
import json
import math
from pathlib import Path
import random
import statistics


def normalize_record(value):
    """Schema 1 called Night amounts Sleep; schema 2 declares their currency."""
    if isinstance(value, dict):
        return {key.replace("Sleep", "Reward").replace("sleep", "reward"): normalize_record(item)
                for key, item in value.items()}
    if isinstance(value, list):
        return [normalize_record(item) for item in value]
    return value


def percentile(values, fraction):
    ordered = sorted(values)
    if not ordered:
        return None
    index = (len(ordered) - 1) * fraction
    lo, hi = math.floor(index), math.ceil(index)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (index - lo)


def distribution(values):
    values = list(values)
    if not values:
        return {"count": 0}
    return {
        "count": len(values), "mean": statistics.mean(values),
        "min": min(values), "p10": percentile(values, .1),
        "median": percentile(values, .5), "p90": percentile(values, .9),
        "max": max(values),
    }


def clustered_rate(seed_values):
    """Ratio and exploratory 95% interval, preserving all observations per seed."""
    groups = [list(values) for _, values in sorted(seed_values.items()) if values]
    count = sum(len(values) for values in groups)
    if not count:
        return {"count": 0, "seeds": 0, "rate": None, "interval95": None}
    total = sum(map(sum, groups))
    result = {"count": count, "seeds": len(groups), "rate": total / count}
    if len(groups) < 2:
        result["interval95"] = None
        return result
    # A bootstrap with no observed failures/events degenerates to [1,1]/[0,0].
    # Bound the chance of any event in a new independent seed instead.
    if total == 0 or total == count:
        unseen = 1 - .05 ** (1 / len(groups))
        result["interval95"] = [0, unseen] if total == 0 else [1 - unseen, 1]
        result["intervalMethod"] = "one-sided 95% no-event bound over seeds"
        return result
    rng = random.Random(4515)
    sums = [sum(values) for values in groups]
    counts = [len(values) for values in groups]
    samples = []
    for _ in range(1200):
        picked = rng.choices(range(len(groups)), k=len(groups))
        samples.append(sum(sums[i] for i in picked) / sum(counts[i] for i in picked))
    result["interval95"] = [percentile(samples, .025), percentile(samples, .975)]
    result["intervalMethod"] = "paired-seed percentile bootstrap, 1200 resamples"
    return result


def gap_band(gap):
    if gap == 0:
        return "tied"
    if gap <= 2:
        return "1-2"
    if gap <= 6:
        return "3-6"
    if gap <= 10:
        return "7-10"
    return "11+"


def load_records(paths):
    seen = set()
    for path in paths:
        opener = gzip.open if str(path).endswith(".gz") else open
        with opener(path, "rt", encoding="utf-8") as stream:
            for line_number, line in enumerate(stream, 1):
                if not line.strip():
                    continue
                record = normalize_record(json.loads(line))
                key = (*comparison_key(record), record["seed"], record["seatAssignment"]["swapped"])
                if key in seen:
                    raise ValueError(f"Duplicate match at {path}:{line_number}: {key}")
                seen.add(key)
                validate_record(record)
                yield record


def comparison_key(record):
    return (
        record["sourceLabel"], record["policies"]["policyA"]["id"],
        record["policies"]["policyB"]["id"], record["schedule"],
        record.get("rulesVersion", 1), record.get("currency", "Sleep"),
    )


def validate_record(record):
    days = record["days"]
    if [day["day"] for day in days] != list(range(1, 11)):
        raise ValueError("A complete match must contain each Day 1-10 exactly once")
    for day in days:
        players = day["players"]
        if {player["playerId"] for player in players} != {"human", "ai"}:
            raise ValueError("Each Day must include both player seats once")
        if len(players) != 2:
            raise ValueError("Duplicate player telemetry")
        for player in players:
            adventure, night = player["adventure"], player["night"]
            if not 1 <= adventure["restSpace"] <= 43 or adventure["drawCount"] < 1:
                raise ValueError("Invalid occupied rest or missing mandatory draw")
            if night["day"] != day["day"]:
                raise ValueError("Previous Night outcome counted again")
            if bool(adventure["isSafe"]) == bool(adventure["isWornOut"]):
                raise ValueError("Safe/worn telemetry disagrees")
            if day["day"] == 10 and player["purchases"]:
                raise ValueError("Final Night cannot include purchases")


def summarize_player(records, policy_id):
    records = [normalize_record(record) for record in records]
    rows, final_scores, policy_games = [], [], []
    type_purchases, offers, bundles = Counter(), Counter(), Counter()
    timing_rows = []
    for record in records:
        timing_rows.extend(t for t in record.get("timings", []) if t["policyId"] == policy_id)
        for standing in record["final"]["standings"]:
            if standing["policyId"] != policy_id:
                continue
            final_scores.append(standing["totalTwigs"])
            seat = standing["playerId"]
            daily = [next(p for p in day["players"] if p["playerId"] == seat)
                     for day in record["days"]]
            policy_games.append((record["seed"], daily))
            for day, player in zip(record["days"], daily):
                rows.append((record["seed"], day, player))
                if day["day"] < 10:
                    bundles[" + ".join(sorted(p["type"] for p in player["purchases"])) or "none"] += 1
                for purchase in player["purchases"]:
                    type_purchases[purchase["type"]] += 1
                    offers[purchase["offerDefinitionId"]] += 1
    safe_oasis, worn_oasis, any_oasis = defaultdict(list), defaultdict(list), defaultdict(list)
    first_arrivals = []
    for seed, daily in policy_games:
        safe = [i + 1 for i, p in enumerate(daily)
                if p["adventure"]["restSpace"] == 43 and p["adventure"]["isSafe"]]
        worn = [i + 1 for i, p in enumerate(daily)
                if p["adventure"]["restSpace"] == 43 and p["adventure"]["isWornOut"]]
        safe_oasis[seed].append(int(bool(safe)))
        worn_oasis[seed].append(int(bool(worn)))
        any_oasis[seed].append(int(bool(safe or worn)))
        if safe:
            first_arrivals.append(safe[0])
    by_day = []
    for day_number in range(1, 11):
        players = [p for _, day, p in rows if day["day"] == day_number]
        by_day.append({
            "day": day_number, "playerDays": len(players),
            "rest": distribution(p["adventure"]["restSpace"] for p in players),
            "draws": distribution(p["adventure"]["drawCount"] for p in players),
            "start": distribution(p["start"]["effectiveStart"] for p in players),
            "permanentTrail": distribution(p["start"].get("permanentTrail", 0) for p in players),
            "dawnFeathers": distribution(p["start"]["dawnFeathersAwarded"] for p in players),
            "reward": distribution(p["night"]["frozenReward"] for p in players),
            "twigsEarned": distribution(p["night"]["totalTwigsEarned"] for p in players),
            "wornOut": sum(p["adventure"]["isWornOut"] for p in players),
            "oneDraw": sum(p["adventure"]["drawCount"] == 1 for p in players),
            "safeOasis": sum(p["adventure"]["restSpace"] == 43 and p["adventure"]["isSafe"] for p in players),
            "wornOasis": sum(p["adventure"]["restSpace"] == 43 and p["adventure"]["isWornOut"] for p in players),
        })
    by_event = []
    for event in sorted({day.get("eventDefinitionId", "unspecified") for _, day, _ in rows}):
        players = [p for _, day, p in rows if day.get("eventDefinitionId", "unspecified") == event]
        by_event.append({"event": event, "playerDays": len(players),
            "meanDraws": statistics.mean(p["adventure"]["drawCount"] for p in players),
            "meanRest": statistics.mean(p["adventure"]["restSpace"] for p in players),
            "meanReward": statistics.mean(p["night"]["frozenReward"] for p in players),
            "meanTwigsEarned": statistics.mean(p["night"]["totalTwigsEarned"] for p in players),
            "wornOut": sum(p["adventure"]["isWornOut"] for p in players),
            "collectiveBonusReward": sum(p["night"].get("collectiveEventReward", 0) for p in players),
            "sunshineChoices": dict(Counter(p.get("sunshineChoice") for p in players if p.get("sunshineChoice"))),
            "sunshineReward": sum(p["night"].get("gloriousSunshineReward", 0) for p in players),
            "directEventTwigs": sum(p["night"]["eventTwigs"] for p in players)})
    starts_after_wear = [p["nextDayStart"]["effectiveStart"] for _, _, p in rows
                        if p["adventure"]["isWornOut"] and p.get("nextDayStart")]
    shopping = [p for _, day, p in rows if day["day"] < 10]
    decision_count = sum(t["decisionCount"] for t in timing_rows)
    return {
        "policyId": policy_id, "playerMatches": len(policy_games), "playerDays": len(rows),
        "finalTwigs": distribution(final_scores),
        "draws": distribution(p["adventure"]["drawCount"] for _, _, p in rows),
        "rest": distribution(p["adventure"]["restSpace"] for _, _, p in rows),
        "biomes": dict(Counter(p["adventure"]["biome"] for _, _, p in rows)),
        "wornOut": sum(p["adventure"]["isWornOut"] for _, _, p in rows),
        "oneDraw": sum(p["adventure"]["drawCount"] == 1 for _, _, p in rows),
        "atShelter4Or10": sum(p["adventure"]["restSpace"] in (4, 10) for _, _, p in rows),
        "safeOasisPlayerMatches": clustered_rate(safe_oasis),
        "wornOasisPlayerMatches": clustered_rate(worn_oasis),
        "anyOasisPlayerMatches": clustered_rate(any_oasis),
        "firstSafeOasisDay": distribution(first_arrivals),
        "purchasesByType": dict(type_purchases), "purchasesByOffer": dict(offers),
        "nightBundlesByType": dict(bundles.most_common()),
        "purchasesPerShoppingNight": distribution(len(p["purchases"]) for p in shopping),
        "rewardSpentPerShoppingNight": distribution(sum(b["price"] for b in p["purchases"]) for p in shopping),
        "zeroBudgetNights": sum(p["night"]["frozenReward"] == 0 for p in shopping),
        "freeSeedPurchases": sum(b["offerDefinitionId"] == "seeds" and b["price"] == 0 for p in shopping for b in p["purchases"]),
        "unspentRewardPerShoppingNight": distribution(p["unspentReward"] for p in shopping),
        "shelterFeathers": sum(p["night"]["feathersAwarded"] for _, _, p in rows),
        "dawnFeathers": sum(p["start"]["dawnFeathersAwarded"] for _, _, p in rows),
        "reedsTwigs": sum(p["night"]["reedsTwigs"] for _, _, p in rows),
        "eventTwigs": sum(p["night"]["eventTwigs"] for _, _, p in rows),
        "flowerReward": sum(p["night"]["flowerReward"] for _, _, p in rows),
        "flockReward": sum(p["night"]["flockReward"] for _, _, p in rows),
        "unspentReward": sum(p["unspentReward"] for _, _, p in rows),
        "mostRestedDays": sum(p["night"].get("isMostRested", False) for _, _, p in rows),
        "nextStartAfterWear": distribution(starts_after_wear),
        "timing": {"decisions": decision_count,
            "meanPolicyMicroseconds": sum(t["totalPolicyMicroseconds"] for t in timing_rows) / decision_count if decision_count else None,
            "maxPolicyMicroseconds": max((t["maxPolicyMicroseconds"] for t in timing_rows), default=None)},
        "byDay": by_day, "byEvent": by_event,
    }


def summarize_matchup(key, records):
    records = [normalize_record(record) for record in records]
    wins, seed_scores = Counter(), defaultdict(list)
    by_seat, lead_groups = defaultdict(lambda: defaultdict(list)), defaultdict(list)
    lead_changes, final_margins = [], []
    for record in records:
        a_seat = next(seat for seat in ("human", "ai") if record["seatAssignment"][seat] == "policyA")
        winners = record["final"]["winnerIds"]
        totals = [p["totalTwigs"] for p in record["final"]["standings"]]
        final_margins.append(abs(totals[0] - totals[1]))
        score = .5 if len(winners) == 2 else float(a_seat in winners)
        wins["draw" if len(winners) == 2 else "policyA" if score == 1 else "policyB"] += 1
        seed_scores[record["seed"]].append(score)
        by_seat[a_seat][record["seed"]].append(score)
        previous_leader, changes = None, 0
        for day in record["days"]:
            players = day["players"]
            difference = players[0]["endOfNightTotalTwigs"] - players[1]["endOfNightTotalTwigs"]
            leader = None if difference == 0 else players[0 if difference > 0 else 1]
            if leader is not None:
                if previous_leader is not None and previous_leader != leader["playerId"]:
                    changes += 1
                previous_leader = leader["playerId"]
            if day["day"] in (3, 5, 7, 9):
                group = (day["day"], gap_band(abs(difference)), leader["policyId"] if leader else "tied")
                lead_groups[group].append((record["seed"],
                    None if leader is None else int(len(winners) == 1 and leader["playerId"] in winners),
                    int(len(winners) == 2),
                    None if leader is None else int(len(winners) == 1 and leader["playerId"] not in winners)))
        lead_changes.append(changes)
    comebacks = []
    for group, outcomes in sorted(lead_groups.items()):
        leader_wins, recoveries = defaultdict(list), defaultdict(list)
        for seed, leader_win, draw, recovery in outcomes:
            if leader_win is not None:
                leader_wins[seed].append(leader_win)
                recoveries[seed].append(recovery)
        comebacks.append({"day": group[0], "gapBand": group[1], "leaderPolicy": group[2],
            "matches": len(outcomes), "finalDraws": sum(row[2] for row in outcomes),
            "leaderOutrightWin": clustered_rate(leader_wins), "trailerOutrightWin": clustered_rate(recoveries)})
    return {"sourceLabel": key[0], "policyA": key[1], "policyB": key[2], "schedule": key[3],
        "currency": sorted({r.get("currency", "Sleep") for r in records}),
        "rulesVersions": sorted({r.get("rulesVersion", 1) for r in records}),
        "sharedMostRestedNights": sum(sum(p["night"].get("isMostRested", False) for p in d["players"]) > 1 for r in records for d in r["days"]),
        "matches": len(records), "seeds": len(seed_scores), "outcomes": dict(wins),
        "policyAScore": clustered_rate(seed_scores),
        "policyAScoreBySeat": {seat: clustered_rate(scores) for seat, scores in by_seat.items()},
        "leadChanges": distribution(lead_changes),
        "finalTwigMargin": distribution(final_margins),
        "players": [summarize_player(records, p) for p in dict.fromkeys(key[1:3])],
        "comebacks": comebacks}


def render_markdown(report):
    lines = ["# Gathering Season evaluation summary", "",
        "Generated from complete Core matches. A match contains two player-matches and twenty player-Days.",
        "Policy score counts a draw as half; intervals resample whole seeds with paired seats kept together.",
        "Equal-policy win scores do not establish balance. Sparse cohorts remain inconclusive.", ""]
    for match in report["matchups"]:
        lines += [f"## {match['policyA']} vs {match['policyB']} — {match['schedule']}", "",
            f"Source `{match['sourceLabel']}`; {match['matches']} matches / {match['seeds']} seeds.", "",
            f"Outcomes: {match['outcomes']}. Policy A score: {match['policyAScore']['rate']:.1%}.", "",
            "| Policy | Mean final Twigs | Draws / Day | One-draw Days | Worn-out Days | Safe oasis / player-match |",
            "| --- | ---: | ---: | ---: | ---: | ---: |"]
        for p in match["players"]:
            lines.append(f"| {p['policyId']} | {p['finalTwigs']['mean']:.1f} | {p['draws']['mean']:.1f} | "
                         f"{p['oneDraw']/p['playerDays']:.1%} | {p['wornOut']/p['playerDays']:.1%} | "
                         f"{p['safeOasisPlayerMatches']['rate']:.1%} |")
        lines += ["", "| Checkpoint | Gap | Leading policy | Matches | Leader wins | Trailer wins |",
                  "| --- | --- | --- | ---: | ---: | ---: |"]
        for c in match["comebacks"]:
            if c["gapBand"] == "tied":
                continue
            lines.append(f"| Day {c['day']} | {c['gapBand']} | {c['leaderPolicy']} | {c['matches']} | "
                         f"{c['leaderOutrightWin']['rate']:.1%} | {c['trailerOutrightWin']['rate']:.1%} |")
        lines.append("")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", nargs="+", type=Path)
    parser.add_argument("--json", required=True, type=Path, dest="json_path")
    parser.add_argument("--markdown", type=Path)
    args = parser.parse_args()
    grouped = defaultdict(list)
    for record in load_records(args.inputs):
        grouped[comparison_key(record)].append(record)
    report = {"schemaVersion": 1, "inputs": [str(p) for p in args.inputs],
              "matchups": [summarize_matchup(key, records) for key, records in sorted(grouped.items())]}
    args.json_path.parent.mkdir(parents=True, exist_ok=True)
    args.json_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n")
    if args.markdown:
        args.markdown.parent.mkdir(parents=True, exist_ok=True)
        args.markdown.write_text(render_markdown(report))
    print(f"Summarized {sum(m['matches'] for m in report['matchups'])} matches in {len(grouped)} comparisons")


if __name__ == "__main__":
    main()
