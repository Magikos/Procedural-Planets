# Physical Character, Equipment, and Animation System

Status: user-supplied design direction, recorded 2026-09-16. Implementation remains open.
Current milestone: Physical Equipment & Gear Transfer, before bow and dodge.
The requirements below preserve Bryan's supplied specification. Implementation notes follow the specification.

## Design Goal

Characters should feel like **physical beings inhabiting and interacting with the world**, rather than animated models moving through it.

Animation should provide authored intent and character, but the final motion should be influenced by the character's skill, environment, physical contacts, equipment, forces, balance, and current state.

The long-term system should therefore not be designed around a simple **animation vs. ragdoll** distinction.

Instead, character motion should exist on a continuum:

**Authored Animation → Procedural Adaptation → Physical Reaction → Ragdoll**

Different parts of the character may operate at different points on this continuum simultaneously.

---

# 1. Core Principle: Intent Is Authoritative, Not the Animation Clip

An animation clip should not be the authoritative description of what the character is doing.

The higher-level state should describe the character's intent and circumstances.

Conceptually:

```text
Actor Intent
    +
Physical State
    +
Equipment State
    +
Skill / Capability
    +
Environment
    +
Contacts / External Forces
        ↓
Character Motion System
        ↓
Authored Animation
+ Procedural Animation
+ IK / Contact Constraints
+ Physical Animation
+ Secondary Physics
+ Ragdoll
```

Examples of intent include:

- Run toward a location.
- Draw a sword.
- Block an incoming attack.
- Swing at a target.
- Pick up an axe.
- Climb onto a ledge.
- Carry a log.
- Reach for an object.

The animation system is responsible for turning that intent into believable physical movement.

---

# 2. Four Layers of Character Motion

## Layer 1 — Authored Animation

Authored animations establish recognizable actions, timing, personality, and high-quality baseline motion.

Examples:

- Idle
- Walk
- Run
- Sword attack
- Block
- Dodge
- Climb
- Draw/stow weapon
- Fishing
- Tool use

These animations remain important.

Procedural animation should augment authored animation rather than attempt to replace good authored motion unnecessarily.

---

## Layer 2 — Procedural Animation and IK

Procedural systems adapt authored motion to the actual situation.

Examples include:

- Foot placement on uneven terrain.
- Pelvis adjustment.
- Stride adaptation.
- Hand placement.
- Weapon grip alignment.
- Reaching toward objects.
- Looking and aiming.
- Climbing contact points.
- Matching hands to tools.
- Adjusting posture to slopes.
- Corrective balance steps.

The world should influence the final pose.

A foot should land on the actual rock beneath it rather than pass through the rock because that is where the animation expected the ground to be.

Likewise, a hand drawing a sword should find the actual sword handle rather than merely approximate its location.

---

# 3. Skill Should Be Visible Through Motion

Character skill should eventually affect **how actions physically look**, rather than being represented only through numerical modifiers.

For example, two characters may perform the same basic sword attack but execute it differently.

A novice may:

- Overcommit.
- Over-rotate.
- Recover slowly.
- Lose guard position.
- Take unnecessary corrective steps.
- Have less precise weapon control.
- Display less stable foot placement.
- React more slowly.
- Use larger, less economical movements.

An expert may:

- Remain balanced.
- Use economical motion.
- Maintain guard.
- Recover quickly.
- Transition smoothly between actions.
- Control the weapon accurately.
- Maintain stable foot placement.
- React efficiently.

This does **not** mean every skill difference should be generated procedurally from one animation.

The intended approach is:

**Authored animation establishes the action and character; procedural systems provide adaptation and controlled variation.**

Major stylistic or competency differences may still warrant separate authored animation sets.

---

# 4. Physical Equipment Is a Hard Design Requirement

Important gear should maintain continuous physical existence.

Weapons and tools should not simply appear or disappear when equipped.

At any meaningful moment, an important physical item should be:

- In a character's hand.
- Stowed somewhere visible on the character.
- On the ground.
- On a weapon/tool rack.
- On a table.
- In another valid physical location.

