#!/usr/bin/env python3
"""Summarise rotated multiplayer JSONL, resampling whole matched-seed clusters."""

import argparse
import collections
import gzip
import json
import random
import statistics
from pathlib import Path


def interval(values, rng, repeats):
    if not values:
        return "n/a"
    mean = statistics.fmean(values)
    draws = sorted(statistics.fmean(rng.choices(values, k=len(values))) for _ in range(repeats))
    low = draws[int(0.025 * (repeats - 1))]
    high = draws[int(0.975 * (repeats - 1))]
    return f"{mean:.3f} [{low:.3f}, {high:.3f}]"


def ratio_interval(count_pairs, rng, repeats):
    """Pool successes/exposure; resample complete seed count pairs together."""
    successes = sum(pair[0] for pair in count_pairs)
    attempts = sum(pair[1] for pair in count_pairs)
    if not attempts:
        return "n/a"
    draws = []
    for _ in range(repeats):
        sample = rng.choices(count_pairs, k=len(count_pairs))
        exposure = sum(pair[1] for pair in sample)
        if exposure:
            draws.append(sum(pair[0] for pair in sample) / exposure)
    if not draws:
        return f"{successes / attempts:.3f} [n/a, n/a]"
    draws.sort()
    low = draws[int(0.025 * (len(draws) - 1))]
    high = draws[int(0.975 * (len(draws) - 1))]
    return f"{successes / attempts:.3f} [{low:.3f}, {high:.3f}]"


def credit(match, player):
    return (1 / len(match["winnerIds"])) if player["win"] else 0


def focal(match, style):
    return next(player for player in match["players"] if player["style"] == style)


def summarize_count(count, by_seed, rng, repeats):
    seeds = sorted(by_seed)
    samples = [by_seed[seed] for seed in seeds]
    matches = [match for group in samples for match in group]
    lines = [f"## {count} players", "",
             f"{len(seeds)} independent seeds, {len(matches)} matches. Brackets are 95% seed-cluster bootstrap intervals.", ""]

    def series(select):
        return [select(group) for group in samples]

    def group(group, prefix):
        return [match for match in group if match["scenario"].startswith(prefix)]

    lines.extend(["| Measure | Estimate [95% interval] |", "|---|---:|"])
    for seat in ["human", "ai", "ai-2", "ai-3"][:count]:
        values = series(lambda matches: credit(
            next(match for match in matches if match["scenario"] == "all-normal"),
            next(player for player in next(match for match in matches if match["scenario"] == "all-normal")["players"]
                 if player["playerId"] == seat)))
        lines.append(f"| All-Normal seat {seat} win credit | {interval(values, rng, repeats)} |")
    styles = sorted({player["style"] for match in matches if match["scenario"].startswith("focal-")
                     for player in match["players"] if player["style"] != "normal"})
    for style in styles:
        prefix = f"focal-{style}-"
        values = series(lambda matches: statistics.fmean(credit(match, focal(match, style)) for match in group(matches, prefix)))
        lines.append(f"| {style} vs Normal win credit | {interval(values, rng, repeats)} |")
        values = series(lambda matches: statistics.fmean(
            focal(match, style)["totalTwigs"]
            - next(player["totalTwigs"] for player in next(base for base in matches if base["scenario"] == "all-normal")["players"]
                   if player["playerId"] == focal(match, style)["playerId"])
            for match in group(matches, prefix)))
        lines.append(f"| {style} vs matched all-Normal seat, Twig delta | {interval(values, rng, repeats)} |")
    mixed_margin = series(lambda matches: statistics.fmean(
        focal(match, "movement-heavy")["totalTwigs"] - focal(match, "reeds-heavy")["totalTwigs"]
        for match in group(matches, "mixed-")))
    mixed_credit = series(lambda matches: statistics.fmean(
        credit(match, focal(match, "movement-heavy")) - credit(match, focal(match, "reeds-heavy"))
        for match in group(matches, "mixed-")))
    lines.append(f"| Mixed Movement minus Reeds Twig margin | {interval(mixed_margin, rng, repeats)} |")
    lines.append(f"| Mixed Movement minus Reeds win credit | {interval(mixed_credit, rng, repeats)} |")

    collective_counts = series(lambda matches: (
        sum(day["collectiveTriggered"] for match in matches for day in match["days"] if day["collectiveAttempt"]),
        sum(day["collectiveAttempt"] for match in matches for day in match["days"])))
    lines.append(f"| Collective event trigger per attempt | {ratio_interval(collective_counts, rng, repeats)} |")
    measures = [
        ("Wear-out days per player-game", lambda matches: statistics.fmean(
            player["wearOutDays"] for match in matches for player in match["players"])),
        ("Safe oasis days per player-game", lambda matches: statistics.fmean(
            player["oasisSafeDays"] for match in matches for player in match["players"])),
        ("Worn oasis days per player-game", lambda matches: statistics.fmean(
            player["oasisWornDays"] for match in matches for player in match["players"])),
        ("Players with any safe oasis reach", lambda matches: statistics.fmean(
            player["oasisSafeDays"] > 0 for match in matches for player in match["players"])),
        ("Winning Twig gap", lambda matches: statistics.fmean(match["winningScoreGap"] for match in matches)),
        ("Winning player's largest prior deficit", lambda matches: statistics.fmean(
            player["maximumLeaderDeficit"] for match in matches for player in match["players"] if player["win"])),
        ("Actions per match", lambda matches: statistics.fmean(match["actionCount"] for match in matches)),
        ("Wall seconds per match", lambda matches: statistics.fmean(match["durationMicroseconds"] / 1e6 for match in matches)),
        ("Policy seconds per player-game", lambda matches: statistics.fmean(
            player["policyMicroseconds"] / 1e6 for match in matches for player in match["players"])),
    ]
    for label, extract in measures:
        lines.append(f"| {label} | {interval([value for value in series(extract) if value is not None], rng, repeats)} |")
    lines.extend(["", "Mean end-of-Day leader deficit by Day (all players and scenarios, clustered by seed):", "",
                  "| Day | Deficit [95% interval] |", "|---:|---:|"])
    for day_number in range(1, 11):
        values = series(lambda matches: statistics.fmean(
            player["endLeaderDeficit"] for match in matches for day in match["days"]
            if day["day"] == day_number for player in day["players"]))
        lines.append(f"| {day_number} | {interval(values, rng, repeats)} |")
    lines.extend(["", "Collective events by card (at most once per match; only ten deck cards are played):", "",
                  "| Event | Triggered / attempts |", "|---|---:|"])
    events = collections.Counter(day["eventId"] for match in matches for day in match["days"] if day["collectiveAttempt"])
    for event_id in sorted(events):
        triggered = sum(day["collectiveTriggered"] for match in matches for day in match["days"] if day["eventId"] == event_id)
        lines.append(f"| {event_id} | {triggered} / {events[event_id]} |")
    lines.append("")
    return lines


