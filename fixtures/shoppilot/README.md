# `.shoppilot` fixture packages (SPK-SHAKEb)

Vendored copy of ShopPilot `fixtures/shoppilot` so VectorPilot CI can round-trip
Mac documents without checking out ShopPilot as a sibling.

Real document packages for the Calibration + Sign flows. Both were generated
from the actual model/recipe code (not hand-rolled JSON) so they always match
the on-disk schema.

| Package | Job | Contents | Use |
| --- | --- | --- | --- |
| `Calibration.shoppilot` | Calibration | 200×200×18 mm sheet, "Cut" layer with a closed 50×50 mm square, precomputed **Profile 1** toolpath (real `ProfileToolpathEngine` output) | G1-A calibration flow without a recipe; SPK-SHAKEd round-trip input |
| `Sign.shoppilot` | Sign SHOP | Sign stock 457.2×609.6×19.05, **Text** layer (4 glyph curves), **Border** layer (1 rect), precomputed **V-Carve 1 (Recipe)** toolpath (408 lines, `SignRecipeManager` output) | G1-B / G2 sign flow; SPK-SHAKEd round-trip input |

## Regenerating

The generator is a checked-in SPM executable target (kept deliberately — the
packages must stay reproducible):

```bash
./scripts/swift_locked.sh run ShopPilotFixtureGen   # rewrites both packages
```

It builds each job with the real recipe/engine code, saves via `DocumentSaver`,
then re-loads via `DocumentLoader` and asserts the round-trip before exiting
non-zero on any mismatch.

> These are SIM inputs like every fixture — never point a real machine at them
> without human verification of travel limits.