For example:

```text
Sword:
World → Hip/Back → Hand → Hip/Back → World

Shield:
World → Back → Arm → Back → World

Bow:
World → Shoulder/Back → Hand → Shoulder/Back → World
```

The item should remain conceptually the same physical object throughout these transitions.

---

# 5. Equipment State Should Describe Physical Location

Avoid reducing equipment state to only:

```text
equipped = true
```

The system should be capable of representing concepts such as:

```text
Item: Sword #184

Owner: Actor

State:
    World
    Carried
    Equipped

Location:
    WeaponRack
    Ground
    RightHip
    Back
    RightHand

Attachment:
    HipScabbardSocket
    BackSocket
    RightPalmSocket
    WorldTransform
```

Drawing or stowing a weapon therefore becomes a **physical transfer between locations/constraints**, not a visibility toggle.

---

# 6. Equip/Stow Should Be Physical Transfer

Example sword draw:

```text
Sword attached to hip/scabbard
        ↓
Hand reaches toward handle
        ↓
IK/contact aligns hand and handle
        ↓
Grip established
        ↓
Control transfers to hand
        ↓
Sword physically leaves scabbard
        ↓
Combat-ready pose
```

Stowing reverses the process.

The attachment/control transfer should occur at an appropriate contact point in the animation.

Eventually these actions should support interruption.

For example, if the character is struck halfway through drawing a sword, the system should be able to represent that the sword has only partially left its stowed state rather than arbitrarily teleporting it into the hand or back into the sheath.

---

# 7. Equipment Should Have Secondary Physical Motion

Carried gear should respond naturally to character movement.

Examples:

- Belt pouches bounce while running.
- A scabbard moves against the hip.
- A shield shifts on the back.
- A backpack reacts to acceleration and landing.
- A bow or strap responds to movement.
- Loose equipment responds when the character is struck.

Prefer **constrained secondary physics** rather than either fully baked animation or unrestricted rigidbody simulation.

Conceptually:

```text
Character motion
      ↓
Attachment point
      ↓
Spring / damping / angular limits
      ↓
Physical secondary motion
```

Physics should be allowed to perturb the intended resting configuration without gaining unlimited control.

This provides natural variation automatically:

- Walking produces subtle movement.
- Running produces stronger movement.
- Jumping causes equipment to rise and settle.
- Impacts produce directional reactions.
- Falling produces larger physical movement.

---

# 8. Objects Can Change Their Physical Control Mode

A physical object does not need to use the same simulation mode at all times.

A sword is a good example.

### Stowed

Attached/constrained to a scabbard or character attachment point with limited secondary movement.

### Being Drawn

Controlled through animation, hand contact, IK, and the stowed constraint during the transfer.

### Held

Primarily controlled by the character's hand and authored/procedural motion.

### Striking

Still controlled by the character, but collision/contact information may feed forces back into the weapon, arms, and body.

### Dropped

Full world rigidbody physics.

The same conceptual sword persists throughout all of these states.

---

# 9. Physical Combat Reactions

The currently accepted generic attack/block/hit animations are a foundation, not the final combat-reaction system.

Future combat reactions should account for:

- Hit location.
- Impact direction.
- Impact force/impulse.
- Character stance.
- Balance.
- Current animation/action.
- Equipment.
- Ground contact.
- Character skill/capability.

Instead of merely:

```text
PlayAnimation("GetHit")
```

the system should eventually receive something conceptually like:

```text
Impact

Location: Left shoulder
Direction: Right + backward
Force: X
Source: Weapon / object
```

The reaction system can then combine authored reaction motion with procedural and physical responses.

---

# 10. Balance and Loss of Control

Physical reactions should form a continuum.

```text
Controlled
    ↓
Reactive
    ↓
Corrective movement
    ↓
Stumble
    ↓
Fall
    ↓
Ragdoll
```

A small impact might cause only upper-body movement.

A stronger impact may require a corrective step.

A larger impact may produce a stumble.

An overwhelming force may cause loss of balance and transition into ragdoll.

