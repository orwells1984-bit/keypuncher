# Director bench, DESKTOP-TGBDC9K, 2026-11-06 04:49

Models gemma3:4b; variants baseline; 2 run(s) a case (the checker and habitat sets once: at temperature 0 the answers do not change). Author picks judged by qwen3:4b.

| model | cold load s | loaded | on GPU | fits 6 GB? |
| --- | --- | --- | --- | --- |
| gemma3:4b | 43,5 | 2,7 GB | 2,7 GB | yes |

## author

| model | variant | calls | valid JSON | passed rules | checker agrees | distinct names | median s | p90 s |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| gemma3:4b | baseline | 96 | 100% | 92% | 67% | 1,2 of 2 | 9,8 | 11,2 |

Why picks failed: checker: Animal, not Object 12x; already in the world 7x; checker: Animal, not ExtinctAnimal 5x; checker: fictional 3x; checker: unsure 2x; place in the name 1x; checker: Animal, not Vehicle 1x; checker: Plant, not Object 1x.

