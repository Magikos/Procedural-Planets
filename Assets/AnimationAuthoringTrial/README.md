# Very Animation authoring trial

Open `Chest Animation Editing.unity` in Edit Mode.
Select `EDIT THIS - Chest Trial Character`.
Open Window > Animation > Animation, then Window > Very Animation > Main.
Choose `Chest Inspection Idle Trial` and press Edit Animation if editing is not active.
The first trial uses a small head turn near 1 second. Save the clip after editing.
The native preview omits production IK. Hand placement here does not certify runtime contact.

The clip is an independent copy. The original chest idle and approved review scene remain the baseline.
No generator owns this trial clip. Do not run Apply Authored Chest Motion in this scene; that tool edits production assets.
`Chest Trial Preview.controller` exists only to expose the clip to the animation editor.
The copied review components are disabled. Do not use Play Mode for this editing scene.
The copied Chest/CollectChest definitions reference the trial clip for a later isolated runtime comparison.
The chest lid is open for posing; the production scene still contains its original closed state.

Verified 2026-09-15: Very Animation 1.4.1 loads and samples this Humanoid clip in Unity 6000.7.0a6.
The tool flags Unity 6000.7 as unsupported. Save/reopen, manual-edit preservation, and runtime integration remain trial checks.