Ragdoll should therefore not be treated simply as:

```text
Alive = animation
Dead = ragdoll
```

Ragdoll represents the extreme end of loss of physical control.

---

# 11. Active / Physical Animation

The architecture should leave room for partial or active-ragdoll techniques.

Conceptually:

```text
Authored animation
       ↓
Desired skeletal pose
       ↓
Physical body attempts to follow pose
       ↓
World forces disturb body
       ↓
Balance/recovery systems respond
```

This could eventually allow a character to remain intentionally animated while still reacting physically to collisions and forces.

It does not need to be implemented immediately, but current architecture should avoid assumptions that make this unnecessarily difficult later.

---

# 12. Different Body Parts May Use Different Control Layers

A character does not need to be entirely "animated" or entirely "physical."

During normal running:

```text
Legs       → authored + procedural
Feet       → procedural ground contact
Pelvis     → procedural adjustment
Torso      → authored
Head       → procedural gaze
Sword      → constrained attachment physics
Pouches    → secondary physics
Backpack   → secondary physics
```

During an impact:

```text
Feet       → ground constraints
Legs       → balance system
Torso      → authored + physical reaction
Arm        → physical/procedural reaction
Weapon     → collision/contact response
Head       → procedural reaction
Gear       → secondary physics
```

During complete loss of control:

```text
Body → predominantly ragdoll/physics
```

This layered ownership model should inform the architecture.

---

# 13. Physical Interaction With Terrain

The same philosophy should apply to locomotion.

Characters should eventually react to the actual generated terrain rather than merely playing locomotion clips over it.

Conceptually:

```text
Desired Movement
      ↓
Locomotion Animation
      ↓
Stride Adaptation
      ↓
Ground Prediction
      ↓
Foot Contact / IK
      ↓
Pelvis and Posture Adjustment
      ↓
Balance
      ↓
Movement
```

Examples:

- Uphill movement changes posture and stride.
- Downhill movement changes balance.
- Rocks and steps alter foot placement.
- Heavy loads affect posture.
- Carrying awkward objects affects balance.
- Slippery surfaces may affect traction.
- Strong impacts may require corrective steps.

The procedural planet should therefore physically influence character motion.

---

# 14. Physical Inventory Philosophy

For significant equipment and resources, consider adopting the broader rule:

> **Important physical objects should not teleport.**

Examples:

- Weapons.
- Shields.
- Bows.
- Tools.
- Backpacks.
- Large harvested resources.
- Carried containers.
- Fishing equipment.
- Objects being handed between characters.

This does not necessarily need to apply to every coin, berry, or tiny inventory object.

Objects should be classified according to whether persistent physical representation materially improves gameplay and immersion.

---

# 15. Visible Loadouts

A desirable consequence of physical equipment is that another character's loadout can be visually understood.

Looking at a character should communicate meaningful information.

For example:

```text
Sword → hip
Shield → back
Bow → shoulder
Pouches → belt
Backpack → back
```

This can eventually influence carrying capacity and equipment compatibility.

Rather than allowing unlimited invisible weapon storage, physical attachment availability may constrain what can reasonably be carried.

The exact inventory restrictions remain a separate gameplay decision, but the architecture should support visible physical loadouts.

---

# 16. Existing Work

Do not discard the currently implemented authored animation work.

Existing locomotion, interaction, fishing, sword attack, shield block, and hit-reaction work should serve as the **authored foundation layer**.

Future procedural and physical systems should augment these assets.

The approved sword-and-shield work should remain valid while later systems add:

- Physical hit reactions.
- IK.
- Contact response.
- Balance.
- Skill variation.
- Secondary equipment physics.
- Physical equip/stow transitions.

---

# 17. Near-Term Priority

The next character-system milestone should be treated as:

## Physical Equipment & Gear Transfer

rather than merely:

## Equip/Stow Animations

Initial scope should establish the architectural foundation for:

