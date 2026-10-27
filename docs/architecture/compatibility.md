# Compatibility and retained technical names

The product is **Gathering Season**. Solution and .NET project paths use
`GatheringSeason.*`; current player-facing language uses **shelter**.

C# namespaces and assembly names use `GatheringSeason.*`. The public Core DLL
is `GatheringSeason.Core.dll`; tests use `GatheringSeason.Core.Tests`.
Save profile IDs, format version and existing enum numeric values remain stable.
New enum values append instead of changing serialized meanings.
`GameActionKind` retains Explore=11, Settle=12, BuyEncounter=13, FinishDream=14,
NextDay=15 and ChooseEventBenefit=16. ChooseTailwind=17 is appended for the
pending Set 2 destination choice. Legacy JSON adapters retain required
wire fields without changing runtime terminology.

Core v2 executes only rules revision 8: ten Days, 43 spaces, zero starting
Feathers, Stars and Wishes, the current meadow shelters at 17/21/25 and fifteen
weather reports (play ten, leave five). `DuckRules.V1` remains the unchanged
Set 1 catalogue; `DuckRules.Set2` supplies the alternate powers and prices.
`DuckRules.ForWishSet(id)` resolves either immutable catalogue. `V1` names the
stable product profile, not a release or rules revision. There is no rules
revision selector in the API or CLI; Wish-set selection is separate.

Core v2 is a breaking compatibility release: revisions 1–7, missing/zero and
future rules revisions are rejected before state construction. Earlier ten-card
revision-8 saves are also invalid. There is no migration or automatic deletion.
Continue preserves source files on failure and can recover a valid current
backup. Accepted saves preserve paid Stars, earned rewards, weather and pouch
order, pending weather choices and final-Day commitments exactly.

Format-1 saves retain their established `Sleep` wire names; these values mean
Stars. `RulesVersion = 8`, `FormatVersion = 1` and profile ID
`gathering-season.duck.v1` remain unchanged. The
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
`MatchSession<DuckMatchView>`. Unknown profiles are unsupported.

## Weather reports and retained event identities

Current weather reports retain their display names, established IDs, enum
names and numeric values. Numeric value 1 is reserved; it is not a current
weather report. The removed Clear Sunlight member does not renumber surviving
values. Five BASE additions use values 11–15. Save fields and command IDs/kinds
remain stable, as do catalogue order and seeded shuffles. The enum type remains
`DuckWorldEventType`.

| Definition ID | Retained enum value | Weather report |
| --- | --- | --- |
| `rain_softened_seeds` | `RainSoftenedSeeds = 0` | Gentle Rain |
| `a_friendly_guide` | `FriendlyGuide = 2` | Clearing Breeze |
| `a_pocket_of_driftwood` | `PocketOfDriftwood = 3` | Windfall Gusts |
| `all_tucked_in` | `AllTuckedIn = 4` | Evening Chill |
| `home_before_dark` | `HomeBeforeDark = 5` | Golden Sunset |
| `shared_supper` | `SharedSupper = 6` | Morning Dew |
| `still_air` | `StillAir = 7` | Still Air |
| `thick_morning_mist` | `ThickMorningMist = 8` | Thick Morning Mist |
| `restless_night` | `RestlessNight = 9` | Thundery Skies |
| `glorious_sunshine` | `GloriousSunshine = 10` | Glorious Sunshine |
| `golden_morning` | `GoldenMorning = 11` | Golden Morning |
| `refreshing_showers` | `RefreshingShowers = 12` | Refreshing Showers |
| `favourable_winds` | `FavourableWinds = 13` | Favourable Winds |
| `crosswinds` | `Crosswinds = 14` | Crosswinds |
| `low_cloud` | `LowCloud = 15` | Low Cloud |

`DuckRuleDefinitions.WeatherReports` and `WeatherReport(id)` alias the retained
`WorldEvents` and `WorldEvent(id)` APIs. `DuckMatchView.CurrentWeatherReport`
aliases `CurrentEvent`, and `DuckDisplayReference.WeatherReport` overloads alias
`Event` and require the current `DuckRuleDefinitions`. Economy-only `Encounter`,
`Event`, `Offer` and `Glossary` overloads with historical defaults have been
removed. Clients may adopt these weather-facing entry points without translating IDs. Format-1 saves continue using
`WorldEventDeckDefinitionIds`, `CurrentEventIndex`, existing reward/penalty
fields and protection flags. The `event` CLI command remains available.
Previously recorded history text is preserved; new history uses weather names.

Fresh Air adds +2 safe Exhaustion; Starry Night adds +2 Stars. Evening Chill
and Thundery Skies include worn-out shelter occupants before halving the
complete Star total. Golden Sunset requires every duck to finish safely;
Morning Dew counts physical Seed placements, including worn-out ducks.
Thundery Skies reduces only the combined printed shelter, Wildflowers and Final
Night shelter subtotal, with a floor of zero. See the current
[weather rules](../rules/world-events.md) for every report.

The Sunshine benefit is displayed as **Starry Night**. Its existing
`duck.event.glorious-sunshine.warm-dreams` action ID, `WarmDreams` enum value and
reward fields remain unchanged. Previously saved history remains verbatim.

