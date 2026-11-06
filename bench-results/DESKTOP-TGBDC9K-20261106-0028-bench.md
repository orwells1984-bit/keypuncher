# Director bench, DESKTOP-TGBDC9K, 2026-11-06 04:16 (STOPPED EARLY)

**Stopped at 04:16 by --until before every case ran. The figures below are for part of each set, in set order, and do not compare with a full run.**

Models llama3.2:3b, phi4-mini, qwen3:8b, llama3.1:8b, gemma3:4b; variants A2, baseline; 2 run(s) a case (the checker and habitat sets once: at temperature 0 the answers do not change). Author picks judged by qwen3:4b.

| model | cold load s | loaded | on GPU | fits 6 GB? |
| --- | --- | --- | --- | --- |
| llama3.2:3b | 17,5 | 2,4 GB | 2,4 GB | yes |
| phi4-mini | 22,2 | 2,9 GB | 2,9 GB | yes |
| qwen3:8b | 37,7 | 5,6 GB | 4,1 GB | NO, partly on the CPU |
| llama3.1:8b | 37,3 | 5,2 GB | 4,0 GB | NO, partly on the CPU |
| gemma3:4b | 26,5 | 2,7 GB | 2,7 GB | yes |

## author

| model | variant | calls | valid JSON | passed rules | checker agrees | distinct names | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| llama3.2:3b | A2 | 96 | 100% | 96% | 81% | 1,6 of 2 | 2,5 | 2,9 |
| llama3.2:3b | baseline | 96 | 100% | 97% | 79% | 1,6 of 2 | 4,5 | 5,2 |
| phi4-mini | A2 | 96 | 100% | 77% | 67% | 1,3 of 2 | 4,8 | 5,3 |
| phi4-mini | baseline | 96 | 100% | 74% | 61% | 1,4 of 2 | 5,4 | 6,3 |
| qwen3:8b | A2 | 96 | 100% | 91% | 85% | 1,4 of 2 | 9,3 | 10,4 |
| qwen3:8b | baseline | 96 | 100% | 99% | 92% | 1,3 of 2 | 13,8 | 15,6 |
| llama3.1:8b | A2 | 96 | 100% | 99% | 83% | 1,6 of 2 | 8,6 | 10,1 |
| llama3.1:8b | baseline | 96 | 100% | 95% | 85% | 1,5 of 2 | 16,2 | 17,9 |
| gemma3:4b | A2 | 96 | 100% | 94% | 64% | 1,2 of 2 | 4,3 | 4,7 |
| gemma3:4b | baseline | 39 | 100% | 92% | 62% | 1,0 of 2 | 8,5 | 10,1 |

Why picks failed: already in the world 48x; checker: unsure 45x; checker: Animal, not Object 25x; place in the name 24x; checker: Vehicle, not Object 17x; checker: Animal, not ExtinctAnimal 16x; checker: Animal, not Vehicle 14x; checker: fictional 6x; checker: Object, not Vehicle 4x; too general, not one kind 3x; checker: Animal, not Plant 2x; not a plain English name 1x; from a story, not real 1x; an animal shape for a thing that is not an animal 1x; should have been great auk 1x; checker: Plant, not Object 1x.

## checker

| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| llama3.2:3b | A2 | 124 | 100% | 69% | 89% | 95% | 23% | 1,3 | 1,6 |
| llama3.2:3b | baseline | 124 | 100% | 69% | 89% | 95% | 23% | 1,4 | 1,9 |
| phi4-mini | A2 | 124 | 100% | 8% | 44% | 13% | 0% | 1,4 | 1,6 |
| phi4-mini | baseline | 124 | 100% | 8% | 44% | 13% | 0% | 1,4 | 1,6 |
| qwen3:8b | A2 | 124 | 100% | 68% | 97% | 100% | 9% | 4,1 | 7,3 |
| qwen3:8b | baseline | 124 | 100% | 68% | 97% | 100% | 9% | 4,4 | 7,4 |
| llama3.1:8b | A2 | 124 | 100% | 71% | 90% | 98% | 23% | 3,5 | 4,0 |
| llama3.1:8b | baseline | 124 | 100% | 71% | 90% | 98% | 23% | 4,6 | 5,1 |
| gemma3:4b | A2 | 124 | 100% | 24% | 60% | 38% | 0% | 2,1 | 2,2 |

