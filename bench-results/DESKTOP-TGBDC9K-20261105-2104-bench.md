# Director bench, DESKTOP-TGBDC9K, 2026-11-05 23:25

Models gemma3:4b, llama3.1:8b, gemma3:1b; variants baseline, A2; 2 run(s) a case (the checker and habitat sets once: at temperature 0 the answers do not change).

| model | cold load s | loaded | on GPU | fits 6 GB? |
| --- | --- | --- | --- | --- |
| gemma3:4b | 37,1 | 2,7 GB | 2,7 GB | yes |
| llama3.1:8b | 37,4 | 5,2 GB | 4,0 GB | NO, partly on the CPU |
| gemma3:1b | 7,8 | 0,8 GB | 0,8 GB | yes |

## author

| model | variant | calls | valid JSON | passed rules | checker agrees | distinct names | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| gemma3:4b | baseline | 96 | 100% | 93% | 20% | 1,3 of 2 | 7,9 | 8,9 |
| gemma3:4b | A2 | 96 | 100% | 92% | 10% | 1,3 of 2 | 3,9 | 4,4 |
| llama3.1:8b | baseline | 96 | 100% | 97% | 88% | 1,5 of 2 | 12,8 | 13,9 |
| llama3.1:8b | A2 | 96 | 100% | 100% | 92% | 1,6 of 2 | 7,3 | 8,4 |
| gemma3:1b | baseline | 96 | 100% | 88% | 17% | 1,4 of 2 | 4,3 | 4,9 |
| gemma3:1b | A2 | 96 | 100% | 77% | 19% | 1,4 of 2 | 1,8 | 2,1 |

Why picks failed: checker: fictional 123x; checker: Animal, not ExtinctAnimal 62x; checker: Animal, not Object 34x; checker: Animal, not Vehicle 28x; already in the world 22x; checker: Animal, not Plant 21x; place in the name 20x; checker: unsure 12x; not a plain English name 9x; checker: Vehicle, not Object 7x; checker: Object, not Vehicle 1x; checker: Object, not Plant 1x; a dinosaur shape for an animal alive today 1x.

## checker

| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| gemma3:4b | baseline | 124 | 100% | 24% | 60% | 38% | 0% | 1,8 | 1,9 |
| gemma3:4b | A2 | 124 | 100% | 25% | 60% | 39% | 0% | 1,8 | 2,0 |
| llama3.1:8b | baseline | 124 | 100% | 71% | 90% | 98% | 23% | 3,6 | 3,9 |
| llama3.1:8b | A2 | 124 | 100% | 71% | 90% | 98% | 23% | 3,2 | 3,8 |
| gemma3:1b | baseline | 124 | 100% | 39% | 47% | 39% | 39% | 0,8 | 0,9 |
| gemma3:1b | A2 | 124 | 100% | 39% | 44% | 36% | 43% | 0,7 | 0,8 |

Most often wrong: irish elk/ExtinctAnimal (6x: real, Animal: "Animal"); woolly mammoth/ExtinctAnimal (4x: fictional, ExtinctAnimal: "ExtinctAnimal"); dodo/ExtinctAnimal (4x: fictional, ExtinctAnimal: "fictional"); ankylosaurus/ExtinctAnimal (4x: real, Animal: "Animal"); great auk/ExtinctAnimal (4x: fictional, Animal: "Animal"); thylacine/ExtinctAnimal (4x: fictional, Animal: "Animal"); steller's sea cow/ExtinctAnimal (4x: real, Animal: "A large, gentle marine mammal"); passenger pigeon/ExtinctAnimal (4x: fictional, Animal: "Animal"); quagga/ExtinctAnimal (4x: fictional, ExtinctAnimal: "Extinct animal"); dandelion/Plant (4x: fictional, Plant: "Plant"); strawberry/Plant (4x: fictional, Plant: "Plant"); pumpkin/Plant (4x: fictional, Plant: "Plant"); icelandic poppy/Plant (4x: fictional, Plant: "Plant"); arctic thyme/Plant (4x: fictional, Plant: "Plant"); edelweiss/Plant (4x: fictional, Plant: "Plant"); crowberry/Plant (4x: fictional, None: "fictional"); alpine rose/Plant (4x: fictional, Plant: "Plant"); marsh marigold/Plant (4x: fictional, Plant: "Plant"); sea thrift/Plant (4x: fictional, Plant: "Plant"); fire engine/Vehicle (4x: fictional, Vehicle: "Vehicle"); bicycle/Vehicle (4x: fictional, Vehicle: "Vehicle"); tram/Vehicle (4x: fictional, Vehicle: "Vehicle"); snowmobile/Vehicle (4x: fictional, Vehicle: "Vehicle"); ferry/Vehicle (4x: fictional, Vehicle: "Vehicle"); teapot/Object (4x: fictional, Object: "Object").