def example(label, match):
    players = ", ".join(f"{player['playerId']}={player['style']} {player['totalTwigs']} Twigs/{player['finalStars']} Stars"
                        for player in match["players"])
    day_ten = next(day for day in match["days"] if day["day"] == 10)
    ten = ", ".join(f"{player['playerId']}@{player['restSpace']}"
                    + (" worn" if player["wornOut"] else " safe") for player in day_ten["players"])
    winner = ",".join(match["winnerIds"])
    winner_deficit = max(player["maximumLeaderDeficit"] for player in match["players"] if player["win"])
    winner_track = ", ".join(
        f"{winner_id}:" + "/".join(str(next(player["endLeaderDeficit"] for player in day["players"]
                                          if player["playerId"] == winner_id)) for day in match["days"])
        for winner_id in match["winnerIds"])
    oasis = [(day["day"], player["playerId"], "safe" if player["oasisSafe"] else "worn")
             for day in match["days"] for player in day["players"] if player["oasisReached"]]
    oasis_text = ", ".join(f"D{day} {player} {state}" for day, player, state in oasis) or "none"
    return (f"- {label}: seed {match['seed']}, {match['playerCount']}p, {match['scenario']}; "
            f"{players}; winner {winner}, largest winner deficit {winner_deficit}; "
            f"winner end-of-Day deficits D1–D10 {winner_track}; Day 10 rests {ten}; oasis reaches {oasis_text}.")


