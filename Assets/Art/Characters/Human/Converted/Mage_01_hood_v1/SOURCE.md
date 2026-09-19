# Mage outfit conversion, revision v1

Ours. This folder is the output of `HumanOutfitConverter`, not an import.

`Conversion.json` is the machine record: the source part, the revision, the input
and output hashes, whether separate headgear or a cloth review was needed, and the
paths of both prefabs. Read it rather than trusting this file for the details.

`*_Original.prefab` is the outfit as it arrives. `*_Fit.prefab` is the same
outfit fitted onto our modular skeleton. `*_LandmarkFit.asset` holds the landmark
correspondences the converter solved.

The underlying outfit mesh comes from a source character pack; the modular
body it is fitted to is documented in `../../SOURCE.md`.

Our filenames drop the source prefix. `SM_Chr_Mage_01` is the sub-mesh we look up inside
the pack; `Mage_01` is what we call the result. `HumanOutfitConverter.RoleName` is
the one place that mapping lives.
