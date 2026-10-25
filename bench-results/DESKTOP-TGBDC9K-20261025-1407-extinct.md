# Extinct-animal picks on the desktop, qwen3:4b, 2026-10-25

Each run is `Keypuncher.Sandbox extinct qwen3:4b 12`: 12 requests for extinct animals in the creature
families, with the place drawn at random.

| commit | change | requests that gave a real thing | tries |
| --- | --- | --- | --- |
| 734d51a (main before) | none | about 1 in 4 extinct requests failed in mixed picks; the first try was nearly always "puffin" | |
| b660a66 | no "puffin" example; "alive nowhere today"; places name eras | 12 of 12, 10 of 12 | 25, 24 |
| 2eb3872 | group names refused ("extinct animal"); suggestions on every try; one well-known extinct animal as a last resort | 12 of 12, 12 of 12, 12 of 12 | 17, 16, 14 |

Animals picked in the 2eb3872 runs:
- Dinosaurs: brachiosaurus, velociraptor, allosaurus, spinosaurus.
- Flying reptiles: pteranodon, pterodactyl.
- Sea animals: plesiosaur, ichthyosaur, megalodon, ammonite, trilobite.
- Ice Age mammals: woolly mammoth, cave bear, sabre-toothed cat.
- Birds: archaeopteryx, dodo, great auk, passenger pigeon.
- Other: thylacine.

A mixed run of `picks` at 2eb3872 gave a real thing for 12 of 12 requests, in 16 tries.

Things it still gets wrong, without failing a request:
- The era often does not match the place asked for. One example is spinosaurus for "flew in the time of
  the dinosaurs".
- The dodo's fact sometimes names Rodrigues instead of Mauritius.
- The checker is unsure of woolly rhinoceros and dimetrodon.
