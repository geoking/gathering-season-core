# Compatibility and retained technical names

The product is **Gathering Season**. Solution and .NET project paths use
`GatheringSeason.*`; current player-facing language uses **shelter**.

C# namespaces and assembly names use `GatheringSeason.*`. The public Core DLL
is `GatheringSeason.Core.dll`; tests use `GatheringSeason.Core.Tests`.
Save profile IDs, format version and existing enum numeric values remain stable.
New enum values append instead of changing serialized meanings.
`GameActionKind` retains Explore=11, Settle=12, BuyEncounter=13, FinishDream=14,
NextDay=15 and ChooseEventBenefit=16. Legacy JSON adapters retain required
wire fields without changing runtime terminology.

| Rules revision | Economy | Shared deck |
| --- | --- | --- |
| 1 | Original Sleep prices: Tailwind 5/10/15, Reeds 6/11/16 | Original deck with Clear Sunlight |
| 2 | Sleep prices: Tailwind 4/8/12, Reeds 8/14/20 | Original deck with Clear Sunlight |
| 3 | Same Sleep prices as revision 2 | Glorious Sunshine replaces Clear Sunlight |
| 4 | Stars and Wishes: Seeds 0; Tailwinds 1/2/3; Reeds 2/3/4; older safe-only reward gates | Glorious Sunshine deck |
| 5 | Same Stars, prices and board; exhausted ducks keep Flock, shelter bonuses and Feathers before Star halving | Same ten weather reports; Evening Chill/Thundery Skies include exhausted shelter occupants |
| 6 | Same Star prices; meadow shelters are 16/22/25 and space 28 has five Twigs | Same ten weather reports; Fresh Air is +2 safe Exhaustion |
| 7 | Same Star prices; meadow shelters are 17/21/25, with current 15–25 reward values; Log waits for the next Wish | Same ten weather reports; Fresh Air is +2 safe Exhaustion |
| 8 (new games) | Same economy and board as revision 7; Log is consumed by the immediately next drawn chip, slowing only a Wish | Same ten weather reports; protection suppresses only the newly drawn nuisance |

Continue restores the recorded catalogue. It never retroactively changes
purchases, paid Sleep/Stars, weather order or already-earned rewards. Clear Sunlight’s
two-token preview survives only for old matches. New games have ten weather reports,
not eleven. Exact pending weather choices and final-Day commitments are saved.

Format-1 saves retain their established `Sleep` wire names. `RulesVersion`
distinguishes whether those values mean legacy Sleep or revision 4–8 Stars. The
public API remains neutral for current clients: board `Reward`/`Stars` retains
legacy `Sleep`; shop `Price`/`StarsPrice` retains `SleepPrice`; player
`FrozenReward`/`FrozenStars` retain `FrozenSleep`, and
`RemainingReward`/`RemainingStars` retain `RemainingSleep`.
Reward outcomes expose `PrintedReward`, `FlowerReward`, `FinalShelterReward`,
`CollectiveEventReward`, `GloriousSunshineReward`, `FlockReward`,
`RewardBeforeWear`, and `FrozenReward`; their retained fields are respectively
`PrintedSleep`, `FlowerSleep`, `FinalShelterSleep`, `CollectiveEventSleep`,
`GloriousSunshineSleep`, `FlockSleep`, `SleepBeforeWear`, and `FrozenSleep`.
Final standings similarly expose `FrozenNightTenReward` beside
`FrozenNightTenSleep`. `DuckMatchView` carries
`RulesRevision`, immutable `Rules`, and immutable `Economy`; clients should use
those observations instead of inferring units from a saved field name.

Default CLI saves use the platform application-data `GatheringSeason` directory.
Use `--save /path/to/existing.json --continue` to open an existing save outside
that directory. The application does not search unrelated historical folders.
No source save is deleted by loading it. The `ducks` profile alias remains valid.

The non-generic `MatchSession` provides the `CreateDuck` factory; clients use
`MatchSession<DuckMatchView>`. The baseline uses profile ID `gathering-season.duck.v1`. Pre-baseline game
profiles are intentionally unsupported; start a new game. Retained rules
revision catalogues support internal regression/evaluation and do not imply
support for a previous product profile.

## Weather reports and retained event identities

Weather naming is a display change within revision 8. All supported catalogues
use the following names; the established IDs, enum names and numeric values,
shuffle order, save fields and command IDs/kinds remain stable. The enum type
remains `DuckWorldEventType`.

