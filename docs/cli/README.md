# Playing Gathering Season in the terminal

The CLI plays the current ten-Day duck game against Normal AI using the Core
rules. It includes all 43 spaces, encounter powers, ten World Events, shopping,
Dawn Delivery, final scoring and local Continue.

## Start a game

You need the **.NET 10 SDK**, rather than only the runtime. From the repository
root:

```sh
dotnet run --project src/GatheringSeason.Cli --configuration Release
```

The duck game is the default. If a save exists, choose `c` to continue, `n` to
start over or `q` to leave it alone. Otherwise a new game starts. The opening
text shows the seed and autosave path.

To see launch options without starting or saving a match:

```sh
dotnet run --project src/GatheringSeason.Cli --configuration Release -- --help
```

Everything after `--` goes to Gathering Season, rather than `dotnet run`. Commands in
this guide assume the repository root is your current directory.

## Your first Day

1. Read the World Event. It affects both ducks for this Day. Type `event` to
   review its effect, and `tokens` or `wishes` if you do not recognize a chip. On Glorious
   Sunshine choose Fresh Air (+2 safe Exhaustion today) or Warm Dreams (+2 Stars
   tonight); everyone must choose before exploring.
2. Enter the number beside **Explore**. Core draws a chip, moves your duck and
   resolves its power. You must draw at least once each Day.
3. Review your position, Exhaustion and the AI's public progress. Use `board`
   to compare rewards and shelters; `pouch` shows your remaining chip counts.
4. Choose **Explore** again or **Settle down** using the current action number.
   You rest on the space you occupy. Its Twig numeral is the total collected
   along the route, added to Nest Twigs once at Night; do not sum the passed numerals.
5. When both ducks finish, review the Night result. Spend Stars on affordable Wishes using
   the numbered actions, then choose **Finish Dream**. Once both ducks are
   finished, choose the action to start the next Day.

Use the numbers printed at each prompt: available actions and their numbers
change with the game state. You can quit with `q` at any prompt and Continue
later. Informational commands and invalid input do not draw chips, advance the
AI or change the saved game.

Days 1–9 expose completed actions so you can react to the opponent. The CLI
paces Normal around successful gameplay actions and lets it finish when you
have settled. On Day 10 only, both active ducks commit privately before each
beat is revealed. The AI's hidden commitment and private Signpost preview are
not shown to you.

## Commands while playing

| Input | What you see or do |
| --- | --- |
| An action number, such as `1` | Execute that currently listed action |
| `help` | Remind yourself of available commands |
| `status` | Review both ducks' public state and your own information |
| `board` | All 43 spaces, printed rewards and named shelters |
| `board 4` | Inspect a particular space |
| `pouch` | Your remaining composition and owned inventory counts |
| `tokens` or `wishes` | Wish powers and Obstacle nuisances |
| `shop` | All offers at this match's prices, spending allowance and current availability |
| `event` | The active World Event and its effect |
| `night` | The last resolved Night's rewards and deductions |
| `history` | Public actions and results so far |
| `view:human` | Review your own view; retained for existing CLI users |
| `q` or `quit` | Quit; previously completed actions remain saved |
| `r` or `restart` | Start over and replace the active save |

The pouch display lists quantities, not the shuffled draw order. Signpost alone
can reveal your actual next chip; weather may alter its preview. Board rewards
are **printed values**, not a prediction of your final payout: chip effects,
World Events, shelter bonuses and wear-out are resolved by Core at Night.

## What the scores mean

**Twigs win the game.** They accumulate across Days. Stars are that Night's shopping budget, and any unspent Stars expire when the next Day begins.
Your Night summary separates frozen earned Stars from the amount still
available to spend. Shopping does not change who won Most Rested. Helpful chips are Wishes;
ordinary white hazards are Obstacles, while chip and token still name physical pieces.

In revision 7, Stars are that Night's currency. Normally five Exhaustion is safe; drawing a sixth obstacle wears you out.
Fresh Air adds two to the safe limit for its Day. From Day 5 each pouch gains one Grumpy Goose. An unprotected Goose lowers the
safe maximum to four for that Day. Worn-out ducks keep earned Twigs and receive
half their total Stars, rounded down, and cannot win
Most Rested. Splash blocks the next obstacle's nuisance, not its Exhaustion.

Current revision 7 shelters are at **4, 10, 17, 21, 25, 32, 36 and 43**. Finishing there grants
Feathers, which permanently move your later starts forward one step each.
Dawn Delivery grants catch-up Feathers from the gap to the Twig leader:
0 for a 0–2 gap, 1 for 3–6, 2 for 7–10 and 3 for 11 or more.
Feathers are never spent. A shelter with one or more Wildflowers gives +1 Star total; positive flock leaders also gain +1 Star. Exhausted ducks keep these bonuses and shelter Feathers; their complete Star total is then halved. Most Rested is a separate temporary +1 start next Day. Pebbles and shelter Restless Night each deduct 1 Star, to a minimum of zero. Home Before Dark keeps its special condition that no duck is exhausted.

