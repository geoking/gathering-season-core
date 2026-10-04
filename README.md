# Gathering Season Core

Deterministic C# gameplay rules, a playable CLI, tests and evaluation tooling.
Core targets .NET Standard 2.1 and has no game-engine dependency. This repository
powers the gameplay backend and terminal client; it does not include the
Gathering Season UI game.

## Run

With the .NET 10 SDK installed, run from this repository:

```sh
dotnet run --project src/GatheringSeason.Cli --configuration Release
```

Add `-- --players 3` or `-- --players 4` for two or three CPU opponents.
The default is one opponent. Choose a listed action number. `board`, `wishes`, `event`, `pouch` and `shop`
show information; `q` quits. Completed actions are saved, and the next launch
can Continue. For a reproducible automated game:

```sh
dotnet run --project src/GatheringSeason.Cli --configuration Release -- --seed 42 --demo-game
```

## Documentation

- [CLI guide](docs/cli/README.md): commands, examples, saves and troubleshooting
- [Gameplay rules](docs/rules/README.md): Days, Wishes, Obstacles, rewards and scoring
- [Board and shop](docs/rules/board-and-shop.md), [encounters](docs/rules/encounters.md) and [Weather reports](docs/rules/world-events.md)
- [Core save compatibility](docs/architecture/compatibility.md)

## Verify

```sh
python3 tools/check_public_boundary.py
dotnet build GatheringSeason.sln --configuration Release
dotnet test GatheringSeason.sln --configuration Release --no-build
python3 -m unittest discover -s tools/tests -v
```

## Evaluate multiplayer strategies

Run complete games with Normal, movement-focused and Reeds-focused shoppers
rotated through two to four seats. The default shopping styles share Normal's
Adventure policy. Day 10 choices use the same public beat before commitments
are submitted.

```sh
dotnet run --project tools/GatheringSeason.Evaluation --configuration Release -- \
  --multiplayer --players all --seed-start 0 --seed-count 16 \
  --source-label YOUR_COMMIT_SHA --output /tmp/gathering-study.jsonl
python3 tools/GatheringSeason.Evaluation/analyze_multiplayer.py \
  --input /tmp/gathering-study.jsonl --output /tmp/gathering-study.md
```

JSONL records outcomes, per-Day rewards/purchases, actions and timing. The
summary resamples whole matched-seed groups, not individual rotated seats.
Record the exact source/build used; timing varies between runs. These scripted
shoppers test specific strategies, not optimal or human play balance. The
existing pairwise mode remains available without `--multiplayer`.

Use `--focal-policies ID,ID,...` to select additional diagnostics, each rotated
through every seat against Normal. Supported isolated scripts include
`movement-first-mixed` (Tailwind, then Reeds), `flowers-first`, `seeds-first`,
`cautious-stop`, `adventurous-stop`, `sunshine-fresh` and `sunshine-warm`.
Stopping scripts retain Normal shopping and decisions using authorised Signpost
previews; Sunshine scripts change only that
weather choice. `--all-mixed-seats` expands the direct Movement/Reeds control
to every ordered seat pair. Existing defaults retain their original matrix.
The analyzer accepts plain or gzip JSONL and validates complete seed rotations,
ten-Day outcomes and matching source/build identities. Multiplayer schema 2
also records full Night components, Exhaustion, Dawn Feather progress, Sunshine
choices and placed-chip counts; it does not change rules or the production AI.

## Licence

The source in this repository is available under the [MIT licence](LICENSE).
The licence applies to the material included in this repository.