1. Persistent physical gear.
2. Character attachment/stow points.
3. Held attachment points.
4. World/drop state.
5. Physical transfer between stowed and held states.
6. Sword draw/stow.
7. Shield equip/stow.
8. Appropriate constrained secondary motion where practical.
9. Pickup/drop/placement architecture.
10. Future extension to bows, tools, packs, and other equipment.

The first implementation does not need to solve the complete procedural/physical animation system.

It **should**, however, establish interfaces and state ownership that allow these systems to be added later without replacing the equipment architecture.

---

# Guiding Principle

The desired experience is:

> **The character and their possessions should feel physically present in the world.**

Animation communicates intention.

Procedural systems adapt that intention to circumstances.

IK establishes believable contact.

Physics provides weight, inertia, impact, and secondary motion.

Skill changes how effectively the character controls their body.

Ragdoll represents significant or complete loss of that control.

Equipment remains physically represented throughout.

The goal is not maximum physics simulation for its own sake. The goal is a coherent illusion that characters, equipment, terrain, and forces all belong to the **same physical world**.

---

## Implementation notes — 2026-09-16

### Existing code and boundaries

- `InventoryService` stores item-type counts. It does not currently represent unique physical gear or attachment ownership.
- `HeldToolGrip` supplies prop-local palm anchors and preserves authored hand rotation.
- `SidekickInteractionReview` already handles pickup, carrying, placement cancellation, and rigidbody control changes. Inspect and reuse this behavior before introducing another transfer implementation.
- `ActorInteractionSession`, `ActorPerformancePlayback`, and `ProceduralPoseRig` provide shared timing, transitions, and contact correction.
- Approved MELEE-01 captures remain the sword/shield motion baseline. The equipment milestone must preserve them as comparisons.

Keep authoritative item identity and location separate from the mechanism that presents its pose.
An animation marker requests a transfer; validated ownership and contact state authorize it.
Treat actor ownership, physical support location, and active motion control as distinct facts.
One item can remain actor-owned while a sheath still constrains a partial draw.
Only one coordinator may write an item's final transform at a time.
Do not let hand following, attachment springs, and rigidbody simulation write competing results.
Reuse the project's actor identity and persistence conventions before choosing new identifier types.
Do not use a Unity instance ID as durable item identity.

### First implementation sequence

1. Establish unique physical sword/shield instances and validated world, stowed, held, and transfer state.
2. Define compatible body slots and grip anchors as editable data. Check the sword, shield, and future bow together for clearance.
3. Select suitable authored draw/stow clips from owned assets. Fit the reach to the actual prop before transferring control.
4. Preserve the displayed pose and transfer progress through interruption. Retain partial extraction where applicable; do not force an endpoint pose.
5. Connect pickup, placement, and drop through shared ownership. Transfer appropriate linear/angular motion when releasing to physics.
6. Add restrained secondary motion to stowed gear where it can be verified. Use the project's gravity frame rather than assuming global down.
7. Capture complete sword/shield sequences before extending the same system to bows and tools.

This sequence establishes real boundaries through working cases. It does not require speculative implementations of future balance or active-ragdoll systems.
Persistent identity across physical transfers is required now. Save/load and replication must use the project's established contracts when integrated; neither is yet implemented here.

### Initial review gates

- The same identified item remains visibly present throughout rack/ground, carried, and held states.
- Source and destination cannot both claim exclusive control, and two items cannot silently occupy one exclusive slot.
- Draw/stow establishes visible hand contact before ownership changes. Transfers preserve pose continuity.
- Interrupt before grip, during extraction, after grip, and during stow. Check no duplication, disappearance, or endpoint teleport.
- Pickup/drop/placement retains the item, its pose, and valid collision behavior. Reject blocked placement or unreachable contacts without extreme IK.
- Walking, turning, and landing keep stowed gear bounded and clear of the actor and other gear.
- Compare original source, production playback without corrections, and final runtime motion. Keep the review queue to three complete actions.

Physical impact reactions, corrective balance, partial active ragdoll, broad skill variants, and terrain prediction remain later milestones.
Exact inventory capacity restrictions and physical representation of small stackable items remain separate gameplay decisions.
