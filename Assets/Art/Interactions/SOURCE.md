# Interaction props and performance data

Mostly ours. Three meshes are sourced.

| Ours | Source |
| --- | --- |
| `Fish_01.fbx` | `Models/Fish_SkelMesh.FBX` |
| `FishingPole_01.fbx` | `Models/WoodenFishingPole_SkelMesh.FBX` |
| `Bowl_01.fbx` | `TEM_Bowl_01A.fbx` |

The first two come from D:/Unity/Explore Assets/Assets/Survival_Animations/
(pack: Survival Animations). `Bowl_01.fbx` came from the TEM pack by hand, which
is why it carries no `AssetOrigin` metadata. Each `.meta` moved with its file, so
the GUIDs and every scene reference survived the rename.

Ours: the four `* Performances.asset` files, the `Beam Review`,
`Ledge Walk Review` and `Rope Review` prefabs, `Climbing Rope.asset`,
`Fishing Rod Mesh.asset`, `Bow Wood.mat` and `Fishing Review Water.mat`. These are
authored by our editor tools and consumed by the interaction system.

`Definitions/`, `Recipes/`, `Animations/` and `RoundOne/` each carry their own
record.
