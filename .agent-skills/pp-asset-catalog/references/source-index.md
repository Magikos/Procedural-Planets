# Asset source index

Paths below are repository-relative unless absolute. Read only sections relevant to the request.

| Source | Use | Evidence limits |
|---|---|---|
| `D:/Unity/Explore Assets/Assets` | Extracted source assets | Root verified 2026-09-09; individual assets require verification |
| `scratch-folders.txt` beside this file | Search top-level folder names before scanning files | Snapshot of 131 folders, 2026-09-09; names only, not a reviewed inventory |
| `docs/research/2026-08-10-external-asset-catalog.md` | Reviewed pack descriptions and historical compatibility findings | Survey dated 2026-08-10; not complete coverage of the current root |
| `docs/research/2026-08-11-asset-adoption-map.md` | Existing adoption decisions and intended project uses | Reconcile with current project assets and newer decisions |
| `docs/research/2026-08-10-unity-asset-store-purchases.tsv` | Search owned products absent from the extracted project | Ownership snapshot; does not prove local availability |
| `%APPDATA%/Unity/Asset Store-5.x` | Locate downloaded `.unitypackage` files if extraction searches fail | Resolve the environment variable and verify this directory first |
| `Assets/AssetPacks`, `Assets/Resources`, `Assets/Game Data` | Check assets already adopted by ProceduralPlanets | Search other `Assets` folders if these do not cover the candidate |

## Catalog routing

| Need | Catalog sections |
|---|---|
| Vegetation, rocks, scatter | 3: Category A |
| Ocean props and aquatic content | 4: Category B |
| Character and creature models | 5: Category C |
| Locomotion and swimming clips | 6.0–6.3: Animation |
| Interaction, survival, combat, social clips | 6.4–6.7: Animation |
| Movement and arbitrary-up prior art | 7: Character-controller prior art; also search the adoption map |
| Shader techniques | 8: Shaders and techniques |
| Editor tools and libraries | 9: Tools and libraries |
| Audio | 10: Audio; also search owned product titles |
| Import hazards and exclusions | 11 and 13 |
| Unresolved assessments | 12 |
| Owned but unextracted products | 15 and the purchase TSV |

Names and categories can mislead. Historical surveys missed animation under `Plugins/Threepeat`.
The notes identify `Action RPG Characters` as voice audio, not character models.
Verify these leads before reporting current contents.

The scratch project contains Asset Inventory tooling. The old catalog says it had not been initialized.
Check its current state before proposing a new indexing system; do not assume a database now exists.
Prefer the existing catalog and `rg` for a focused lookup.