def validate(matches):
    if not matches:
        raise ValueError("input contains no matches")
    by_count = collections.defaultdict(lambda: collections.defaultdict(list))
    for match in matches:
        count = match["playerCount"]
        if count not in (2, 3, 4):
            raise ValueError("unsupported player count")
        ids = ["human", "ai", "ai-2", "ai-3"][:count]
        if sorted(player["playerId"] for player in match["players"]) != sorted(ids):
            raise ValueError("invalid player seats")
        if not match["winnerIds"] or len(set(match["winnerIds"])) != len(match["winnerIds"]):
            raise ValueError("invalid winners")
        if set(match["winnerIds"]) != {player["playerId"] for player in match["players"] if player["win"]}:
            raise ValueError("winner flags differ from standings")
        if [day["day"] for day in match["days"]] != list(range(1, 11)):
            raise ValueError("incomplete ten-Day match")
        if len({day["eventId"] for day in match["days"]}) != 10:
            raise ValueError("weather cards repeated")
        if any(sorted(player["playerId"] for player in day["players"]) != sorted(ids) for day in match["days"]):
            raise ValueError("incomplete Day seats")
        if any(match["styles"].get(player["playerId"]) != player["style"] for player in match["players"]):
            raise ValueError("policy assignment differs from player telemetry")
        by_count[count][match["seed"]].append(match)
    if len({(match["sourceLabel"], match["rulesRevision"], match["coreAssemblyVersion"])
            for match in matches}) != 1:
        raise ValueError("input mixes source labels, rules revisions or Core builds")
    if len({tuple(sorted(seeds)) for seeds in by_count.values()}) != 1:
        raise ValueError("player counts must use the same matched seed set")
    matrix = None
    for count, seeds in by_count.items():
        ids = ["human", "ai", "ai-2", "ai-3"][:count]
        expected_scenarios = None
        for seed, group in seeds.items():
            scenarios = {match["scenario"] for match in group}
            if len(scenarios) != len(group):
                raise ValueError(f"duplicate rotations for {count}p seed {seed}")
            baseline = [match for match in group if match["scenario"] == "all-normal"]
            if len(baseline) != 1 or any(player["style"] != "normal" for player in baseline[0]["players"]):
                raise ValueError("invalid all-Normal baseline")
            styles = {player["style"] for match in group if match["scenario"].startswith("focal-")
                      for player in match["players"] if player["style"] != "normal"}
            required = {"all-normal"} | {f"focal-{style}-{seat}" for style in styles for seat in ids}
            neighbors = {f"mixed-m-{ids[m]}-r-{ids[(m + offset) % count]}"
                         for m in range(count) for offset in (1, count - 1)}
            full = {f"mixed-m-{m}-r-{r}" for m in ids for r in ids if m != r}
            if scenarios not in (required | neighbors, required | full):
                raise ValueError(f"incomplete rotations for {count}p seed {seed}")
            for match in group:
                assignment = match["styles"]
                if match["scenario"].startswith("focal-"):
                    nonnormal = [(seat, style) for seat, style in assignment.items() if style != "normal"]
                    if len(nonnormal) != 1 or match["scenario"] != f"focal-{nonnormal[0][1]}-{nonnormal[0][0]}":
                        raise ValueError("invalid focal assignment")
                elif match["scenario"].startswith("mixed-"):
                    movement = [seat for seat, style in assignment.items() if style == "movement-heavy"]
                    reeds = [seat for seat, style in assignment.items() if style == "reeds-heavy"]
                    if len(movement) != 1 or len(reeds) != 1 or match["scenario"] != f"mixed-m-{movement[0]}-r-{reeds[0]}" \
                            or sum(style != "normal" for style in assignment.values()) != 2:
                        raise ValueError("invalid mixed assignment")
            if expected_scenarios is not None and scenarios != expected_scenarios:
                raise ValueError("policy matrix differs between seeds")
            expected_scenarios = scenarios
            if matrix is not None and styles != matrix:
                raise ValueError("focal policies differ between counts or seeds")
            matrix = styles
    return by_count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--bootstrap", type=int, default=1000)
    parser.add_argument("--bootstrap-seed", type=int, default=20260924)
    args = parser.parse_args()
    if args.bootstrap < 100:
        parser.error("--bootstrap must be at least 100")
    opener = gzip.open if args.input.suffix == ".gz" else open
    with opener(args.input, "rt") as source:
        matches = [json.loads(line) for line in source if line.strip()]
    try:
        by_count = validate(matches)
    except ValueError as error:
        parser.error(str(error))
    rng = random.Random(args.bootstrap_seed)
    lines = ["# Multiplayer evaluation", "",
             f"Source label: `{matches[0]['sourceLabel']}`. Rules revision: {matches[0]['rulesRevision']}. "
             f"Bootstrap seed: {args.bootstrap_seed}; {args.bootstrap} resamples.", "",
             "Each seed is one resampling cluster: its baseline and every rotated seat assignment stay together. "
             "Intervals describe variation over sampled seeds; they do not establish an optimal policy. "
             "A collective-event attempt means that card appeared, whether its condition ultimately held or not. "
             "Its trigger rate pools success/attempt counts and resamples those count pairs by whole seed; "
             "bootstrap samples with no exposure have no defined ratio and are omitted.", ""]
    for count in sorted(by_count):
        lines.extend(summarize_count(count, by_count[count], rng, args.bootstrap))
    comeback = max((match for match in matches if any(player["win"] for player in match["players"])),
                   key=lambda match: max(player["maximumLeaderDeficit"] for player in match["players"] if player["win"]))
    mixed = min((match for match in matches if match["scenario"].startswith("mixed-")),
                key=lambda match: abs(focal(match, "movement-heavy")["totalTwigs"]
                                      - focal(match, "reeds-heavy")["totalTwigs"]))
    oasis = next((match for match in matches if any(player["oasisSafeDays"] for player in match["players"])), None)
    lines.extend(["## Real game examples", "", example("Largest winner comeback", comeback),
                  example("Closest direct Movement/Reeds score", mixed)])
    if oasis:
        lines.append(example("Safe oasis reach", oasis))
    lines.append("")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines))


if __name__ == "__main__":
    main()
