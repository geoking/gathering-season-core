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

## Releases

Stable DLLs are available from [GitHub releases](https://github.com/geoking/gathering-season-core/releases).
A release identifies an exact source revision and its built Core DLL. Stable
releases use Git tags and GitHub releases: `v1` initially, `v1.1` for compatible
incremental releases, and `v2` or later for substantial or incompatible upgrades.
Compatible corrections may use patch versions. Release versioning does not
change rules revisions or the supported save/catalogue contracts by itself.

Publish a new Core version when a distributed application adopts a changed
Core revision. Reuse the existing version when its exact DLL is unchanged.
Published tags and assets remain fixed; a correction receives a new version.

1. Finish the approved changes and merge the reviewed branch. Select the exact
   clean source commit and confirm the release tag is unused.
2. Run the checks in the [verification steps](#verify), including the CLI demo
   used by CI. Build with the Release configuration and
   `-p:ContinuousIntegrationBuild=true`. Core targets .NET Standard 2.1.
3. Verify the DLL assembly version matches the release (`v1` is `1.0.0.0`),
   and record its SHA-256, source commit, SDK and build command. Keep runtime
   version metadata aligned when preparing later releases.
4. Create a draft GitHub release targeting the exact verified commit. Attach
   `GatheringSeason.Core.dll` and `GatheringSeason.Core.dll.sha256`. Release
   notes describe the implemented backend/CLI and supported contracts, with
   the build provenance and relevant validation results.
5. Read back the draft asset and verify its bytes and checksum before
   publication. Publish the approved release, then verify the public tag,
   downloadable DLL and checksum. Do not replace an existing published asset.

The DLL has no game-engine dependency and contains the gameplay backend.
Source archives are provided by GitHub. The repository's MIT licence applies.

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
