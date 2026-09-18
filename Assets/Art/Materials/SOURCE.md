# Shared scatter and prop materials

Every material here is ours. Nothing in this folder came from a pack.

Seventeen use our `Scatter/FoliageLit` shader, two use `Scatter/VertexColorLit`,
and two use URP Lit. They bind the textures that live beside their meshes in
`../Vegetation/*` and `../Props/*`, so a material and its texture are one edit
apart without either folder holding a fraction of a set.

`FoliagePine.mat` and `ScatterProps.mat` bind the two differently-imported copies
of the generic atlas; `../Props/Generic/SOURCE.md` explains why both copies exist.

`Ground/` holds the terrain materials and has its own record. `Review/` holds the
capture-scene materials and has its own record.
