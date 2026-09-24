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
- [Board and shop](docs/rules/board-and-shop.md), [encounters](docs/rules/encounters.md) and [World Events](docs/rules/world-events.md)
- [Core save compatibility](docs/architecture/compatibility.md)

## Verify

```sh
python3 tools/check_public_boundary.py
dotnet build GatheringSeason.sln --configuration Release
dotnet test GatheringSeason.sln --configuration Release --no-build
python3 -m unittest discover -s tools/tests -v
```

## Licence

The source in this repository is available under the [MIT licence](LICENSE).
The licence applies to the material included in this repository.
