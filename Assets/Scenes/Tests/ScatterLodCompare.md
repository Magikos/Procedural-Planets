# Scatter LOD verification

Open ScatterLodCompare.unity in this folder and enter Play mode. It loads the runtime library, including generated variants.

- Previous / Next or Left / Right: select a prototype. Shift moves ten entries.
- Find: select the next name containing the search text.
- Up / Down: choose a mesh tier, billboard, or production AUTO drawing.
- Compare: show LOD0 beside the selected tier.
- Blink (B): alternate LOD0 and the selected tier at the same position twice per second.
- Walk transition (T): move through 65?120% of the billboard handover distance using production LOD drawing.
- Orbit or the angle slider: inspect other viewing directions.
- 1?4: hold the object at 36, 48, 96, or 192 pixels.
- Plus / Minus: magnify the pixels without changing distance or mip level.
- Capture all (F): write comparison strips, CSV measurements, and a ranked report.

Select CARD with Up / Down before blinking to compare the billboard. Flat prototypes without a billboard may have an empty forced CARD view; AUTO follows their real mesh cull.

The ScatterLodSweep component exposes OutputFolder, CaptureYaw, and HandoverPixels. Use a separate output folder for each angle. A capture strip places LOD0 first, followed by other mesh tiers and the billboard when available.

Measurements flag silhouette, coverage, brightness, and colour differences. They are a review queue, not visual approval. Thin objects can trigger empty-coverage flags. An intentionally absent billboard can trigger NO_CARD.

The batch sweep holds each object at a fixed pixel size. Walk transition instead uses the production distance boundary. Use both: static agreement alone does not prove smooth movement. This scene isolates assets under showcase lighting; planet weather, shadows, and terrain still require in-world checks.
