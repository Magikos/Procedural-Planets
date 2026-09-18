# Generic scatter props

Source: D:/Unity/Explore Assets/Assets/Synty/PolygonGeneric/
Pack: POLYGON - Fantasy Kingdom Pack (Synty)

Taken: seven meshes from `Models/` (two bushes, a flower cluster, two rocks, a
tree and a pine) and two atlas textures from `Textures/Alts/`. Filenames were shortened; the `.meta`
moved with each file so the GUIDs survived.

Ours: the seven `.prefab` files. They are our assemblies, not the pack's prefabs.
None of them is referenced by anything today — the live planet consumes the FBXs
through the scatter prototypes in `Assets/Resources/Settings/Scatter/`, not through
these prefabs.

Two atlases, not a duplicate pair. `Generic_Atlas_Props.png` and
`Generic_Atlas_Foliage.png` hold identical pixels but are imported differently on
purpose: the props copy uses point filtering and `alphaTestReferenceValue` 0.5 for
the flat prop look, and the foliage copy uses bilinear filtering,
`mipMapsPreserveCoverage` and a 4096 size override so alpha-cut leaves survive
mipping. `ScatterProps.mat` consumes the first and `FoliagePine.mat` the second.
Do not merge them.

One pine, not two. `Tree_Pine_01_Duplicate.fbx` was a stale re-import of the same
source at Unity's default settings, while `Tree_Pine_01.fbx` carries the tuned
import the eight live scatter prototypes use. Its only two consumers,
`Tree_Pine_01.prefab` and the `ScatterAssets` test scene, were repointed at the
tuned FBX and the duplicate was deleted. Both FBXs exposed the same mesh under the
same `fileID` with the same triangle count and bounds, so the swap was a GUID
change only.

Not imported: no pack scripts, shaders, demo scenes, or presets.
