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
| 1 | Original Sleep prices: Tailwind 5/10/15, Reeds 6/11/16 | Original deck with Sunlit Signboards |
| 2 | Sleep prices: Tailwind 4/8/12, Reeds 8/14/20 | Original deck with Sunlit Signboards |
| 3 | Same Sleep prices as revision 2 | Glorious Sunshine replaces Sunlit Signboards |
| 4 | Stars and Wishes: Seeds 0; Tailwinds 1/2/3; Reeds 2/3/4; older safe-only reward gates | Glorious Sunshine deck |
| 5 | Same Stars, prices and board; exhausted ducks keep Flock, shelter bonuses and Feathers before Star halving | Same ten events; All Tucked In/Restless Night include exhausted shelter occupants |
| 6 | Same Star prices; meadow shelters are 16/22/25 and space 28 has five Twigs | Same ten events; Fresh Air is +2 safe Exhaustion |
| 7 (new games) | Same Star prices; meadow shelters are 17/21/25, with current 15–25 reward values | Same ten events; Fresh Air is +2 safe Exhaustion |

Continue restores the recorded catalogue. It never retroactively changes
purchases, paid Sleep/Stars, event order or already-earned rewards. Sunlit Signboards’
two-token preview survives only for old matches. New games have ten events,
not eleven. Exact pending weather choices and final-Day commitments are saved.

Format-1 saves retain their established `Sleep` wire names. `RulesVersion`
distinguishes whether those values mean legacy Sleep or revision 4–7 Stars. The
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