Most often wrong: irish elk/ExtinctAnimal (7x: unsure, None: "unknown"); wadi rum gazelle/Animal (6x: real, Animal: "a type of gazelle found in Wadi Rum desert"); broadleaf/Plant (6x: real, Plant: "a broadleaf tree"); a vehicle/Vehicle (6x: real, Vehicle: "a vehicle"); steller's sea cow/ExtinctAnimal (5x: real, Animal: "a large marine mammal"); passenger pigeon/ExtinctAnimal (5x: real, Animal: "a type of bird"); quagga/ExtinctAnimal (5x: real, Animal: "a type of zebra"); pikachu/Animal (4x: real, Animal: "a small, yellow rodent-like creature with electric powers"); snorlax/Animal (4x: real, Animal: "a large, sleeping Pokémon"); glimmerwing moth/Animal (4x: real, Animal: "a type of moth"); moonpetal flower/Plant (4x: real, Plant: "a type of orchid"); rowing boat/Vehicle (4x: fictional, None: "unknown"); ladybird/Animal (3x: fictional, Animal: "fictional"); mute swan/Animal (3x: fictional, None: "unknown"); sand cat/Animal (3x: fictional, None: "unknown"); axolotl/Animal (3x: fictional, None: "unknown"); mudfish/Animal (3x: fictional, None: "unknown"); salamander/Animal (3x: fictional, None: "unknown"); chamois/Animal (3x: fictional, None: "unknown"); redwing/Animal (3x: fictional, None: "unknown"); grass snake/Animal (3x: fictional, None: "unknown"); woolly mammoth/ExtinctAnimal (3x: fictional, ExtinctAnimal: "extinct animal"); dodo/ExtinctAnimal (3x: fictional, ExtinctAnimal: "unknown"); ankylosaurus/ExtinctAnimal (3x: fictional, ExtinctAnimal: "unknown"); great auk/ExtinctAnimal (3x: fictional, ExtinctAnimal: "extinct animal").

## habitat

| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| llama3.2:3b | A2 | 54 | 100% | 98% | 52% | 100% | 96% | 1,5 | 1,9 |
| llama3.2:3b | baseline | 54 | 100% | 98% | 52% | 100% | 96% | 1,7 | 2,2 |
| phi4-mini | A2 | 54 | 100% | 74% | 72% | 96% | 52% | 2,0 | 2,3 |
| phi4-mini | baseline | 54 | 100% | 74% | 72% | 96% | 52% | 2,1 | 2,6 |
| qwen3:8b | A2 | 54 | 100% | 80% | 70% | 100% | 59% | 4,9 | 6,0 |
| qwen3:8b | baseline | 54 | 100% | 80% | 70% | 100% | 59% | 4,9 | 5,5 |
| llama3.1:8b | A2 | 54 | 100% | 78% | 72% | 100% | 56% | 4,0 | 4,9 |
| llama3.1:8b | baseline | 54 | 100% | 78% | 72% | 100% | 56% | 5,2 | 6,2 |
| gemma3:4b | A2 | 54 | 100% | 76% | 70% | 96% | 56% | 2,7 | 3,4 |

Most often wrong: shark at Lake Bled, Slovenia (9x: found there); jellyfish at Lake Bled, Slovenia (9x: found there); whale at Lake Bled, Slovenia (9x: found there); octopus at Lake Bled, Slovenia (9x: found there); mango at Lake Bled, Slovenia (9x: found there); dolphin at Wadi Rum, Jordan (9x: found there); seal at Kruger National Park, South Africa (9x: found there); starfish at Hallormsstadaskogur, Iceland (9x: found there); crab at Hallormsstadaskogur, Iceland (9x: found there); sunflower at Reynisfjara, Iceland (9x: found there); squirrel at Reynisfjara, Iceland (9x: found there); banana at Hallormsstadaskogur, Iceland (6x: found there); gazelle at Lake Bled, Slovenia (5x: found there); fennec fox at Lake Bled, Slovenia (5x: found there); polar bear at Wadi Rum, Jordan (5x: found there); coconut palm at Lake Bled, Slovenia (4x: found there); oak tree at Wadi Rum, Jordan (4x: found there); salmon at Wadi Rum, Jordan (4x: found there); arctic fox at Kruger National Park, South Africa (4x: found there); cactus at Reynisfjara, Iceland (4x: found there); lion at Reynisfjara, Iceland (4x: found there); camel at Hallormsstadaskogur, Iceland (3x: found there); kangaroo at Lake Bled, Slovenia (2x: found there); penguin at Wadi Rum, Jordan (2x: found there); reindeer at Kruger National Park, South Africa (2x: found there).