| Definition ID | Retained enum value | Weather report |
| --- | --- | --- |
| `rain_softened_seeds` | `RainSoftenedSeeds = 0` | Gentle Rain |
| `sunlit_signboards` (revisions 1–2 only) | `SunlitSignboards = 1` | Clear Sunlight |
| `a_friendly_guide` | `FriendlyGuide = 2` | Clearing Breeze |
| `a_pocket_of_driftwood` | `PocketOfDriftwood = 3` | Windfall Gusts |
| `all_tucked_in` | `AllTuckedIn = 4` | Evening Chill |
| `home_before_dark` | `HomeBeforeDark = 5` | Golden Sunset |
| `shared_supper` | `SharedSupper = 6` | Morning Dew |
| `still_air` | `StillAir = 7` | Still Air |
| `thick_morning_mist` | `ThickMorningMist = 8` | Thick Morning Mist |
| `restless_night` | `RestlessNight = 9` | Thundery Skies |
| `glorious_sunshine` (revisions 3–8) | `GloriousSunshine = 10` | Glorious Sunshine |

`DuckRuleDefinitions.WeatherReports` and `WeatherReport(id)` alias the retained
`WorldEvents` and `WorldEvent(id)` APIs. `DuckMatchView.CurrentWeatherReport`
aliases `CurrentEvent`, and `DuckDisplayReference.WeatherReport` overloads alias
`Event` and require `DuckRuleDefinitions` from the running catalogue. Retained
economy-only `Event` overloads keep their original legacy defaults; use the
catalogue overloads when displaying a match. Clients may adopt these weather-facing
entry points without translating IDs. Format-1 saves continue using
`WorldEventDeckDefinitionIds`, `CurrentEventIndex`, existing reward/penalty
fields and protection flags. The `event` CLI command remains available.
Previously recorded history text is preserved; new history uses weather names.

Only names and reference prose change. Clear Sunlight still previews two chips
in revisions 1–2. Glorious Sunshine keeps +3 Fresh Air through revision 5 and
+2 from revision 6; Warm Dreams keeps +8 Sleep in revision 3 and +2 Stars from
revision 4. Evening Chill requires safe shelter occupants through revision 4;
Thundery Skies applies only to safe shelter occupants through revision 4. Both
include worn-out shelter occupants from revision 5, before halving the complete
Star total. Golden Sunset always requires every duck to finish safely; Morning
Dew always counts physical Seed placements, including worn-out ducks. Thundery
Skies reduces only the combined printed shelter, Wildflowers and Final Night
shelter subtotal, with a floor of zero. Clearing Breeze and Still Air retain
each catalogue's Log consumption and protection rules described below.

## Match setup and seats

`DuckMatchSettings` validates two to four players and `WishSetId = "set-1"`.
Defaults remain two players, ten Days and zero starting Feathers. Set identity
names the encounter powers; it is distinct from the numeric rules revision.
The ordered player IDs are `human`, `ai`, `ai-2`, `ai-3`, using as many as the
setup needs. Every seat uses the same player state and legal-action contracts.

Format-1 `Settings.PlayerCount` and `Settings.WishSetId` are additive fields.
A missing/zero count means two players; a missing/empty set means Set 1 in the
recorded rules catalogue. Unknown sets, unsupported counts, mismatched player
records and invalid command/result references fail validation. Earlier rules
revisions remain two-player only; revisions 7–8 support two to four players.
Continue preserves the recorded setup and catalogue.

Core observations retain the viewer's own private data for policy clients.
The terminal interface intentionally hides changing pouch composition and
accumulated purchase lists. The opening recipe is shown before the first draw;
every player's current-Night purchases remain visible until Dawn clears them.
Explicit Signpost previews and already placed chips remain available.

## Settling projection

`DuckMatchView.RestPreview` is present during Adventure once the viewer has a
placed chip. It projects resting on that occupied space through the same reward
calculation used by Night, without issuing a command or consuming randomness.
Twigs earned today include already-banked Reeds/event Twigs; the separate signed
`NestTwigChangeOnRest` is the additional route change, excluding final Dream
Twigs. Feathers are exact. Star and Dream Twig minimum/maximum bounds distinguish
secure rewards from bonuses still dependent on the other ducks.

Shared-event, Flock and Most Rested statuses are `Unavailable`, `Possible` or
`Guaranteed`. Final-Day commitments remain private until their cohort resolves.
Clients must not present a possible bonus as earned. The property is absent
before a first placement and outside Adventure; resolved Night outcomes remain
the authority after settlement. Retained catalogues use their original currency
and conversion rules despite the current `Stars` property names.

## Pending Fallen Log

`LogSlowdownPending` retains its format-1 wire name. In revision 8 it is consumed
by the immediately next actual chip draw: a Wish moves half its total, rounded
up, and a white Obstacle moves normally. A new unprotected Log then arms a fresh
pending slowdown; a protected Log does not. Signpost previews and detached
observations do not consume it. Revisions 1–7 retain the next-Wish behavior,
including a pending Log surviving white chips and protection against a later Log.
Restore selects this behavior from `RulesVersion`; no save-field migration occurs.