## Match setup and seats

`DuckMatchSettings` validates two to four players and `WishSetId` equal to
`"set-1"` or `"set-2"`, each with `WishSetVersion = 1`.
Defaults remain two players, ten Days and zero starting Feathers. Set identity
names the encounter powers; it is distinct from the numeric rules revision.
The ordered player IDs are `human`, `ai`, `ai-2`, `ai-3`, using as many as the
setup needs. Every seat uses the same player state and legal-action contracts.

Format-1 `Settings.PlayerCount` and `Settings.WishSetId` are additive fields.
A missing/zero count means two players; a missing/empty set means Set 1 in the
current rules catalogue. Missing/zero `Settings.WishSetVersion` means version 1
only for legacy Set 1 data. Set 2 saves require explicit version 1. Unknown sets
or versions, unsupported counts, mismatched player records and invalid
command/result references fail validation before state construction. Continue
preserves the recorded setup. The existing two-argument settings constructor is
retained; a separate three-argument overload accepts the set version.

Older binaries already reject `set-2`. They ignore the new version field for
Set 1, so Set 1/version 1 remains frozen: an incompatible future power change
would require an identity or format those binaries reject.

Core observations retain the viewer's own private data. `OwnBag` is sorted by
physical ID, never draw order. The CLI's exact next-draw probabilities now make
the viewer's remaining composition available without exposing hidden order or
opponent pouches. The opening recipe and placed chips remain available;
current-Night purchases clear at Dawn and no accumulated purchase ledger is
introduced.

`KnownNextChips` retains its ordered meaning. `UnorderedNextChips` is a Set 2
Signpost pair presented in canonical physical-ID order. Drawing a member makes
the remaining chip known. Overlapping peeks preserve inferences: if a previous
pair was `{Signpost, A}`, drawing Signpost makes A next; its new `{A, B}` peek
then establishes the ordered prefix A, B. Repeated observations do not consume
randomness or advance the pouch. Saved unordered IDs must match the actual
next two chips as a set; ordered IDs must match an actual prefix.

## Tailwind choice and probability observations

After a Set 2 Tailwind is revealed, `OwnPendingTailwind` supplies its definition,
full movement/destination and first reachable shelter, if any. The issued
`duck.tailwind.full` and `duck.tailwind.shelter` actions complete that same draw.
The latter settles immediately. Its chip stays reserved at the pouch head;
it is not placed or removed twice, and ordinary draw/rest actions are unavailable
until the choice resolves. Pending state survives save/restore, which issues
fresh commands and validates the reserved chip and reachable destination.

On Day 10, all ordinary Draw/Settle commitments precede any Tailwind choices.
The entire cohort stays at the preceding public state until every required
choice is submitted, then resolves and reveals together. Only the choosing
player sees their own pending chip and destinations.

`DuckMatchView.DrawProbability` is available only when Explore is currently
legal. It is absent during a pending Tailwind choice, after a Day-10 commitment,
outside Adventure or after finishing. Each outcome has a definition ID, exact
integer `Numerator`/`Denominator`, `Probability` and `WearsOut`.
`WearOutNumerator` with the shared `Denominator` gives the combined immediate
wear-out probability. The candidate pool is the first ordered known chip,
otherwise the unordered pair, otherwise all remaining chips. Duplicate variants
combine into one outcome. These weights describe player knowledge and do not
inspect the hidden draw order.

Wear-out predictions use the same authoritative encounter transition as play,
including the selected Splash power, Goose's safe-limit reduction, Clearing
Breeze and weather. Observation never mutates state or consumes randomness.

## Settling projection

`DuckMatchView.RestPreview` is present during Adventure once the viewer has a
placed chip. It projects resting on that occupied space through the same reward
calculation used by Night, without issuing a command or consuming randomness.
Twigs earned today include already-banked Set 1 Reeds/event Twigs. Set 2 Reeds
are conditional until settlement and count only at a shelter; they enter the
nest exactly once at Night. The separate signed `NestTwigChangeOnRest` includes
the additional route change and eligible unbanked Set 2 Reeds, excluding final
Dream Twigs. No rest preview is offered during a pending Tailwind choice.
Feathers are exact. Star and Dream Twig minimum/maximum bounds distinguish
secure rewards from bonuses still dependent on the other ducks.

Shared-event, Flock and Most Rested statuses are `Unavailable`, `Possible` or
`Guaranteed`. Final-Day commitments remain private until their cohort resolves.
Clients must not present a possible bonus as earned. The property is absent
before a first placement and outside Adventure; resolved Night outcomes remain
the authority after settlement. Stars convert one for one into final Dream Twigs.

## Pending Fallen Log

`LogSlowdownPending` retains its format-1 wire name. In revision 8 it is consumed
by the immediately next actual chip draw: a Wish moves half its total, rounded
up, and a white Obstacle moves normally. A new unprotected Log then arms a fresh
pending slowdown; a protected Log does not. Signpost previews and detached
observations do not consume it. Restore validates `RulesVersion = 8`; no
save-field migration occurs.
