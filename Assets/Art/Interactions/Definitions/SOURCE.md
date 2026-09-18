# Interaction definitions

All forty-six assets here are ours. Nothing came from a pack.

Each `.asset` is one `ActorInteractionDefinition` — what the interaction is called,
which contacts and phases it uses, what it needs and what it yields. The
interaction system loads them by name; our editor authors write them.

The two `.mat` files are review-scene materials that a handful of these definitions
reference directly.

These are settings, not art. They live under `Art/` because the interaction system
treats a definition and the clips and props it names as one authored set.
