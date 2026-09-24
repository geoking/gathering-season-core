#!/usr/bin/env python3
"""Describe matched-Dawn event probes; never reconstruct or simulate rules.

Each event comparison uses the same pre-Dawn state and remaining-card eligibility.
Home Before Dark is a named reference, NOT a neutral/no-event control. Probe rows
are correlated and do not count as independent full games. Sunshine contrasts
hold the other duck's choice fixed. Results are exploratory, not optimal-play claims.
"""
import argparse
from collections import defaultdict
import gzip
import json
from pathlib import Path
import statistics


def measures(player):
    night = player["night"]
    return {
        "stars": night["frozenReward"],
        "twigsIncludingDream": night["totalTwigsEarned"] + night["dreamTwigs"],
        "restSpace": player["restSpace"],
        "feathers": night["feathersAwarded"],
        "wornOut": int(player["isWornOut"]),
        "mostRested": int(night["isMostRested"]),
    }


def differences(player, reference):
    a, b = measures(player), measures(reference)
    return {key: a[key] - b[key] for key in a}


def summarize(rows):
    return {"playerContrasts": len(rows),
            "meanDelta": {key: statistics.mean(row[key] for row in rows) for key in rows[0]}} if rows else {}


def analyze(records):
    states = defaultdict(dict)
    for record in records:
        if record["currency"] != "Stars":
            raise ValueError("This probe report requires Stars records")
        key = (record["sourceLabel"], record["seed"], record["swapped"], record["schedule"], record["day"], record["sourceStateHash"])
        branch = (record["eventDefinitionId"], record["variant"])
        if branch in states[key]:
            raise ValueError("Duplicate branch would inflate probe evidence")
        states[key][branch] = record
    contrasts, sunshine, sunshine_day, sunshine_policy = (defaultdict(list) for _ in range(4))
    for state, branches in states.items():
        reference = branches.get(("home_before_dark", "policy"))
        if reference:
            for (event, variant), record in branches.items():
                if event == "home_before_dark" or variant != "policy":
                    continue
                for player in record["players"]:
                    other = next(p for p in reference["players"] if p["playerId"] == player["playerId"])
                    row = differences(player, other)
                    contrasts[(event, "all")].append(row)
                    contrasts[(event, player["policyId"])].append(row)
        # Fresh minus Warm, with the opponent held at the named choice.
        for seat_index, seat in enumerate(("human", "ai")):
            for opponent in ("fresh", "warm"):
                variants = (["fresh-" + opponent, "warm-" + opponent] if seat_index == 0
                            else [opponent + "-fresh", opponent + "-warm"])
                pair = [branches.get(("glorious_sunshine", variant)) for variant in variants]
                if not all(pair):
                    continue
                players = [next(p for p in record["players"] if p["playerId"] == seat) for record in pair]
                delta = differences(*players)
                sunshine[opponent].append(delta)
                sunshine_day[state[4]].append(delta)
                sunshine_policy[players[0]["policyId"]].append(delta)
    return {
        "sourceStates": len(states), "branches": sum(len(value) for value in states.values()),
        "seeds": len({key[1] for key in states}),
        "eventReference": "Home Before Dark, matched pre-Dawn state; not a neutral control",
        "eventContrasts": [{"event": event, "policy": policy, **summarize(rows)}
                           for (event, policy), rows in sorted(contrasts.items())],
        "sunshineFreshMinusWarm": {key: summarize(rows) for key, rows in sorted(sunshine.items())},
        "sunshineByDay": {key: summarize(rows) for key, rows in sorted(sunshine_day.items())},
        "sunshineByPolicy": {key: summarize(rows) for key, rows in sorted(sunshine_policy.items())},
        "limitations": "Correlated one-Day branches; unseen cards only on Days 2–10, no future shopping or long-term effects, policy-dependent results, not independent full games or proof of balance."
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path)
    parser.add_argument("--json", required=True, type=Path)
    args = parser.parse_args()
    opener = gzip.open if str(args.input).endswith(".gz") else open
    with opener(args.input, "rt") as stream:
        result = analyze(json.loads(line) for line in stream if line.strip())
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(result, indent=2) + "\n")
    print(f"Summarized {result['branches']} branches from {result['sourceStates']} Dawn states / {result['seeds']} seeds")


if __name__ == "__main__":
    main()