## habitat

| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| gemma3:4b | baseline | 54 | 100% | 76% | 70% | 96% | 56% | 2,4 | 2,9 |
| gemma3:4b | A2 | 54 | 100% | 80% | 67% | 96% | 63% | 2,4 | 3,1 |
| llama3.1:8b | baseline | 54 | 100% | 78% | 72% | 100% | 56% | 4,2 | 5,2 |
| llama3.1:8b | A2 | 54 | 100% | 78% | 72% | 100% | 56% | 3,5 | 4,3 |
| gemma3:1b | baseline | 54 | 100% | 91% | 56% | 96% | 85% | 1,0 | 1,2 |
| gemma3:1b | A2 | 54 | 100% | 80% | 67% | 96% | 63% | 1,0 | 1,3 |

Most often wrong: octopus at Lake Bled, Slovenia (6x: found there); mango at Lake Bled, Slovenia (6x: found there); dolphin at Wadi Rum, Jordan (6x: found there); seal at Kruger National Park, South Africa (6x: found there); camel at Hallormsstadaskogur, Iceland (6x: found there); sunflower at Reynisfjara, Iceland (6x: found there); squirrel at Reynisfjara, Iceland (6x: found there); jellyfish at Lake Bled, Slovenia (5x: found there); whale at Lake Bled, Slovenia (5x: found there); polar bear at Wadi Rum, Jordan (5x: found there); starfish at Hallormsstadaskogur, Iceland (5x: found there); gazelle at Lake Bled, Slovenia (4x: found there); fennec fox at Lake Bled, Slovenia (4x: found there); shark at Lake Bled, Slovenia (4x: found there); crab at Hallormsstadaskogur, Iceland (4x: found there); banana at Hallormsstadaskogur, Iceland (4x: found there); oak tree at Wadi Rum, Jordan (3x: found there); lion at Reynisfjara, Iceland (3x: found there); arctic fox at Hallormsstadaskogur, Iceland (2x: not found there); penguin at Wadi Rum, Jordan (2x: found there); salmon at Wadi Rum, Jordan (2x: found there); reindeer at Kruger National Park, South Africa (2x: found there); arctic fox at Kruger National Park, South Africa (2x: found there); birch tree at Kruger National Park, South Africa (2x: found there); giraffe at Hallormsstadaskogur, Iceland (2x: found there).

## scene

| model | variant | requests | accepted | tries a request | median s a try |
| --- | --- | --- | --- | --- | --- |
| gemma3:4b | baseline | 48 | 100% | 1,1 | 1,9 |
| gemma3:4b | A2 | 48 | 100% | 1,3 | 2,0 |
| llama3.1:8b | baseline | 48 | 100% | 1,1 | 3,6 |
| llama3.1:8b | A2 | 48 | 100% | 1,0 | 2,9 |
| gemma3:1b | baseline | 48 | 98% | 1,9 | 1,6 |
| gemma3:1b | A2 | 48 | 94% | 1,9 | 2,0 |

Most often wrong: s11 (2x: checker: not a real place); s06 (2x: checker: not a real place).

