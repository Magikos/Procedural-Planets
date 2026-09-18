# Animal sounds

These sound files come from Bryan's owned asset library. No vendor scripts or plugins were imported.

| Local file | Source under `D:/Unity/Explore Assets/Assets/` |
|---|---|
| DeerCall.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Deer.ogg |
| WolfHowl.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Wolf_Howl.ogg |
| BearCall.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Bear_Calm.ogg |
| BearGrowl.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Bear_Growl.ogg |
| BearAttack.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Bear_Growl_2.ogg |
| Grazing.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Eating_Grass.ogg |
| SeagullCall1.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Seagull.ogg |
| SeagullCall2.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Seagull_2.ogg |
| GoatBleat1.wav | Farm Animal Sounds/Animals/Goat/Goat 03.wav |
| GoatBleat2.wav | Farm Animal Sounds/Animals/Goat/Goat 07.wav |
| BoarSnort1.wav | Farm Animal Sounds/Animals/Pig/Pig Snort 03.wav |
| BoarSnort2.wav | Farm Animal Sounds/Animals/Pig/Pig Snort 06.wav |
| BoarFeeding.wav | Farm Animal Sounds/Animals/Pig/Pig Eating 01.wav |
| SnakeRattle1.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Snake_Rattle.ogg |
| SnakeRattle2.ogg | polyperfect/Low Poly Animated Animals/Sounds/SFX_Snake_Rattle_2.ogg |

Wolf alerts reuse `Assets/AssetPacks/PolyperfectAnimals/Wolf/WarningBark.wav`.
That clip was previously copied from `Malbers Animations/Animal Controller/Wolf Lite/Audio/Wolf Bark.wav`.

Rabbit audio uses quiet grazing only. The searched catalog contains no rabbit vocal recordings.
The 2026-09-07 search found no identified fox or wild boar recordings in the owned library.
The boar profile uses short domestic pig snorts as restrained presentation source material, following Bryan's requested expansion.
These are not recordings of wild boars. No pig squeals were imported.
The selected snorts last 0.50 and 0.38 seconds. The profile uses low volume and long intervals.
The goat profile uses adult bleats lasting 1.09 and 0.82 seconds; no baby cries were imported.
`SnakeHiss.wav` is an original synthetic warning sound, not an animal recording.
Offline generation uses deterministic noise (seed 20260907), a broad 1.8–9 kHz filter band, and smooth attack/release envelopes.
The result is a 0.72-second mono 44.1 kHz PCM16 file with a peak of approximately -9.9 dBFS.
The snake profile uses this hiss only for alerts. No runtime synthesis or audio plugin is required.
The imported rattles remain optional source assets. No current profile assigns them because the snake's rattle anatomy is unconfirmed.
The filename search found no identified fox recordings. The fox profile remains unassigned.
Deer call audio currently also serves as its alert cue. Dedicated alarm recordings remain a content improvement.
The deer walking clip remains outside the project until footfall markers can synchronize playback with each gait.
Eagle and vulture recordings were not identified in the owned sound library. These birds remain silent.
The owned library has sparrow calls, but its identified songbird model is a Quirky Series cardinal.
That art style was previously rejected. Sparrow calls were not assigned to the current eagle models.

Imported clips use mono spatial playback and compressed memory. The pool limits animal audio to eight simultaneous voices.
Calls and alerts are presentation. The authority simulation controls hearing signals independently of the player's audio distance.
