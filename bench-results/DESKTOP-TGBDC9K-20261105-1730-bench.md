# Director bench, DESKTOP-TGBDC9K, 2026-11-05 19:41

Models qwen3:4b; variants A1, A2, A4, A8, best; 3 run(s) a case (the checker and habitat sets once: at temperature 0 the answers do not change).

| model | cold load s | loaded | on GPU | fits 6 GB? |
| --- | --- | --- | --- | --- |
| qwen3:4b | 20,7 | 3,0 GB | 3,0 GB | yes |

## author

| model | variant | calls | valid JSON | passed rules | checker agrees | distinct names | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| qwen3:4b | A1 | 144 | 100% | 62% | 47% | 1,6 of 3 | 6,6 | 6,9 |
| qwen3:4b | A2 | 144 | 100% | 76% | 65% | 2,0 of 3 | 3,7 | 5,4 |
| qwen3:4b | A4 | 144 | 100% | 62% | 44% | 1,5 of 3 | 6,2 | 6,9 |
| qwen3:4b | A8 | 144 | 100% | 66% | 53% | 1,4 of 3 | 6,6 | 7,5 |
| qwen3:4b | best | 144 | 100% | 69% | 51% | 1,6 of 3 | 6,4 | 7,4 |

Why picks failed: place in the name 119x; already in the world 66x; checker: Animal, not ExtinctAnimal 36x; too general, not one kind 26x; checker: fictional 22x; a placeholder, not a name 19x; checker: Plant, not Object 16x; checker: Animal, not Object 13x; checker: unsure 11x; a dinosaur shape for an animal alive today 5x; checker: Animal, not Vehicle 3x; checker: Animal, not Plant 2x; checker: Object, not Vehicle 2x; checker: Plant, not ExtinctAnimal 1x; the place, not a thing in it 1x; too many words 1x; not a plain English name 1x.

## checker

| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| qwen3:4b | A1 | 124 | 100% | 65% | 96% | 98% | 7% | 1,8 | 3,1 |
| qwen3:4b | A2 | 124 | 100% | 65% | 96% | 98% | 7% | 1,7 | 3,1 |
| qwen3:4b | A4 | 124 | 100% | 61% | 94% | 93% | 5% | 1,5 | 1,8 |
| qwen3:4b | A8 | 124 | 100% | 69% | 93% | 98% | 16% | 1,9 | 3,3 |
| qwen3:4b | best | 124 | 100% | 61% | 94% | 93% | 5% | 1,5 | 1,8 |

Most often wrong: steller's sea cow/ExtinctAnimal (5x: unsure, None: "unknown"); ball/Object (4x: unsure, None: "unknown"); glimmerwing moth/Animal (3x: real, Animal: "a moth species"); bumblebeet/Plant (3x: real, Plant: "a type of root vegetable"); glowberry/Plant (3x: real, Plant: "a type of berry"); kite/Object (3x: real, Animal: "a type of bird"); broadleaf/Plant (3x: real, Plant: "broadleaf"); kite/Animal (3x: real, Animal: "a type of bird"); icelandic poppy/Plant (2x: fictional, None: "unknown"); arctic thyme/Plant (2x: fictional, None: "unknown"); tram/Vehicle (2x: fictional, None: "unknown"); wadi rum gazelle/Animal (1x: real, Animal: "A type of gazelle found in the Wadi Rum desert in Jordan"); a vehicle/Vehicle (1x: real, Vehicle: "a vehicle").

## habitat

| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| qwen3:4b | A1 | 54 | 100% | 69% | 78% | 96% | 41% | 2,3 | 2,8 |
| qwen3:4b | A2 | 54 | 100% | 69% | 78% | 96% | 41% | 2,2 | 2,8 |
| qwen3:4b | A4 | 54 | 100% | 69% | 78% | 96% | 41% | 2,1 | 2,7 |
| qwen3:4b | A8 | 54 | 100% | 69% | 78% | 96% | 41% | 2,5 | 3,2 |
| qwen3:4b | best | 54 | 100% | 69% | 78% | 96% | 41% | 2,1 | 2,7 |

Most often wrong: gazelle at Lake Bled, Slovenia (5x: found there); jellyfish at Lake Bled, Slovenia (5x: found there); whale at Lake Bled, Slovenia (5x: found there); octopus at Lake Bled, Slovenia (5x: found there); reindeer at Kruger National Park, South Africa (5x: found there); seal at Kruger National Park, South Africa (5x: found there); camel at Hallormsstadaskogur, Iceland (5x: found there); starfish at Hallormsstadaskogur, Iceland (5x: found there); crab at Hallormsstadaskogur, Iceland (5x: found there); crab at Reynisfjara, Iceland (5x: not found there); sunflower at Reynisfjara, Iceland (5x: found there); squirrel at Reynisfjara, Iceland (5x: found there).

## scene

| model | variant | requests | accepted | tries a request | median s a try |
| --- | --- | --- | --- | --- | --- |
| qwen3:4b | A1 | 72 | 100% | 1,0 | 1,6 |
| qwen3:4b | A2 | 72 | 100% | 1,0 | 1,6 |
| qwen3:4b | A4 | 72 | 100% | 1,0 | 1,5 |
| qwen3:4b | A8 | 72 | 100% | 1,0 | 1,7 |
| qwen3:4b | best | 72 | 100% | 1,1 | 1,7 |

