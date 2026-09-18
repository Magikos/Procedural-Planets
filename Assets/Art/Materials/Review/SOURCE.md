Materials and settings for the animation review scenes. These are ours.

They exist only to make review captures readable: a ground plane, an obstacle,
a water plane, a panel, and two coloured targets that mark where a hand or the
gaze is asked to go. Nothing here ships in a built planet.

ReviewGround.asset is the settings asset the review authors load and, when it is
absent, create. The .mat files use project shaders; no vendor material, shader,
or preset was imported.

The review scenes and the authors that build them are in Assets/Editor
(HumanoidReviewAuthor and the *ReviewAuthor family).
