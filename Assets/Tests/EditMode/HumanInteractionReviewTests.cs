using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class HumanInteractionReviewTests
    {
        [Test]
        public void ChestInspectionLoopsAnIdleWithoutRepeatingCollection()
        {
            var author = System.Type.GetType("HumanChestMotionAuthor, ProceduralPlanets.Editor", true);
            foreach (string action in new[] { "Chest", "CollectChest" })
            {
                var phases = (ActorInteractionDefinition.Phase[])author.GetMethod("CreatePhases").Invoke(null, new object[] { action });
                var look = System.Array.Find(phases, phase => phase.Animation.Name == "Look inside");
                Assert.IsTrue(look.WaitForInput);
                Assert.That(look.ContactSet, Is.EqualTo("InspectionRim"));
                Assert.That(look.RightHandWeight, Is.EqualTo(1f));
                Assert.That(look.LeftHandWeight, Is.EqualTo(1f));
                Assert.IsTrue(look.Animation.Loop);
                Assert.IsEmpty(look.Marker);
                Assert.Greater(look.Animation.Clip.length, 1f);
                Assert.That(look.Animation.EndNormalized - look.Animation.StartNormalized, Is.EqualTo(1f));
                Assert.AreEqual(action == "CollectChest" ? 1 : 0, System.Array.FindAll(phases, phase => phase.Marker == "Collect").Length);
            }
        }

        [TestCase("OpenDoor")]
        [TestCase("CloseDoor")]
        public void DoorReachPreservesTheCompleteSourceClockAndReleasesBeforeItsRecovery(string action)
        {
            var author = System.Type.GetType("HumanDoorMotionAuthor, ProceduralPlanets.Editor", true);
            var phases = (ActorInteractionDefinition.Phase[])author.GetMethod("CreatePhases").Invoke(null, new object[] { action });
            float previous = 0f, seconds = 0f;
            var clip = phases[0].Animation.Clip;
            foreach (var phase in phases)
            {
                Assert.That(phase.Animation.Clip, Is.SameAs(clip));
                Assert.That(phase.Animation.StartNormalized, Is.EqualTo(previous).Within(.00001f));
                float span = phase.Animation.EndNormalized - phase.Animation.StartNormalized;
                Assert.That(phase.Seconds, Is.EqualTo(span * clip.length).Within(.00001f));
                Assert.That(phase.ForwardLeanDegrees, Is.Zero);
                Assert.That(phase.GripRadius, Is.Zero);
                previous = phase.Animation.EndNormalized;
                seconds += phase.Seconds;
            }
            Assert.That(previous, Is.EqualTo(1f));
            Assert.That(seconds, Is.EqualTo(clip.length).Within(.00001f));
            Assert.That(phases[1].Marker, Is.EqualTo(action == "OpenDoor" ? "Open" : "Close"));
            Assert.That(phases[1].Animation.StartNormalized, Is.GreaterThanOrEqualTo(.36f));
            Assert.That(phases[1].Animation.EndNormalized, Is.LessThanOrEqualTo(.44f));
            float markerSource = Mathf.Lerp(phases[1].Animation.StartNormalized, phases[1].Animation.EndNormalized, phases[1].MarkerProgress);
            Assert.That(markerSource, Is.EqualTo(.404f).Within(.00001f));
            Assert.That(phases[^1].RightHandWeight, Is.Zero);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void DoorSurfaceProjectionKeepsPalmHeightAndChoosesTheFacingPanel(bool rear)
        {
            var door = new GameObject("Door surface");
            try
            {
                var target = new HumanInteractionReview.Target { Hinge = door.transform, Contact = door.transform,
                    LidVertices = new[] { new Vector3(-1,0,0), new Vector3(1,0,0), new Vector3(1,2,0), new Vector3(-1,2,0),
                        new Vector3(-1,0,.1f), new Vector3(1,0,.1f), new Vector3(1,2,.1f), new Vector3(-1,2,.1f) },
                    LidTriangles = new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7 } };
                var origin = new Vector3(-.32f, 1.24f, rear ? 1f : -1f);
                Vector3 direction = rear ? Vector3.back : Vector3.forward;
                Assert.IsTrue(HumanInteractionReview.TryProjectContactSurface(target, origin, direction, out var point, out var normal));
                Assert.That(point.y, Is.EqualTo(1.24f).Within(.00001f));
                Assert.That(point.x, Is.EqualTo(-.32f).Within(.00001f));
                Assert.That(point.z, Is.EqualTo(rear ? .1f : 0f).Within(.00001f));
                Assert.That(Vector3.Dot(normal, direction), Is.LessThan(-.99f));
                Assert.IsFalse(HumanInteractionReview.TryProjectContactSurface(target, origin + Vector3.up * 2f, direction, out _, out _));
            }
            finally { Object.DestroyImmediate(door); }
        }
        [Test]
        public void DoorContactHeightChangeCancelsBeforeTheOpeningMarker()
        {
            var root = new GameObject("Door height fixture");
            var door = new GameObject("Door");
            var contact = new GameObject("Palm contact");
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
            var clip = new AnimationClip();
            try
            {
                contact.transform.SetParent(door.transform);
                contact.transform.position = new Vector3(-.08f, 1.24f, .572f);
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                definition.Action = "Door";
                definition.Phases = new[] { new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Palm contact", Clip = clip, BlendSeconds = .15f }, Seconds = 1f, Marker = "Open", MarkerProgress = .5f } };
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = contact.transform, Hinge = door.transform,
                    OpenAwayFromActor = true, HasAuthoredContact = true, AuthoredContactOffset = contact.transform.position,
                    Use = definition, OpenEuler = new Vector3(0,-90,0) } };
                int markers = 0; review.Session.Marker += _ => markers++;
                Assert.IsTrue(review.Interact());
                contact.transform.position += Vector3.up;
                review.Tick(.75f);
                Assert.IsFalse(review.Session.Active);
                Assert.That(markers, Is.Zero);
                Assert.That(Quaternion.Angle(door.transform.rotation, Quaternion.identity), Is.Zero);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(door); Object.DestroyImmediate(definition); Object.DestroyImmediate(clip); }
        }
        [Test]
        public void LidContactSlidesOnRotatedMeshAndStopsAtItsEdge()
        {
            var lid = new GameObject("Contact surface");
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }, triangles = new[] { 0, 2, 1 } };
            try
            {
                lid.AddComponent<MeshFilter>().sharedMesh = mesh;
                lid.transform.SetPositionAndRotation(new Vector3(2f, 3f, -4f), Quaternion.Euler(-53f, 24f, 0f));
                var target = new HumanInteractionReview.Target { Hinge = lid.transform, Contact = lid.transform };
                var inside = lid.transform.TransformPoint(new Vector3(.2f, .3f, .4f));
                var expected = lid.transform.TransformPoint(new Vector3(.2f, 0f, .4f));
                Assert.That(Vector3.Distance(HumanInteractionReview.ClosestLidPoint(target, inside), expected), Is.LessThan(.0001f));
                var outside = lid.transform.TransformPoint(new Vector3(1f, .3f, 1f));
                expected = lid.transform.TransformPoint(new Vector3(.5f, 0f, .5f));
                Assert.That(Vector3.Distance(HumanInteractionReview.ClosestLidPoint(target, outside), expected), Is.LessThan(.0001f));
            }
            finally { Object.DestroyImmediate(lid); Object.DestroyImmediate(mesh); }
        }

        [TestCase(-85f)]
        [TestCase(85f)]
        public void AuthoredHingeFollowsAcceleratingLiftAndItsReverse(float openAngle)
        {
            var closed = Quaternion.Euler(12f, 25f, -8f);
            var lever = new Vector3(.03f, .1f, -.55f);
            float[] fractions = { 0f, .03f, .12f, .46f, .83f, 1f, .83f, .46f, .12f, .03f, 0f };
            foreach (float fraction in fractions)
            {
                var motion = Quaternion.Euler(openAngle * fraction, 0f, 0f);
                var actual = HumanInteractionReview.FitAuthoredHinge(closed, new Vector3(openAngle, 0f, 0f), lever, motion * lever);
                Assert.That(Quaternion.Angle(actual, closed * motion), Is.LessThan(.02f));
            }
        }

        [Test]
        public void ExcessiveAuthoredContactReleasesThroughTheExistingBlend()
        {
            const float allowance = .12f;
            Assert.That(HumanInteractionReview.AuthoredContactWeight(.04f, allowance), Is.EqualTo(1f));
            Assert.That(HumanInteractionReview.AuthoredContactWeight(.09f, allowance), Is.EqualTo(.5f).Within(.0001f));
            Assert.That(HumanInteractionReview.AuthoredContactWeight(.6f, allowance), Is.Zero);
            var blend = new InteractionPoseBlend();
            var target = new InteractionPoseTarget(Vector3.forward, null, 1f, useContact: true);
            for (int i = 0; i < 30; i++) blend.Tick(target, 1f / 60f);
            var release = blend.Tick(new InteractionPoseTarget(Vector3.forward, null,
                HumanInteractionReview.AuthoredContactWeight(.6f, allowance), useContact: true), 1f / 60f);
            Assert.That(release.HasValue, Is.True);
            Assert.That(release.Value.Weight, Is.EqualTo(.95f).Within(.0001f));
            var interrupted = blend.Tick(target, 1f / 60f);
            Assert.That(interrupted.Value.Weight, Is.GreaterThan(release.Value.Weight));
            Assert.That(Vector3.Distance(interrupted.Value.Position, release.Value.Position), Is.LessThan(.0001f));
        }

        [Test]
        public void CarryGazeWaitsForIdleAndReleasesWhenWalkingResumes()
        {
            var root = new GameObject("Carry gaze actor"); var item = new GameObject("Held item");
            try
            {
                root.transform.position = new Vector3(300f, 0f, 0f);
                item.transform.position = root.transform.position + Vector3.forward * .6f;
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = item.transform, PickupRoot = item.transform } };
                Assert.IsTrue(review.Interact()); Assert.AreEqual(0, review.Held);
                review.Tick(1f); Assert.IsNull(actor.LookTarget);
                review.Tick(1.1f); Assert.AreSame(item.transform, actor.LookTarget);
                root.transform.position += Vector3.right * .1f;
                review.Tick(.02f); Assert.IsNull(actor.LookTarget);
                review.Tick(1f); Assert.IsNull(actor.LookTarget);
                review.Tick(1.1f); Assert.AreSame(item.transform, actor.LookTarget);
                review.HeldItemLookDelay = 0f; root.transform.position += Vector3.right * .1f;
                review.Tick(.02f); Assert.IsNull(actor.LookTarget);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(item); }
        }

        [TestCase("GroundPickup")]
        [TestCase("TablePickup")]
        public void PortablePickupRecoversBeforeUnlockedCarry(string name)
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/" + name + ".asset");
            var plan = definition.Snapshot();
            Assert.AreEqual("Stand with item", plan[2].Name);
            Assert.IsFalse(plan[2].WaitForInput);
            Assert.IsTrue(plan[3].AllowMovement);
            Assert.IsTrue(plan[3].UseLocomotion);
            Assert.IsTrue(plan[3].WaitForInput);
        }

        [Test]
        public void ShelfCommitsInOrderAndDoesNotConsumeAnUnfinishedSlot()
        {
            var root = new GameObject("Shelf test"); var book = new GameObject("Book");
            try
            {
                var shelf = root.AddComponent<InteractionShelf>();
                shelf.Slots = new[] { root.transform, root.transform };
                Assert.AreEqual(0, shelf.NextSlot()); Assert.AreEqual(0, shelf.NextSlot());
                Assert.IsTrue(shelf.Place(0, book.transform)); Assert.IsFalse(shelf.Place(0, book.transform));
                Assert.AreEqual(1, shelf.NextSlot()); Assert.IsTrue(shelf.Place(1, book.transform));
                Assert.AreEqual(-1, shelf.NextSlot());
                shelf.ResetSlots(); Assert.AreEqual(0, shelf.NextSlot());
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(book); }
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void CrateGripsFollowPickupSideWithoutTurningTheProp(float side)
        {
            var actorRoot = new GameObject("Pickup actor");
            var crate = new GameObject("Pickup crate");
            try
            {
                crate.transform.SetPositionAndRotation(new Vector3(200f, 0f, 0f), Quaternion.Euler(0f, 37f, 0f));
                var right = new GameObject("Right grip").transform; right.SetParent(crate.transform, false);
                var left = new GameObject("Left grip").transform; left.SetParent(crate.transform, false);
                right.localPosition = new Vector3(.30f, .16f, -.08f); left.localPosition = new Vector3(-.30f, .16f, -.08f);
                Quaternion facing = crate.transform.rotation * Quaternion.Euler(0f, side, 0f);
                actorRoot.transform.SetPositionAndRotation(crate.transform.position - facing * Vector3.forward * .8f, facing);
                var actor = actorRoot.AddComponent<HumanoidAnimationPrototype>();
                var review = actorRoot.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = right, LeftContact = left,
                    PickupRoot = crate.transform, SelectionAnchor = crate.transform, FourSidedPickup = true,
                    Use = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>("Assets/Art/Interactions/Definitions/TwoHandCarry.asset") } };
                Quaternion original = crate.transform.rotation;
                review.RefreshTarget(); Assert.AreEqual(0, review.Selected);
                Assert.IsTrue(review.Interact());
                Vector3 middle = (right.position + left.position) * .5f - crate.transform.position;
                Assert.Greater(Vector3.Dot(Vector3.ProjectOnPlane(middle, Vector3.up).normalized, -actorRoot.transform.forward), .99f);
                Assert.Greater(Vector3.Dot((right.position - left.position).normalized, actorRoot.transform.right), .99f);
                Assert.Less(Quaternion.Angle(original, crate.transform.rotation), .001f);
            }
            finally { Object.DestroyImmediate(actorRoot); Object.DestroyImmediate(crate); }
        }

        [Test]
        public void CancellingHeldFlaskRunsReturnWithoutPouringOrRestartingReturn()
        {
            var root = new GameObject("Flask cancellation fixture");
            var contact = new GameObject("Flask contact");
            try
            {
                root.transform.position = new Vector3(700f, 0f, 0f);
                contact.transform.position = root.transform.position + Vector3.forward * .6f;
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                    "Assets/Art/Interactions/Definitions/Pour reagent.asset");
                var action = new HumanInteractionReview.StationAction { Definition = definition, PickupAtMarker = true, ReturnPhase = "Return flask" };
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = contact.transform, Actions = new[] { action },
                    ContactSets = new[] {
                        new HumanInteractionReview.HandContacts { Id = "FlaskRest", Right = contact.transform },
                        new HumanInteractionReview.HandContacts { Id = "FlaskLift", Right = contact.transform },
                        new HumanInteractionReview.HandContacts { Id = "Pour", Right = contact.transform } } } };
                var markers = new System.Collections.Generic.List<string>(); review.Session.Marker += markers.Add;
                Assert.IsTrue(review.UseStationAction(0));
                review.Session.Advance(.1f); Assert.IsFalse(action.ToolHeld);
                review.Session.Advance(definition.Phases[0].Seconds); Assert.IsTrue(action.ToolHeld);
                review.Cancel(); Assert.AreEqual("Return flask", review.Session.Phase.Name);
                review.Session.Advance(.1f); review.Cancel(); Assert.AreEqual(.1f, review.Session.Elapsed);
                review.Session.Advance(10f);
                CollectionAssert.AreEqual(new[] { "TakeTool", "ReturnTool" }, markers);
                Assert.IsFalse(action.ToolHeld); Assert.IsFalse(review.Session.Active);
                Assert.IsTrue(review.UseStationAction(0));
                review.Session.Advance(definition.Phases[0].Seconds);
                root.transform.position += Vector3.right * 2f;
                review.Tick(.02f);
                Assert.IsFalse(review.Session.Active, "Leaving range must not stall forever in the return phase.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(contact); }
        }

        [Test]
        public void PouringCommitsOnceAndEarlyCancellationDoesNotPour()
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/Pour reagent.asset");
            var session = new ActorInteractionSession();
            int pours = 0; session.Marker += marker => { if (marker == "Pour") pours++; };
            session.Begin(definition.Snapshot()); session.Advance(.1f); session.Cancel(); session.Advance(10f);
            Assert.AreEqual(0, pours);
            session.Begin(definition.Snapshot()); session.Advance(10f); session.Advance(10f);
            Assert.AreEqual(1, pours); Assert.IsFalse(session.Active);
        }

        [Test]
        public void CampfireRequiresConstructionThenIgnitionBeforeCooking()
        {
            var root = new GameObject("Campfire test");
            try
            {
                var fire = root.AddComponent<CampfireInteraction>();
                ActorInteractionDefinition Load(string action) => UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                    "Assets/Art/Interactions/Definitions/" + action + ".asset");
                var build = Load("Build campfire"); var light = Load("Light campfire"); var cook = Load("Stir cooking pot");
                Assert.IsTrue(fire.CanUse(build)); Assert.IsFalse(fire.CanUse(light)); Assert.IsFalse(fire.CanUse(cook));
                fire.ApplyMarker("LightCampfire"); Assert.IsFalse(fire.Lit);
                var session = new ActorInteractionSession(); session.Marker += fire.ApplyMarker;
                session.Begin(build.Snapshot()); session.Advance(.01f); session.Cancel(); session.Advance(100f);
                Assert.IsFalse(fire.Built, "Early cancellation must not build the fire.");
                session.Begin(build.Snapshot()); session.Advance(100f);
                Assert.IsTrue(fire.Built); Assert.IsFalse(fire.CanUse(build)); Assert.IsTrue(fire.CanUse(light));
                session.Begin(light.Snapshot()); session.Advance(.01f); session.Cancel(); session.Advance(100f);
                Assert.IsFalse(fire.Lit, "Early cancellation must not ignite the fire.");
                session.Begin(light.Snapshot()); session.Advance(100f);
                Assert.IsTrue(fire.Lit); Assert.IsFalse(fire.CanUse(light)); Assert.IsTrue(fire.CanUse(cook));
                fire.ResetState(); Assert.IsFalse(fire.Built); Assert.IsFalse(fire.Lit);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CampfireConstructionAndLightBlendTheirVisuals()
        {
            var root = new GameObject("Campfire visual test");
            var wood = new GameObject("Wood");
            try
            {
                var fire = root.AddComponent<CampfireInteraction>(); fire.WoodVisual = wood.transform;
                fire.FireLight = root.AddComponent<Light>(); fire.ResetState();
                fire.ApplyMarker("BuildCampfire"); fire.ApplyMarker("LightCampfire");
                Assert.AreEqual(Vector3.zero, wood.transform.localScale); Assert.AreEqual(0f, fire.FireLight.intensity);
                fire.Tick(.1f);
                Assert.That(wood.transform.localScale.x, Is.InRange(.01f, .99f));
                Assert.That(fire.FireLight.intensity, Is.InRange(.01f, 1.99f));
                fire.Tick(1f); Assert.AreEqual(Vector3.one, wood.transform.localScale); Assert.AreEqual(2f, fire.FireLight.intensity);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(wood); }
        }

        [TestCase("Hammer at anvil")]
        [TestCase("Grind ingredients")]
        [TestCase("Stir cooking pot")]
        [TestCase("Roast meat")]
        public void CraftingActionsLoopUntilFinishedThenExit(string action)
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/" + action + ".asset");
            var session = new ActorInteractionSession(); session.Begin(definition.Snapshot());
            session.Advance(100f);
            Assert.IsTrue(session.Phase.WaitForInput);
            Assert.IsTrue(definition.Phases[session.PhaseIndex].Animation.Loop);
            Assert.IsTrue(session.Continue()); session.Advance(100f);
            Assert.IsFalse(session.Active);
        }

        [Test]
        public void StationActionChoiceRejectsInvalidIndexAndAllowsLoopSwitch()
        {
            var root = new GameObject("Station action fixture"); var contact = new GameObject("Work contact");
            var first = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
            var second = ScriptableObject.CreateInstance<ActorInteractionDefinition>(); var clip = new AnimationClip();
            try
            {
                root.transform.position = Vector3.right * 700f;
                contact.transform.position = root.transform.position + Vector3.forward * .6f;
                first.Action = "First"; second.Action = "Second";
                foreach (var definition in new[] { first, second }) definition.Phases = new[] {
                    new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase {
                        Name = "Work", Clip = clip, BlendSeconds = .2f }, WaitForInput = true } };
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = contact.transform,
                    Actions = new[] { new HumanInteractionReview.StationAction { Definition = first },
                        new HumanInteractionReview.StationAction { Definition = second } } } };
                Assert.IsFalse(review.UseStationAction(-1));
                Assert.IsTrue(review.UseStationAction(0)); Assert.AreEqual("First", review.Session.Plan.Action);
                Assert.IsFalse(review.UseStationAction(2)); Assert.AreEqual("First", review.Session.Plan.Action);
                Assert.IsTrue(review.UseStationAction(1)); Assert.AreEqual("Second", review.Session.Plan.Action);
                review.Cancel(); Assert.IsFalse(review.Session.Active);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(contact);
                Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(clip); }
        }

        [TestCase("SlidingCabinet", "Open")]
        [TestCase("CollectSlidingCabinet", "Collect")]
        public void SlidingContainerWaitsBeforeClosingWithoutExtraCollection(string action, string initialMarker)
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/" + action + ".asset");
            var session = new ActorInteractionSession();
            var markers = new System.Collections.Generic.List<string>(); session.Marker += markers.Add;
            session.Begin(definition.Snapshot()); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { initialMarker }, markers);
            Assert.IsTrue(session.Phase.WaitForInput);
            Assert.IsTrue(session.Continue()); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { initialMarker, "Close" }, markers);
            Assert.IsFalse(session.Active);
        }

        [Test]
        public void ButtonCommitsPressAndReleaseInOrder()
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/PressButton.asset");
            var session = new ActorInteractionSession();
            var markers = new System.Collections.Generic.List<string>(); session.Marker += markers.Add;
            session.Begin(definition.Snapshot()); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { "Open", "Close" }, markers);
        }

        [TestCase("LeverOn", "Open")]
        [TestCase("WheelOpen", "Open")]
        [TestCase("WheelClose", "Close")]
        [TestCase("LeverOff", "Close")]
        [TestCase("TakeEquipment", "Pickup")]
        [TestCase("ReturnEquipment", "Place")]
        public void AdditionalInteractionDefinitionsCommitOneMarker(string action, string marker)
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/" + action + ".asset");
            Assert.IsNotNull(definition);
            var session = new ActorInteractionSession();
            var markers = new System.Collections.Generic.List<string>(); session.Marker += markers.Add;
            session.Begin(definition.Snapshot()); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { marker }, markers);
            session.Cancel(); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { marker }, markers);
        }

        [Test]
        public void RackReturnRejectsDistanceAndCancelsWhenDockMoves()
        {
            var root = new GameObject("Rack fixture");
            var item = new GameObject("Item"); var dock = new GameObject("Dock");
            try
            {
                root.transform.position = Vector3.right * 600f;
                item.transform.position = dock.transform.position = root.transform.position + new Vector3(0, 1, .6f);
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = item.transform,
                    PickupRoot = item.transform, ReturnAnchor = dock.transform, UseSourceHandRotation = true } };
                Assert.IsTrue(review.Interact());
                review.Tick(1f / 60f);
                Assert.IsNull(actor.RightInteractionTarget.Value.Rotation);
                root.transform.position += Vector3.back * 2f;
                Assert.IsFalse(review.Interact()); Assert.AreEqual(0, review.Held);
                root.transform.position += Vector3.forward * 2f;
                Assert.IsTrue(review.Interact()); Assert.IsTrue(review.Placing);
                dock.transform.position += Vector3.right * .1f;
                review.Tick(1f / 60f);
                Assert.IsFalse(review.Placing); Assert.AreEqual(0, review.Held);
                dock.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Assert.IsTrue(review.Interact());
                for (int i = 0; i < 180; i++) review.Tick(1f / 60f);
                Assert.AreEqual(-1, review.Held);
                Assert.That(Vector3.Distance(item.transform.position, dock.transform.position), Is.LessThan(.002f));
                Assert.That(Quaternion.Angle(item.transform.rotation, dock.transform.rotation), Is.LessThan(.1f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(item); Object.DestroyImmediate(dock); }
        }

        [Test]
        public void PlacementBoundsUseFinalOrientationWithoutMovingHeldItem()
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                item.transform.position = new Vector3(3f, 2f, 1f);
                item.transform.localScale = new Vector3(.2f, .6f, .3f);
                item.transform.rotation = Quaternion.Euler(0, 0, 45f);
                var rotation = item.transform.rotation;
                var method = typeof(HumanInteractionReview).GetMethod("ItemBounds",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var bounds = (Bounds)method.Invoke(null, new object[] { item.transform, Quaternion.identity });
                Assert.That(Vector3.Distance(bounds.size, item.transform.localScale), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(bounds.center, item.transform.position), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(rotation, item.transform.rotation), Is.LessThan(.001f));
            }
            finally { Object.DestroyImmediate(item); }
        }

        [Test]
        public void ExplicitChestCollectionReturnsToInspectionBeforeClosing()
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/CollectChest.asset");
            var session = new ActorInteractionSession();
            var markers = new System.Collections.Generic.List<string>();
            session.Marker += markers.Add;
            session.Begin(definition.Snapshot()); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { "Collect" }, markers);
            Assert.IsTrue(session.Phase.WaitForInput);
            Assert.IsTrue(session.Continue()); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { "Collect", "Close" }, markers);
            Assert.IsFalse(session.Active);
        }

        [Test]
        public void HingeGripCorrectionFollowsTravelInBothDirections()
        {
            var root = new GameObject("Grip fixture"); var hinge = new GameObject("Hinge");
            var contact = new GameObject("Grip");
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>(); var clip = new AnimationClip();
            try
            {
                root.transform.position = Vector3.right * 400f;
                hinge.transform.position = root.transform.position + Vector3.forward * .6f;
                contact.transform.SetParent(hinge.transform, false); contact.transform.localPosition = Vector3.up;
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                definition.Action = "Grip";
                definition.Phases = new[] { new ActorInteractionDefinition.Phase {
                    Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Hold", Clip = clip },
                    Seconds = 10f, RightHandWeight = 1f, WaitForInput = true } };
                var offset = new Vector3(0, -.14f, .08f);
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = contact.transform,
                    Hinge = hinge.transform, OpenEuler = new Vector3(0, 90, 0), OpenHandOffset = offset, Use = definition } };
                Assert.IsTrue(review.Interact());
                foreach (float angle in new[] { 0f, 45f, 90f, 45f, 0f })
                {
                    hinge.transform.rotation = Quaternion.Euler(0, angle, 0); review.Tick(0f);
                    Assert.That(Vector3.Distance(actor.RightInteractionTarget.Value.Position,
                        contact.transform.position + hinge.transform.TransformVector(offset) * (angle / 90f)), Is.LessThan(.0001f));
                }
                review.Cancel(); Assert.IsNull(actor.RightInteractionTarget);
                review.Targets[0].UseSourceHandRotation = true;
                definition.Phases[0].GripRadius = .035f;
                Assert.IsTrue(review.Interact());
                review.Tick(1f / 60f);
                Assert.IsNull(actor.RightInteractionTarget.Value.Rotation);
                Assert.AreEqual(0f, actor.RightInteractionTarget.Value.GripRadius);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(hinge);
                Object.DestroyImmediate(definition); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void ChestClosesWithoutCollectingAfterInspectionInput()
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(
                "Assets/Art/Interactions/Definitions/Chest.asset");
            var session = new ActorInteractionSession();
            var markers = new System.Collections.Generic.List<string>();
            session.Marker += markers.Add;
            session.Begin(definition.Snapshot()); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { "Open" }, markers);
            Assert.IsTrue(session.Phase.WaitForInput);
            Assert.IsTrue(session.Continue()); session.Advance(100f);
            CollectionAssert.AreEqual(new[] { "Open", "Close" }, markers);
            Assert.IsFalse(session.Active);
        }

        [Test]
        public void OpenDoorSelectionFollowsTheHandleInsteadOfTheClosedAnchor()
        {
            var root = new GameObject("Open door fixture"); var hinge = new GameObject("Hinge");
            var handle = new GameObject("Handle"); var selection = new GameObject("Closed selection");
            try
            {
                root.transform.position = new Vector3(300, 0, -.5f);
                hinge.transform.position = new Vector3(301, 0, 0);
                handle.transform.SetParent(hinge.transform, false); handle.transform.localPosition = new Vector3(-1, 1, 0);
                selection.transform.position = handle.transform.position;
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = handle.transform,
                    SelectionAnchor = selection.transform, Hinge = hinge.transform,
                    OpenEuler = new Vector3(0, 90, 0), OpenAwayFromActor = true } };
                Assert.IsTrue(review.Interact()); review.Tick(1f); review.RefreshTarget();
                Assert.AreEqual(-1, review.Selected);
                root.transform.position = handle.transform.position - Vector3.forward * .5f - Vector3.up;
                review.RefreshTarget(); Assert.AreEqual(0, review.Selected);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(hinge); Object.DestroyImmediate(selection); }
        }

        [TestCase(-.5f, 90f)]
        [TestCase(.5f, 90f)]
        [TestCase(-.5f, -90f)]
        [TestCase(.5f, -90f)]
        public void DoorOpensAwayFromEitherApproachSide(float actorZ, float openAngle)
        {
            var root = new GameObject("Door approach fixture"); var hinge = new GameObject("Hinge");
            var contact = new GameObject("Handle");
            try
            {
                hinge.transform.position = Vector3.right * 300;
                contact.transform.SetParent(hinge.transform, false); contact.transform.localPosition = new Vector3(-.5f, 1f, 0f);
                root.transform.position = hinge.transform.position + new Vector3(-.5f, 0f, actorZ);
                root.transform.forward = actorZ < 0f ? Vector3.forward : Vector3.back;
                var actor = root.AddComponent<HumanoidAnimationPrototype>(); var review = root.AddComponent<HumanInteractionReview>();
                review.Actor = actor;
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = contact.transform, Hinge = hinge.transform,
                    OpenEuler = new Vector3(0f, openAngle, 0f), OpenAwayFromActor = true } };
                Assert.IsTrue(review.Interact()); review.Tick(.1f);
                Assert.That(contact.transform.position.z * actorZ, Is.LessThan(0f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(hinge); }
        }

        [Test]
        public void UseBlendsHingeAndCanReverseWithoutResettingItsPose()
        {
            var root = new GameObject("Use fixture");
            var door = new GameObject("Door");
            try
            {
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                door.transform.position = new Vector3(0, 1, .5f);
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = door.transform,
                    Hinge = door.transform, OpenEuler = new Vector3(0, 90, 0) } };
                Assert.IsTrue(review.Interact());
                Assert.That(Quaternion.Angle(Quaternion.identity, door.transform.rotation), Is.Zero);
                review.Tick(.1f);
                Assert.That(Quaternion.Angle(Quaternion.identity, door.transform.rotation), Is.EqualTo(9f).Within(.01f));
                Quaternion before = door.transform.rotation;
                Assert.IsTrue(review.Interact());
                Assert.That(Quaternion.Angle(before, door.transform.rotation), Is.Zero);
                review.Tick(.05f);
                Assert.That(Quaternion.Angle(before, door.transform.rotation), Is.EqualTo(4.5f).Within(.01f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(door); }
        }

        [Test]
        public void PickupAndCancelledPlacementKeepDisplayedPoseAndRestoreColliderState()
        {
            var root = new GameObject("Pickup fixture");
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var contact = new GameObject("Contact");
            try
            {
                root.transform.position = Vector3.right * 100;
                floor.transform.position = root.transform.position - Vector3.up * .1f;
                floor.transform.localScale = new Vector3(4, .2f, 4);
                item.transform.position = root.transform.position + new Vector3(0, .1f, .5f);
                item.transform.localScale = Vector3.one * .2f;
                contact.transform.SetParent(item.transform, false);
                var disabled = item.AddComponent<SphereCollider>(); disabled.enabled = false;
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = contact.transform, PickupRoot = item.transform } };
                Vector3 start = item.transform.position;
                Physics.SyncTransforms();
                Assert.IsTrue(review.Interact());
                Assert.That(item.transform.position, Is.EqualTo(start));
                Assert.IsFalse(item.GetComponent<BoxCollider>().enabled);
                review.Tick(1f / 60f);
                Assert.That(Vector3.Distance(start, item.transform.position), Is.LessThanOrEqualTo(.02001f));
                for (int i = 0; i < 90; i++) review.Tick(1f / 60f);
                Vector3 localCarry = root.transform.InverseTransformPoint(item.transform.position);
                Quaternion localRotation = Quaternion.Inverse(root.transform.rotation) * item.transform.rotation;
                root.transform.SetPositionAndRotation(root.transform.position + Vector3.right * .3f, Quaternion.Euler(0f, 120f, 0f));
                review.Tick(1f / 60f);
                Assert.Less(Vector3.Distance(localCarry, root.transform.InverseTransformPoint(item.transform.position)), .01f,
                    "The acquired prop must follow actor travel and turning without world-space lag.");
                Assert.Less(Quaternion.Angle(localRotation, Quaternion.Inverse(root.transform.rotation) * item.transform.rotation), .01f);
                root.transform.SetPositionAndRotation(Vector3.right * 100, Quaternion.identity);
                review.Tick(1f / 60f);
                Physics.SyncTransforms();
                // A cancelled rack return must not poison the next floor placement.
                var dock = new GameObject("Temporary rack dock");
                dock.transform.SetParent(floor.transform, true);
                dock.transform.position = root.transform.position + Vector3.forward * .65f + Vector3.up * .1f;
                review.Targets[0].ReturnAnchor = dock.transform;
                Assert.IsTrue(review.Interact(), review.Status);
                review.Cancel();
                review.Targets[0].ReturnAnchor = null;
                Assert.IsTrue(review.Interact(), review.Status);
                Assert.IsTrue(review.Placing);
                review.Tick(.1f);
                Vector3 beforeCancel = item.transform.position;
                review.Cancel();
                Assert.That(item.transform.position, Is.EqualTo(beforeCancel));
                Assert.That(review.Held, Is.EqualTo(0));
                review.Tick(1f / 60f);
                Assert.That(Vector3.Distance(beforeCancel, item.transform.position), Is.LessThanOrEqualTo(.10001f));
                review.ResetRoom();
                Assert.IsTrue(item.GetComponent<BoxCollider>().enabled);
                Assert.IsFalse(disabled.enabled);
                Assert.IsNull(actor.RightInteractionTarget);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(item); Object.DestroyImmediate(floor); }
        }

        [Test]
        public void SharedDefinitionRetargetsAndCancelsUncommittedMarkers()
        {
            var root = new GameObject("Shared definition fixture");
            var first = new GameObject("First"); var second = new GameObject("Second");
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>(); var clip = new AnimationClip();
            try
            {
                root.transform.position = Vector3.right * 200;
                first.transform.position = root.transform.position + Vector3.forward * .5f;
                second.transform.position = root.transform.position + Vector3.forward * .8f;
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                definition.Action = "CustomUse";
                definition.Phases = new[] { new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase
                    { Name = "Use", Clip = clip }, Marker = "CustomEffect", Seconds = 1f } };
                review.Targets = new[] {
                    new HumanInteractionReview.Target { Contact = first.transform, Use = definition },
                    new HumanInteractionReview.Target { Contact = second.transform, Use = definition } };
                var committed = new System.Collections.Generic.List<HumanInteractionReview.Target>();
                review.InteractionMarker += (target, marker) => committed.Add(target);
                Assert.IsTrue(review.Interact()); review.Tick(.2f);
                first.transform.position = root.transform.position - Vector3.forward;
                review.RefreshTarget();
                Assert.IsFalse(review.Session.Active);
                Assert.IsTrue(review.Interact()); review.Tick(.6f);
                Assert.That(committed.Count, Is.EqualTo(1));
                Assert.AreSame(review.Targets[1], committed[0]);
                second.SetActive(false); review.Tick(.1f);
                Assert.IsFalse(review.Session.Active);
                Assert.IsNull(actor.RightInteractionTarget);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(first); Object.DestroyImmediate(second);
                Object.DestroyImmediate(definition); Object.DestroyImmediate(clip); }
        }

        [TestCase(0f)]
        [TestCase(90f)]
        public void ContactApproachPreservesHeightAndAlignsTheComfortableReach(float turn)
        {
            var rotation = Quaternion.Euler(0f, turn, 0f);
            var seed = new CharacterPose(new Vector3(3f, 2f, 4f), Vector3.up, rotation * Vector3.forward);
            Vector3 target = new Vector3(4f, 3.2f, 5f), local = new Vector3(.16f, 0f, .7f);
            var plan = ActorInteractionApproach.ForContact(seed, target, rotation * Vector3.forward, local);
            Assert.That(plan.Position.y, Is.EqualTo(seed.Position.y));
            Vector3 predicted = plan.Position + Quaternion.LookRotation(plan.Forward, plan.Up) * local;
            Assert.That(Vector3.ProjectOnPlane(predicted - target, plan.Up).magnitude, Is.LessThan(.0001f));
            Assert.Throws<System.ArgumentException>(() => ActorInteractionApproach.ForContact(seed, target, Vector3.up, local));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void OffAxisDoorApproachesBeforeStartingItsReachAndCanCancel(bool targetMoves, bool unreachableHeight)
        {
            var root = new GameObject("Off-axis door fixture");
            var hinge = new GameObject("Door hinge");
            var contact = new GameObject("Door handle");
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
            var clip = new AnimationClip();
            try
            {
                root.transform.position = new Vector3(200f, 0f, 0f);
                root.transform.rotation = Quaternion.Euler(0f, 65f, 0f);
                contact.transform.SetParent(hinge.transform, false);
                contact.transform.position = root.transform.position + new Vector3(.5f, 1f, .7f);
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var motor = new SurfaceCharacterController(actor, actor, 0f,
                    new CharacterPose(root.transform.position, Vector3.up, root.transform.forward));
                typeof(HumanoidAnimationPrototype).GetField("_motor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(actor, motor);
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                definition.Action = "Door";
                definition.Phases = new[] { new ActorInteractionDefinition.Phase {
                    Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Reach", Clip = clip }, Seconds = 2f } };
                review.Targets = new[] { new HumanInteractionReview.Target { Contact = contact.transform,
                    Hinge = hinge.transform, OpenAwayFromActor = true, Use = definition } };
                if (unreachableHeight)
                {
                    review.Targets[0].HasAuthoredContact = true;
                    review.Targets[0].AuthoredContactOffset = new Vector3(-.08f, 1.24f, .572f);
                    contact.transform.position += Vector3.up;
                    Assert.IsFalse(review.Interact());
                    Assert.IsFalse(review.Approaching);
                    Assert.IsFalse(review.Session.Active);
                    return;
                }
                Vector3 before = root.transform.position;
                Assert.IsTrue(review.Interact());
                Assert.IsTrue(review.Approaching, "An off-axis door must use locomotion before its authored reach.");
                Assert.IsFalse(review.Session.Active);
                Assert.IsFalse(actor.InteractionMovementLocked);
                Assert.IsTrue(actor.InteractionApproachPosition.HasValue);
                Assert.That(Vector3.Angle(actor.InteractionApproachForward.Value, Vector3.forward), Is.LessThan(.01f));
                Assert.AreEqual(before, root.transform.position, "Planning must not teleport the actor.");
                if (targetMoves) { contact.transform.position += Vector3.right * .2f; review.Tick(.02f); }
                else review.Cancel();
                Assert.IsFalse(review.Approaching);
                Assert.IsNull(actor.InteractionApproachPosition);
                Assert.IsNull(actor.InteractionApproachForward);
                Assert.IsFalse(review.Session.Active);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(hinge);
                Object.DestroyImmediate(definition); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void InteractionHoldsCurrentStanceInsteadOfSlidingDuringClip()
        {
            var root = new GameObject("Approach fixture");
            var contact = new GameObject("Contact");
            var approach = new GameObject("Approach");
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
            var clip = new AnimationClip();
            try
            {
                root.transform.position = Vector3.right * 200f;
                contact.transform.position = root.transform.position + Vector3.forward * .8f;
                approach.transform.position = root.transform.position + Vector3.forward * .3f;
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                definition.Action = "Use";
                definition.Phases = new[] { new ActorInteractionDefinition.Phase {
                    Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Reach", Clip = clip }, Seconds = 2f } };
                review.Targets = new[] { new HumanInteractionReview.Target {
                    Contact = contact.transform, Approach = approach.transform, Use = definition } };
                Assert.IsTrue(review.Interact()); review.Tick(.1f);
                Assert.AreEqual(root.transform.position, actor.InteractionPosition);
                Assert.AreEqual(approach.transform.forward, actor.InteractionForward);
                Assert.IsTrue(actor.InteractionMovementLocked);
                review.Cancel();
                Assert.IsNull(actor.InteractionPosition);
                Assert.IsNull(actor.InteractionForward);
                Assert.IsFalse(actor.InteractionMovementLocked);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(contact); Object.DestroyImmediate(approach);
                Object.DestroyImmediate(definition); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void PhaseContactsRequireAnchorsAndSwitchHandsWithoutChangingTheSelectedObject()
        {
            var root = new GameObject("Contact fixture"); var contact = new GameObject("Lid");
            var inside = new GameObject("Inside"); var rim = new GameObject("Rim");
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>(); var clip = new AnimationClip();
            try
            {
                root.transform.position = Vector3.right * 200;
                contact.transform.position = root.transform.position + Vector3.forward * .5f;
                inside.transform.position = contact.transform.position + Vector3.up * .3f;
                rim.transform.position = contact.transform.position + Vector3.left * .2f;
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                definition.Action = "Chest";
                definition.Phases = new[] { new ActorInteractionDefinition.Phase {
                    Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Inspect", Clip = clip },
                    ContactSet = "Inside", RightHandWeight = 1f, LeftHandWeight = 1f, Seconds = 1f } };
                var target = new HumanInteractionReview.Target { Contact = contact.transform, Use = definition };
                review.Targets = new[] { target };
                Assert.IsFalse(review.Interact(), "Missing required contacts must reject the action.");
                target.ContactSets = new[] { new HumanInteractionReview.HandContacts { Id = "Inside", Right = inside.transform, Left = rim.transform } };
                Assert.IsTrue(review.Interact()); review.Tick(.1f);
                Assert.AreEqual(inside.transform.position, actor.RightInteractionTarget.Value.Position);
                Assert.AreEqual(rim.transform.position, actor.LeftInteractionTarget.Value.Position);
                Assert.AreEqual(0, review.Selected);
                review.Cancel(); Assert.IsNull(actor.RightInteractionTarget); Assert.IsNull(actor.LeftInteractionTarget);
                definition.Phases[0].ReverseAnimation = true;
                definition.Phases[0].GripRadius = .025f;
                var plan = definition.Snapshot();
                definition.Phases[0].ReverseAnimation = false; definition.Phases[0].ContactSet = "Changed";
                Assert.IsTrue(plan[0].ReverseAnimation); Assert.AreEqual("Inside", plan[0].ContactSet);
                Assert.AreEqual(.025f, plan[0].GripRadius);
                definition.Phases[0].GripRadius = float.NaN;
                Assert.Throws<System.ArgumentOutOfRangeException>(() => definition.Snapshot());
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(contact); Object.DestroyImmediate(inside);
                Object.DestroyImmediate(rim); Object.DestroyImmediate(definition); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void InspectionCanContinueBeforeItsFirstLoopCompletes()
        {
            var clip = new AnimationClip();
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
            try
            {
                definition.Action = "Inspect";
                definition.Phases = new[] {
                    new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase {
                        Name = "Inspect", Clip = clip, Loop = true }, Seconds = 3f, WaitForInput = true, ForwardLeanDegrees = 20f },
                    new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase {
                        Name = "Close", Clip = clip } }
                };
                var plan = definition.Snapshot();
                definition.Phases[0].ForwardLeanDegrees = 0f;
                Assert.AreEqual(20f, plan[0].ForwardLeanDegrees);
                var session = new ActorInteractionSession();
                session.Begin(plan); session.Advance(.1f);
                Assert.IsTrue(session.Continue());
                Assert.AreEqual("Close", session.Phase.Name);
                foreach (float value in new[] { float.NaN, float.PositiveInfinity, 31f, -31f })
                {
                    definition.Phases[0].ForwardLeanDegrees = value;
                    Assert.Throws<System.ArgumentOutOfRangeException>(() => definition.Snapshot());
                }
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(definition); }
        }

        [Test]
        public void SequenceMarkersFireOnceAndWaitForInputWithoutDependingOnRendering()
        {
            var clip = new AnimationClip();
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
            try
            {
                definition.Action = "Test";
                definition.Phases = new[]
                {
                    new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Reach", Clip = clip }, Seconds = 1f, Marker = "Pickup" },
                    new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Inspect", Clip = clip }, Seconds = 1f, WaitForInput = true },
                    new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase { Name = "Place", Clip = clip }, Seconds = 1f, Marker = "Place" }
                };
                var session = new ActorInteractionSession();
                var markers = new System.Collections.Generic.List<string>();
                session.Marker += markers.Add;
                var snapshot = definition.Snapshot();
                definition.Phases[0].Marker = "Changed after snapshot";
                session.Begin(snapshot);
                session.Advance(10f);
                CollectionAssert.AreEqual(new[] { "Pickup" }, markers);
                Assert.That(session.Phase.Name, Is.EqualTo("Inspect"));
                Assert.IsTrue(session.Continue());
                session.Advance(2f);
                CollectionAssert.AreEqual(new[] { "Pickup", "Place" }, markers);
                Assert.IsFalse(session.Active);
                session.Begin(snapshot); session.Advance(.1f); session.Cancel(); session.Advance(10f);
                Assert.That(markers.Count, Is.EqualTo(2), "Cancellation must not commit a future marker.");
                Assert.Throws<System.ArgumentOutOfRangeException>(() => session.Advance(float.NaN));
                definition.Phases[0].Animation.BlendSeconds = 0f;
                Assert.Throws<System.ArgumentException>(() => definition.Snapshot());
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(definition); }
        }

        [Test]
        public void WalkingBetweenObjectsRetargetsWithoutVisitingAndReleasesOldReach()
        {
            var root = new GameObject("Actor fixture");
            var first = new GameObject("First contact");
            var second = new GameObject("Second contact");
            try
            {
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var review = root.AddComponent<HumanInteractionReview>(); review.Actor = actor;
                first.transform.position = new Vector3(0, 1, .5f);
                second.transform.position = new Vector3(3, 1, .5f);
                review.Targets = new[] {
                    new HumanInteractionReview.Target { Contact = first.transform },
                    new HumanInteractionReview.Target { Contact = second.transform } };
                Assert.IsTrue(review.Interact());
                Assert.AreSame(first.transform, actor.HandTarget);
                Assert.IsTrue(actor.Reach);
                root.transform.position = Vector3.right * 3;
                review.RefreshTarget();
                Assert.AreSame(second.transform, actor.HandTarget);
                Assert.IsFalse(actor.Reach, "A new target must not inherit the old reach.");
                Assert.IsTrue(review.Interact());
                Assert.IsTrue(actor.Reach);
                root.transform.rotation = Quaternion.Euler(0, 180, 0);
                Assert.IsFalse(review.Interact(), "Targets behind the actor must not be selected.");
                Assert.IsNull(actor.HandTarget);
                Assert.IsFalse(actor.Reach);
                root.transform.rotation = Quaternion.identity;
                second.SetActive(false);
                Assert.IsFalse(review.Interact(), "Inactive and distant targets must be rejected.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void DryInteractionRoomDoesNotInheritTheMovementWorkbenchWaterRegion()
        {
            var root = new GameObject("Dry interaction fixture");
            try
            {
                var actor = root.AddComponent<HumanoidAnimationPrototype>();
                var point = new Vector3(6, 0, 0);
                Assert.IsTrue(actor.TryGetDepth(point, out _, out _), "Existing movement scenes keep their water fixture.");
                actor.EnableWater = false;
                Assert.IsFalse(actor.TryGetDepth(point, out float depth, out float bodyDepth));
                Assert.AreEqual(0f, depth);
                Assert.AreEqual(0f, bodyDepth);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}






