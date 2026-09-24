# Board and Dream shop

Rules revision 7 uses **Stars** as the Night currency. The Twig numeral is the
cumulative amount collected along the route by that space. Core adds that route
total once at Night, alongside Reeds/event Twigs and any Brambles deduction.
Never add together the Twig numerals of passed spaces. Revisions 1–3 retain
their recorded Sleep board rewards and prices.

## Cumulative route Twigs

The nest baseline is zero. The route total increases by one at each of
**1, 5, 9, 15, 20, 29, 30, 34 and 43**. A chip can travel past several of
these spaces; the destination's Twig value already includes them. A Feather
start also uses the destination's cumulative value. Core awards this total
once at Night. Reeds and event Twigs remain additional to the route total.

Wetlands occupy 1–14, meadow 15–28, wasteland 29–43. Eight shelters lie at
4, 10, 17, 21, 25, 32, 36 and 43. Their printed Stars are 2 in wetlands, 3 in
meadow and 4 in wasteland; ordinary spaces are 1, 2 and 1 respectively. The
oasis at 43 grants 5 Stars and 2 Feathers. A final-Day shelter adds one
further Star. The nest is separate and unscored.

## Every space

| Space | Biome | Stars | Twigs | Feathers | Shelter |
| --- | --- | ---: | ---: | ---: | --- |
| 1 | Wetlands | 1 | 1 | 0 | — |
| 2 | Wetlands | 1 | 1 | 0 | — |
| 3 | Wetlands | 1 | 1 | 0 | — |
| 4 | Wetlands | 2 | 1 | 1 | Reed hammock |
| 5 | Wetlands | 1 | 2 | 0 | — |
| 6 | Wetlands | 1 | 2 | 0 | — |
| 7 | Wetlands | 1 | 2 | 0 | — |
| 8 | Wetlands | 1 | 2 | 0 | — |
| 9 | Wetlands | 1 | 3 | 0 | — |
| 10 | Wetlands | 2 | 3 | 1 | Willow nest |
| 11 | Wetlands | 1 | 3 | 0 | — |
| 12 | Wetlands | 1 | 3 | 0 | — |
| 13 | Wetlands | 1 | 3 | 0 | — |
| 14 | Wetlands | 1 | 3 | 0 | — |
| 15 | Meadow | 2 | 4 | 0 | — |
| 16 | Meadow | 2 | 4 | 0 | — |
| 17 | Meadow | 3 | 4 | 1 | Clover hollow |
| 18 | Meadow | 2 | 4 | 0 | — |
| 19 | Meadow | 2 | 4 | 0 | — |
| 20 | Meadow | 2 | 5 | 0 | — |
| 21 | Meadow | 3 | 5 | 1 | Orchard shelter |
| 22 | Meadow | 2 | 5 | 0 | — |
| 23 | Meadow | 2 | 5 | 0 | — |
| 24 | Meadow | 2 | 5 | 0 | — |
| 25 | Meadow | 3 | 5 | 1 | Hayloft hideaway |
| 26 | Meadow | 2 | 5 | 0 | — |
| 27 | Meadow | 2 | 5 | 0 | — |
| 28 | Meadow | 2 | 5 | 0 | — |
| 29 | Wasteland | 1 | 6 | 0 | — |
| 30 | Wasteland | 1 | 7 | 0 | — |
| 31 | Wasteland | 1 | 7 | 0 | — |
| 32 | Wasteland | 4 | 7 | 2 | Shaded rock nook |
| 33 | Wasteland | 1 | 7 | 0 | — |
| 34 | Wasteland | 1 | 8 | 0 | — |
| 35 | Wasteland | 1 | 8 | 0 | — |
| 36 | Wasteland | 4 | 8 | 2 | Spring-fed refuge |
| 37 | Wasteland | 1 | 8 | 0 | — |
| 38 | Wasteland | 1 | 8 | 0 | — |
| 39 | Wasteland | 1 | 8 | 0 | — |
| 40 | Wasteland | 1 | 8 | 0 | — |
| 41 | Wasteland | 1 | 8 | 0 | — |
| 42 | Wasteland | 1 | 8 | 0 | — |
| 43 | Wasteland | 5 | 9 | 2 | Oasis sanctuary |

Editable data: [CSV](../../tests/GatheringSeason.Core.Tests/Fixtures/Duck/Rules/board.csv) and [JSON](../../tests/GatheringSeason.Core.Tests/Fixtures/Duck/Rules/board.json), with identical rows.

## Every shop offer

Prices are Stars and buy **one owned Wish chip**, not one use. Reeds quantity
changes Twig yield; it never changes movement or how many chips are purchased.

| Wish | Star price |
| --- | ---: |
| Seeds | 0 |
| Tailwind →2 | 1 |
| Tailwind →4 | 2 |
| Tailwind →6 | 3 |
| Signpost →2 / preview 1 | 2 |
| Refreshing splash | 1 |
| Nesting reeds ×1 | 2 |
| Nesting reeds ×2 | 3 |
| Nesting reeds ×3 | 4 |
| Companion duck | 2 |
| Wildflowers | 1 |

[Shop data](../../tests/GatheringSeason.Core.Tests/Fixtures/Duck/Rules/shop.json) records the approved policy: all Wishes are available
from Night 1 with unlimited stock; a duck may buy one per helpful type per Night
within the shared calendar’s 1/2/3 purchase limits. Free Seeds still use a
purchase slot and the one-per-type limit. Different Tailwind or Reeds variants
share a helpful type. Stars never carry to the next Day. Night 10 has no
shopping; every retained Star becomes a Dream Twig. White Obstacles, Goose,
player ducks, Feathers and the Most Rested award are not shop offers. Reeds are not
retroactively upgraded: each variant is a separate owned chip and pays only when
placed on a later Day.

New games use revision 7. [Compatibility](../architecture/compatibility.md)
explains why Continue never silently changes an existing game. Detailed payout
order belongs to [encounters](encounters.md); shared weather is specified in
[World Events](world-events.md).
