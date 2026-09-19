# Modular body parts

Source: D:/Unity/Explore Assets/Assets/Synty/SidekickCharacters/
Pack: Synty Sidekick Characters

Twenty-two meshes: head, nose, teeth, tongue, eyebrows, eyes, ears, torso, upper
and lower arms, hands, hips, legs and feet. Together they are the modular human
body the kit assembles.

Renamed from the vendor's `SK_HUMN_BASE_01_<NN><CODE>_HU01` to `<NN>_<Role>`. Each
`.meta` moved with its file, so the GUIDs and every serialized reference survived.
The two-digit ordinal is load-bearing, not decoration: `HumanBodyReviewAuthor`
assembles the body from `Directory.GetFiles(...).OrderBy(p => p)`, so filename sort
order is assembly order. The `SK_HUMN_*` names inside each FBX are sub-mesh names
and stay as the source wrote them.

They were copied in by hand, which is why they carry no `AssetOrigin` metadata.

Not imported: no vendor scripts, shaders, creator tools, or character-builder UI.
