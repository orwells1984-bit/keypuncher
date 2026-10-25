# Director on the desktop, qwen3:4b, 2026-10-25

Commit 3676e09, GTX 1060 6 GB. `Keypuncher.Sandbox check` then `picks qwen3:4b 12` twice.

## Fact checker: 34 of 34 right

No inventions got through and no real things were refused. Each check took 1.7 s. The cases are 16 real
things, 13 inventions or story creatures (unicorn, gruffalo, pikachu, fluffaroo, crocoduck, sparkleraptor,
moonpetal flower, jabberwock, hippogriff and others), and 5 real things offered as the wrong kind:
dodo as alive, puffin as extinct, triceratops as alive, fennec fox as a plant. All five were refused.

## Picks

| run | requests that gave a real thing | tries |
| --- | --- | --- |
| A | 10 of 12 | 18 |
| B | 12 of 12 | 15 |

Run A families: dodo, cactus, potato, swing, kitchen, desert willow, woolly mammoth, seaweed, apples,
river turtle. Two invented families were left as they were.

Run B families: penguin, banana, beach ball, pear, woolly mammoth, boat, tomato, sailboat, carrot,
mollusk, apple, cave bear.

A request took 7 to 9 s, or 16 to 24 s when the model needed more than one try.

Both misses in run A were requests for an extinct animal. The model kept offering a puffin, which the
checker correctly called alive, so the family stayed as it was. After this run, "kitchen" (the place
named in the request, offered as an object) and "apples" next to "apple" are refused.

Earlier runs on the same day, before the fixes:
- Commit 68f55ef: 4 of 12. The model offered the same few names and filed food plants as objects.
- Commit 55ad456: 10 of 12, twice, but a puffin got through as extinct.