At Night, buy at most one chip of each helpful type: two Tailwind variants
still count as the same type. Nest capacity allows one purchase on Nights 1–3,
two on Nights 4–6 and three on Nights 7–9. The shop has unlimited stock.
Reeds quantities mean Twig yield, not movement; all Reeds move one space.
New matches use Star prices: Seeds **0**, Tailwinds **1/2/3**, Signpost **2**, Splash **1**, Reeds **2/3/4**, Companion **2** and Wildflowers **1**. Free Seeds still use a purchase slot and the one-per-type limit.

Night 10 has no shop. A shelter adds one extra Star before any exhaustion halving; each retained Star becomes one Dream Twig. The safe Most
Rested duck, including tied winners, also earns one Dream Twig. Total Twigs
wins; tied Twigs use final frozen Stars, then a draw.

The [full rules recap](../rules/README.md),
[encounter timing](../rules/encounters.md) and
[World Events](../rules/world-events.md) explain the edge cases.
The CLI's legal-action list remains authoritative for what you can do now.

## Saving, Continue and restart

Interactive games autosave after each completed action, including an unrevealed
Day 10 commitment or pending World Event choice. The default file is `GatheringSeason/ducks-save.json` beneath the
platform's local application-data directory; **the CLI prints the exact path**.
Quitting with `q` does not start a new game. To open a save from this baseline stored
elsewhere, provide its explicit `--save` path; automatic discovery is limited
to the current default directory. Pre-baseline game profiles are unsupported; start a new game.

Continue directly:

```sh
dotnet run --project src/GatheringSeason.Cli --configuration Release -- --continue
```

Use a separate named save, for example:

```sh
dotnet run --project src/GatheringSeason.Cli --configuration Release -- --save "$HOME/GatheringSeason-Saves/my-game.json"
dotnet run --project src/GatheringSeason.Cli --configuration Release -- --save "$HOME/GatheringSeason-Saves/my-game.json" --continue
```

Start afresh with `--new-game`, or choose `r` during play. When saving is
enabled, these replace the selected active save; `--no-save` leaves saved files
untouched. By default a fresh game uses a new seed; pass `--seed 42`
if you want the same setup on each new game or restart. Identical results also
require identical actions. Continue restores its saved random state instead of
reshuffling; the launch seed matters only if you later restart. Continue pauses
at a saved human decision; it only progresses the AI first when the human must
wait for the opponent to resolve a pending choice or finish.

A `.bak` file retains the previous saved action. If the primary save cannot be
read but the backup can, Continue explicitly reports that it recovered the
previous action. If neither is valid, it reports an error instead of silently
starting over. A write failure stops play to preserve the preceding save.

Revisions 1–6 keep their recorded rules. New games use revision 7 with Stars
and Wishes and the current meadow catalogue. Existing paid Sleep or Stars and
random order are preserved. Always use the in-game `shop` display for the
running match.
Save files contain private host state, including future pouch order. Do not read
them while trying to play without foreknowledge.

For a temporary game that does not touch the default save:

```sh
dotnet run --project src/GatheringSeason.Cli --configuration Release -- --no-save
```

## Reproducible demonstrations and developer controls

| Option | Purpose |
| --- | --- |
| `--seed 42` | Use a fixed seed for new games and restarts |
| `--inspect` | Print the current catalogue and exit |
| `--demo-day` | Script Day 1, Night 1 purchases and the transition to Day 2 |
| `--demo-game` | Let Normal control both ducks for a complete game |
| `--save path` | Select the save; also enables saving a demo |
| `--no-save` | Disable interactive saves |
| `--continue` | Restore the selected save without the startup chooser |
| `--new-game` | Replace the selected save with a fresh match |
| `--two-player` | Developer mode: manually control both ducks |
| `--profile ducks` | Explicit alias for the Gathering Season game |
| `--profile classic` | Rejected; the retired profile is no longer shipped |

For example, watch a complete game without changing your interactive save:

```sh
dotnet run --project src/GatheringSeason.Cli --configuration Release -- --seed 42 --demo-game
```

Demo modes do not save unless you supply `--save`, or Continue a saved game.
`--demo-day` requires Day 1. Choose only one of `--inspect`, `--demo-day` and
`--demo-game`; `--continue` and `--new-game` are mutually exclusive. `--no-save`
cannot accompany `--save` or `--continue`.

Developer mode accepts `human:1` and `ai:1`, and `view:human` / `view:ai` switch
the observing duck. It exposes that selected duck's private information, so it
is a testing tool, not a concealed-information local multiplayer interface.
Normal human-versus-AI mode does not allow the AI's private view.

The CLI exposes only Gathering Season rules and options. The `ducks` profile
alias remains for existing scripts; `classic` is reported as unsupported.

## If something goes wrong

- If `dotnet` is missing or no compatible SDK is found, install the .NET 10 SDK
  and reopen your terminal. `dotnet --list-sdks` shows installed SDKs.
- Run from the repository root so the project path resolves.
- Use `help` while playing or `--help` at launch. Enter an action number from the
  current list, rather than reusing an old number after the phase changes.
- For save errors, read the printed path and error. Use a different `--save`
  location or `--no-save` for a separate test; preserve the original save if you
  want to investigate it.
- To report a gameplay issue, record the seed, Day, World Event, recent actions
  and what you expected. A copy of the save helps reproduce the exact state.

For retained save and API names, see [compatibility](../architecture/compatibility.md).
