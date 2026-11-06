# Director bench, DESKTOP-TGBDC9K, 2026-11-05 15:11

Models qwen3:4b, qwen3:1.7b; variants baseline; 3 run(s) a case (the checker and habitat sets once: at temperature 0 the answers do not change).

| model | cold load s | loaded | on GPU | fits 6 GB? |
| --- | --- | --- | --- | --- |
| qwen3:4b | 19,0 | 3,0 GB | 3,0 GB | yes |
| qwen3:1.7b | 13,6 | 1,6 GB | 1,6 GB | yes |

## author

| model | variant | calls | valid JSON | passed rules | checker agrees | distinct names | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| qwen3:4b | baseline | 144 | 100% | 63% | 51% | 1,6 of 3 | 6,8 | 7,4 |
| qwen3:1.7b | baseline | 144 | 100% | 96% | 53% | 1,8 of 3 | 3,6 | 4,3 |

Why picks failed: checker: Animal, not ExtinctAnimal 35x; already in the world 19x; too general, not one kind 18x; place in the name 13x; checker: None, not Object 13x; checker: Object, not Vehicle 10x; a placeholder, not a name 7x; checker: fictional 5x; checker: unsure 4x; checker: None, not Vehicle 4x; checker: Animal, not Vehicle 3x; checker: None, not Plant 3x; checker: Animal, not Object 2x; an animal shape for a thing that is not an animal 1x; too many words 1x.

## checker

| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| qwen3:4b | baseline | 124 | 100% | 65% | 96% | 98% | 7% | 1,8 | 3,1 |
| qwen3:1.7b | baseline | 124 | 100% | 67% | 85% | 90% | 25% | 1,2 | 1,3 |

Most often wrong: glimmerwing moth/Animal (2x: real, Animal: "a moth species"); steller's sea cow/ExtinctAnimal (1x: unsure, None: "unknown"); ball/Object (1x: unsure, None: "unknown"); bumblebeet/Plant (1x: real, Plant: "a type of root vegetable"); glowberry/Plant (1x: real, Plant: "a type of berry"); redwing/Animal (1x: fictional, None: "A redwing is a type of bird, but it's not commonly known. It's a small bird with"); dodo/ExtinctAnimal (1x: real, Animal: "A type of bird"); irish elk/ExtinctAnimal (1x: fictional, None: "An Irish elk is a type of animal, but it is not a real animal today. It is a pre"); quagga/ExtinctAnimal (1x: real, Animal: "A quagga is a type of antelope native to Africa. It is known for its distinctive"); rowing boat/Vehicle (1x: real, Object: "A rowing boat is a type of boat used for rowing, typically in water, to move and"); fishing boat/Vehicle (1x: real, Object: "A boat used for fishing."); hot-air balloon/Vehicle (1x: real, Object: "A hot-air balloon is a toy or a real object that rises into the air because of a"); kite/Object (1x: fictional, None: "A kite is a flying object with a tail and wings, usually made of fabric, used to"); crocoduck/Animal (1x: real, Animal: "A crocoduck is a type of animal that looks like a crocodile but has the features"); wadi rum gazelle/Animal (1x: real, Animal: "The Wadi Rum Gazelle is a type of gazelle found in the Wadi Rum region of Saudiʾ"); sunshine tree/Plant (1x: real, Plant: "A sunshine tree is a type of tree that is known for its bright, golden color, as"); hoverboard/Vehicle (1x: real, Vehicle: "A hoverboard is a small electric vehicle that can move on two wheels, usually on"); batmobile/Vehicle (1x: real, Vehicle: "A vehicle"); thunder car/Vehicle (1x: real, Vehicle: "A Thunder Car is a type of vehicle that is powered by an engine and is designed "); sparkle cup/Object (1x: real, Object: "A sparkle cup is a small, colorful container that is often used in children's玩耍,"); a vehicle/Vehicle (1x: real, Vehicle: "A vehicle"); dodo/Animal (1x: real, Animal: "A type of bird"); triceratops/Animal (1x: real, Animal: "A triceratops is a prehistoric animal with three horns and a long tail. It lived").

## habitat

| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| qwen3:4b | baseline | 54 | 100% | 69% | 78% | 96% | 41% | 2,3 | 2,9 |
| qwen3:1.7b | baseline | 54 | 100% | 50% | 59% | 59% | 41% | 1,2 | 1,6 |

Most often wrong: gazelle at Lake Bled, Slovenia (2x: found there); jellyfish at Lake Bled, Slovenia (2x: found there); octopus at Lake Bled, Slovenia (2x: found there); reindeer at Kruger National Park, South Africa (2x: found there); starfish at Hallormsstadaskogur, Iceland (2x: found there); crab at Hallormsstadaskogur, Iceland (2x: found there); crab at Reynisfjara, Iceland (2x: not found there); whale at Lake Bled, Slovenia (1x: found there); seal at Kruger National Park, South Africa (1x: found there); camel at Hallormsstadaskogur, Iceland (1x: found there); sunflower at Reynisfjara, Iceland (1x: found there); squirrel at Reynisfjara, Iceland (1x: found there); mute swan at Lake Bled, Slovenia (1x: not found there); mink at Lake Bled, Slovenia (1x: not found there); pike at Lake Bled, Slovenia (1x: not found there); roe deer at Lake Bled, Slovenia (1x: not found there); beech tree at Lake Bled, Slovenia (1x: not found there); fennec fox at Lake Bled, Slovenia (1x: found there); fennec fox at Wadi Rum, Jordan (1x: not found there); oak tree at Wadi Rum, Jordan (1x: found there); salmon at Wadi Rum, Jordan (1x: found there); birch tree at Hallormsstadaskogur, Iceland (1x: not found there); bilberry at Hallormsstadaskogur, Iceland (1x: not found there); reindeer at Hallormsstadaskogur, Iceland (1x: not found there); giraffe at Hallormsstadaskogur, Iceland (1x: found there).

## scene

| model | variant | requests | accepted | tries a request | median s a try |
| --- | --- | --- | --- | --- | --- |
| qwen3:4b | baseline | 72 | 100% | 1,0 | 1,7 |
| qwen3:1.7b | baseline | 72 | 100% | 1,2 | 0,9 |

