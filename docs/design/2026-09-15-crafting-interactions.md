# Crafting and cooking interaction slice

Status: implemented in the review scene; visual approval pending. This does not close the broader animation audit.

## Scope

`Assets/Scenes/Tests/CraftingInteractionReview.unity` reuses the existing alchemy bench and cooking fire. It adds two sample recipes: one Wildflowers becomes one Herbal powder; one Raw meat becomes one Cooked meat. These quantities are review data, not final balance.

`CraftingRecipe` stores editable costs and results. `InventoryService.TryExchange` validates all entries and totals before mutation. Missing ingredients, duplicate costs exceeding stock, invalid entries, and result overflow reject the entire exchange. Preview does not reserve or consume resources. Completion checks inventory again.

`SidekickInteractionReview.StationAction.Recipe` binds a recipe to the existing action. `CraftComplete` commits once per session. The marker follows the work phase. New definitions use a finite work phase followed by authored recovery. Repeating the interaction starts another independent transaction. Cancellation before completion consumes nothing. Cancellation after completion preserves the completed exchange.

The review fixture supplies local stock. Production must inject the player's inventory through `Review.Inventory`; this slice does not wire the planet player's inventory or persistence. Campfire build/light still use their existing state markers without recipe costs.

## Animation and prop authoring

The two definitions are `Grind wildflowers.asset` and `Cook meat.asset`. Their clips are editable `.anim` copies. No new per-animation C# authoring generator was added.

Sources under `Assets/Art/Interactions/Animations/`:

- `Survival_Build_Crafting_MortarAndPestle_Enter.FBX`
- `Survival_Build_Crafting_MortarAndPestle_Loop.FBX`
- `Survival_Build_Crafting_MortarAndPestle_Exit.FBX`
- `Survival_CampFire_KneelDown_Cooking_Meat.FBX`

Each copy uses the source filename plus ` editable.anim`. Source timing and limb curves remain intact. Grinding keeps the full enter/work/exit clips. Roasting retains the existing quarter/half/quarter source division at its authored rate.

Both tools reuse `HeldToolGrip`. The right palm owns the pestle; the left palm owns the roasting stick. The initial roasting fixture followed the wrong hand. Anchor rotation fits the authored hand without another IK writer. The mortar and table now fit the authored grinding reach. The bowl mesh comes from owned `Toon Enchanted Meadow/Models/TEM_Bowl_01A.fbx`, copied with its metadata; the fixture reuses its existing material.

TakeTool and ReturnTool mark ownership. Cancellation while holding the tool selects the existing authored recovery through ReturnPhase. Tool transfer still uses the existing blend; it is not a completed pickup/stow animation.

## Evidence

Bundle: `local-only/animation-review/crafting-2026-09-15/index.html`.

- Current: grind-final and roast-final, complete actions from side and rear.
- Cancellation: grind-cancel and roast-cancel, with ingredient/result counts per frame.
- Before prop fitting: grind-v1 and roast-v1. Transactions already existed in these first-pass captures.
- Source diagnostics: grind-source and roast-source. Original FBX on verified original rig at 1x appears beside raw editable playback on the production actor without corrections.

The raw retargeted diagnostics do not reproduce production transition blends. A full production-B comparison remains missing. Source capture fixes the Animator root and retains baked body motion. Grinding source clips run consecutively without runtime crossfades.

I inspected full-action contact sheets and consecutive frames around work and cancellation. This inspection is not user approval. Settled primary palm-to-tool-anchor distance measured zero in the repeated grinding check. This does not prove finger wrapping or pestle-to-bowl contact throughout every frame.

71 focused EditMode tests passed: CraftingRecipeTests, SidekickInteractionReviewTests, InteractionPoseBlendTests. Final job: `50277ef8f3e8431d870a1f0e6cb8ff4c`. Earlier test setup failed with `System.ArgumentException : A nonempty key is required.` and then `System.ArgumentException : Performance phase requires a clip.` The fixture now supplies both; the final run passed.

Runtime checks:

- Each complete capture consumes one ingredient, grants one result, and returns to idle.
- Both cancellation captures consume zero and grant zero, then return to idle.
- Missing ingredients reject before starting.
- An unlit fire rejects cooking.
- Target loss before completion grants nothing.
- Ingredients removed during work reject at completion.
- Two grinding actions consume two ingredients and produce two results. A third action rejects with empty stock.

## Remaining work

Tool pickup/stow, ingredient placement, visible result transport, final food/tool models, food colour change, finger contact, and production inventory adoption remain open. Roasting contains the source inspection/tasting gesture. Smithing, pouring, stirring, campfire costs, and other recipes are unreviewed by this slice. Traversal, rope, fishing, combat, and skill variants retain their existing open coverage.

## User approval

Bryan approved both current actions on 2026-09-15. This supersedes their pending visual status only. Remaining work stays open.
