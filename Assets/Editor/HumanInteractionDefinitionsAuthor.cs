using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authors reusable interaction assets and assigns the review room without rebuilding it.</summary>
public static class HumanInteractionDefinitionsAuthor
{
    const string Folder = "Assets/Art/Interactions/Definitions";
    const string Clips = "Assets/Art/Interactions/Animations/";

    [MenuItem("Tools/Actors/Human/Configure Interaction Sequences")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before authoring interactions.");
        var review = UnityEngine.Object.FindFirstObjectByType<HumanInteractionReview>();
        if (review == null || review.gameObject.scene.path != HumanInteractionReviewAuthor.ScenePath)
            throw new InvalidOperationException("Open the Human interaction review scene.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Interactions", "Definitions");
        var definitions = new List<ActorInteractionDefinition>();
        ActorInteractionDefinition.Phase Phase(string name, string file, float first = 0f, float last = 1f,
            float right = 0f, float left = 0f, string marker = "", bool wait = false, bool movement = false, bool locomotion = false)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(Clips + file).OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (!clip.isHumanMotion) throw new InvalidOperationException(file + " must be a Humanoid animation.");
            return new ActorInteractionDefinition.Phase
            {
                Animation = new ActorAnimationPerformanceLibrary.Phase { Name = name, Clip = clip,
                    StartNormalized = first, EndNormalized = last, Loop = wait && last - first > .2f, BlendSeconds = .2f },
                Seconds = Mathf.Max(.3f, clip.length * (last - first)), RightHandWeight = right, LeftHandWeight = left,
                Marker = marker, MarkerProgress = marker == "Place" ? .98f : .15f,
                WaitForInput = wait, AllowMovement = movement, UseLocomotion = locomotion
            };
        }
        ActorInteractionDefinition Definition(string action, params ActorInteractionDefinition.Phase[] phases)
        {
            string path = Folder + "/" + action + ".asset";
            var definition = AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
                definition.Action = action; definition.Phases = phases; definition.Snapshot();
                AssetDatabase.CreateAsset(definition, path);
            }
            definitions.Add(definition); return definition;
        }
        var chest = Definition("Chest", HumanChestMotionAuthor.CreatePhases("Chest"));
        var collectChest = Definition("CollectChest", HumanChestMotionAuthor.CreatePhases("CollectChest"));
        var closeChest = Definition("CloseChest", HumanChestMotionAuthor.CreatePhases("CloseChest"));
        var door = Definition("OpenDoor", HumanDoorMotionAuthor.CreatePhases("OpenDoor"));
        var closeDoor = Definition("CloseDoor", HumanDoorMotionAuthor.CreatePhases("CloseDoor"));
        ActorInteractionDefinition Pickup(string action, string prefix) => Definition(action,
            Phase("Reach for item", prefix + "Enter.fbx", 0f, .55f, right: 1f),
            Phase("Lift item", prefix + "Enter.fbx", .55f, 1f, right: 1f, marker: "Pickup"),
            Phase("Stand with item", prefix + "Exit.fbx", .4f, 1f),
            Phase("Carry item", prefix + "Loop.fbx", wait: true, movement: true, locomotion: true));
        ActorInteractionDefinition PutDown(string action, string file) => Definition(action,
            Phase("Lower item", file, 0f, .8f, right: 1f, left: 1f, marker: "Place"),
            Phase("Release and recover", file, .8f, 1f));
        var table = Pickup("TablePickup", "Loot_TableTop_RightHand_Inspect_");
        var ground = Pickup("GroundPickup", "Loot_FloorPickUp_Kneel_RightHand_Inspect_");
        var tablePlace = PutDown("TablePlace", "Loot_TableTop_RightHand_Inspect_Exit.fbx");
        var groundPlace = Definition("GroundPlace",
            Phase("Lower item", "Loot_FloorPickUp_Kneel_RightHand_Inspect_Enter.fbx", 0f, .55f, right: 1f, marker: "Place"),
            Phase("Release and recover", "Loot_FloorPickUp_Kneel_RightHand_Inspect_Exit.fbx", .4f, 1f));
        const string lift = "HumanM@Carry01_PickUp01.fbx", carry = "HumanM@Carry01_Idle01.fbx";
        var bend = Phase("Bend and grip", lift, 0f, .5f, right: 1f, left: 1f);
        bend.Animation.BlendSeconds = .4f; bend.Seconds = .9f;
        var crate = Definition("TwoHandCarry", bend,
            Phase("Lift with both hands", lift, .5f, 1f, right: 1f, left: 1f, marker: "Pickup"),
            Phase("Carry", carry, right: 1f, left: 1f, wait: true, movement: true, locomotion: true));
        var cratePlace = PutDown("TwoHandPlace", "HumanM@Carry01_Drop01.fbx");
        var turnToSeat = Phase("Turn toward seat", "HumanM@SitMedium01 - Begin.fbx", 0f, .05f);
        turnToSeat.Seconds = 1.2f;
        var sit = Definition("Sit", turnToSeat,
            Phase("Sit down", "HumanM@SitMedium01 - Begin.fbx", marker: "Seat"),
            Phase("Seated", "HumanM@SitMedium01 - Loop.fbx", wait: true),
            Phase("Stand up", "HumanM@SitMedium01 - Stop.fbx", marker: "Stand"));
        var stand = Definition("Stand", Phase("Stand up", "HumanM@SitMedium01 - Stop.fbx", marker: "Stand"));
        var drag = Definition("MoveChair", Phase("Grip chair back", "Activate_Wall_KeyTurn_DoorKnob.FBX", 0f, .35f, right: 1f, left: 1f),
            Phase("Move chair", "Activate_Floor_Box_Push.FBX", right: 1f, left: 1f, marker: "Drag", wait: true, movement: true, locomotion: true));
        Undo.RecordObjects(new UnityEngine.Object[] { review, review.Actor }, "Assign interaction definitions");
        review.Targets[0].Use = chest; review.Targets[0].Close = closeChest; review.Targets[0].Collect = collectChest;
        review.Targets[1].Use = door; review.Targets[1].Close = closeDoor;
        review.Targets[1].OpenAwayFromActor = true;
        review.Targets[1].MatchContactRotation = true;
        review.Targets[1].RightElbowDirection = Vector3.zero;
        review.Targets[1].Contact.rotation = Quaternion.LookRotation(review.Targets[1].Hinge.root.forward, -review.Targets[1].Hinge.root.right);
        HumanDoorMotionAuthor.ConfigureTarget(review.Targets[1]);
        for (int i = 2; i <= 7; i++)
        {
            var target = review.Targets[i]; target.PickupRoot = target.Contact.parent;
            target.FollowAnimatedHand = i < 7;
            target.FourSidedPickup = i == 7;
            if (target.FourSidedPickup) target.SelectionAnchor = target.PickupRoot;
            target.Use = i == 7 ? crate : i == 6 ? ground : table;
            target.PutDown = i == 7 ? cratePlace : i == 6 ? groundPlace : tablePlace;
            if (i < 7)
            {
                target.GroundUse = ground; target.TableUse = table;
                target.GroundPutDown = groundPlace; target.TablePutDown = tablePlace;
            }
        }
        var chair = review.Targets[8];
        chair.Use = sit; chair.Close = stand; chair.Rear = drag; chair.PutDown = cratePlace; chair.PickupRoot = chair.Contact.parent;
        Transform Anchor(string name, Transform parent, Vector3 local)
        {
            var anchor = parent.Find(name);
            if (anchor == null) { anchor = new GameObject(name).transform; Undo.RegisterCreatedObjectUndo(anchor.gameObject, "Add interaction anchor"); anchor.SetParent(parent, false); }
            anchor.localPosition = local; return anchor;
        }
        chair.SeatApproach = Anchor("Seat approach", chair.PickupRoot, new Vector3(0f, 0f, .5f));
        chair.SeatApproach.localRotation = Quaternion.identity;
        chair.LeftContact = Anchor("Left back grip", chair.PickupRoot, new Vector3(-.18f, 1.05f, -.2f));
        chair.Contact.localPosition = new Vector3(.18f, 1.05f, -.2f);
        chair.SelectionAnchor = Anchor("Chair selection", chair.PickupRoot, new Vector3(0f, .5f, 0f));
        review.Actor.Stations[8].SetPositionAndRotation(chair.PickupRoot.position + Vector3.forward * .9f, Quaternion.Euler(0f, 180f, 0f));
        foreach (var target in review.Targets.Where(t => t.Hinge != null))
        {
            var parent = target.Hinge.root;
            if (target.SelectionAnchor == null)
                target.SelectionAnchor = Anchor(target.Label + " selection", parent, parent.InverseTransformPoint(target.Contact.position));
        }
        var chestRoot = review.Targets[0].Hinge.root;
        review.Targets[0].LookAnchor = Anchor("Look inside", chestRoot, new Vector3(.07f, .84f, -.4f));
        var chestTarget = review.Targets[0];
        // Keep the whole glove outside the lip, including the finger curl during clip blending.
        Vector3 lidCenter = chestRoot.TransformPoint(new Vector3(0f, .72f, -.44f));
        chestTarget.Contact.position = lidCenter + chestRoot.right * .252f;
        chestTarget.Contact.rotation = Quaternion.LookRotation(-chestRoot.up, chestRoot.forward);
        chestTarget.LeftContact = Anchor("Left lid grip", chestTarget.Hinge,
            chestTarget.Hinge.InverseTransformPoint(lidCenter - chestRoot.right * .252f));
        chestTarget.LeftContact.rotation = chestTarget.Contact.rotation;
        chestTarget.MatchContactRotation = true;
        chestTarget.OpenHandOffset = new Vector3(0f, -.14f, .08f);
        chestTarget.OpenEuler = new Vector3(-55f, 0f, 0f);
        chestTarget.HandOrientation = Anchor("Palm orientation", chestRoot, Vector3.zero);
        chestTarget.HandOrientation.rotation = Quaternion.LookRotation(-chestRoot.up, chestRoot.forward);
        chestTarget.RightElbowDirection = new Vector3(1f, 0f, -.2f);
        chestTarget.LeftElbowDirection = new Vector3(-1f, 0f, -.2f);
        chestTarget.Approach = Anchor("Chest approach", chestRoot, new Vector3(0f, 0f, -.94f));
        chestTarget.Approach.rotation = chestRoot.rotation;
        var rim = Anchor("Inspection rim support", chestRoot, new Vector3(-.22f, .43f, -.36f));
        var inside = Anchor("Inspection reach inside", chestRoot, new Vector3(.18f, .4f, -.29f));
        rim.rotation = inside.rotation = Quaternion.LookRotation(-chestRoot.up, chestRoot.forward);
        var clearRight = Anchor("Right hand clearance", chestRoot, new Vector3(.252f, 1.18f, -.35f));
        var clearLeft = Anchor("Left hand clearance", chestRoot, new Vector3(-.252f, 1.18f, -.35f));
        var closeRight = Anchor("Right close clearance", chestRoot, new Vector3(.30f, 1.12f, -.25f));
        var closeLeft = Anchor("Left close clearance", chestRoot, new Vector3(-.30f, 1.12f, -.25f));
        closeRight.rotation = closeLeft.rotation = rim.rotation;
        var collected = Anchor("Collected item hold", chestRoot, new Vector3(.2f, .8f, -.55f));
        clearRight.rotation = clearLeft.rotation = collected.rotation = rim.rotation;
        chestTarget.ContactSets = new[] {
            new HumanInteractionReview.HandContacts { Id = "Inspect", Left = rim, Right = inside },
            new HumanInteractionReview.HandContacts { Id = "Clear", Left = clearLeft, Right = clearRight },
            new HumanInteractionReview.HandContacts { Id = "CloseClear", Left = closeLeft, Right = closeRight },
            new HumanInteractionReview.HandContacts { Id = "Collect", Left = rim, Right = collected }
        };
        review.Actor.Stations[0].SetPositionAndRotation(chestTarget.Approach.position, chestTarget.Approach.rotation);
        var leverOn = Definition("LeverOn",
            Phase("Reach for lever", "Activate_Wall_LargeLever_Pull.FBX", 0f, .3f, right: 1f),
            Phase("Pull lever", "Activate_Wall_LargeLever_Pull.FBX", .3f, .7f, right: 1f, marker: "Open"),
            Phase("Release lever", "Activate_Wall_LargeLever_Pull.FBX", .7f, 1f));
        var leverOff = Definition("LeverOff",
            Phase("Reach for lever", "Activate_Wall_LargeLever_PushUp.FBX", 0f, .3f, right: 1f),
            Phase("Push lever", "Activate_Wall_LargeLever_PushUp.FBX", .3f, .7f, right: 1f, marker: "Close"),
            Phase("Release lever", "Activate_Wall_LargeLever_PushUp.FBX", .7f, 1f));
        var takeEquipment = Pickup("TakeEquipment", "Loot_TableTop_RightHand_Inspect_");
        var returnEquipment = PutDown("ReturnEquipment", "Loot_TableTop_RightHand_Inspect_Exit.fbx");
        if (!review.Targets.Any(t => t.Use == leverOn))
        {
            var targets = review.Targets.ToList();
            var stations = review.Actor.Stations.ToList();
            var material = GameObject.Find("Interaction room floor").GetComponent<Renderer>().sharedMaterial;
            Transform Part(string name, Transform parent, Vector3 position, Vector3 scale)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(part, "Add interaction fixture");
                part.name = name; part.transform.SetParent(parent, false);
                part.transform.localPosition = position; part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = material;
                return part.transform;
            }
            void Add(HumanInteractionReview.Target target, Vector3 position)
            {
                var station = new GameObject(target.Label + " approach").transform;
                Undo.RegisterCreatedObjectUndo(station.gameObject, "Add interaction station");
                station.position = position;
                targets.Add(target); stations.Add(station);
            }
            var lever = new GameObject("07 / Lever").transform;
            Undo.RegisterCreatedObjectUndo(lever.gameObject, "Add lever");
            lever.position = new Vector3(-3.5f, 0f, -5f);
            Part("Lever pedestal", lever, new Vector3(0f, .5f, 0f), new Vector3(.35f, 1f, .35f));
            var pivot = Anchor("Lever pivot", lever, new Vector3(0f, 1f, 0f));
            Part("Lever arm", pivot, new Vector3(0f, .2f, 0f), new Vector3(.05f, .4f, .05f));
            Part("Lever handle", pivot, new Vector3(0f, .4f, 0f), new Vector3(.25f, .07f, .07f));
            var grip = Anchor("Lever grip", pivot, new Vector3(.07f, .4f, -.045f));
            Add(new HumanInteractionReview.Target { Label = "Lever", Task = "E pulls or resets the lever.",
                Contact = grip, Hinge = pivot, OpenEuler = new Vector3(-60f, 0f, 0f),
                SelectionAnchor = Anchor("Lever selection", lever, new Vector3(0f, 1.1f, 0f)),
                Use = leverOn, Close = leverOff }, new Vector3(-3.65f, 0f, -5.65f));

            var rack = new GameObject("08 / Equipment rack").transform;
            Undo.RegisterCreatedObjectUndo(rack.gameObject, "Add equipment rack");
            rack.position = new Vector3(2f, 0f, -5f);
            Part("Rack left post", rack, new Vector3(-.8f, .6f, 0f), new Vector3(.1f, 1.2f, .1f));
            Part("Rack right post", rack, new Vector3(.8f, .6f, 0f), new Vector3(.1f, 1.2f, .1f));
            Part("Rack support", rack, new Vector3(.07f, .73f, -.4f), new Vector3(1.7f, .12f, .15f));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Human/Review/Sword_01.fbx");
            if (model == null) throw new InvalidOperationException("The reviewed sword model is required.");
            for (int slot = 0; slot < 2; slot++)
            {
                float x = slot == 0 ? -.45f : .45f;
                var item = new GameObject(slot == 0 ? "Rack sword" : "Rack short sword").transform;
                Undo.RegisterCreatedObjectUndo(item.gameObject, "Add rack item");
                item.position = rack.TransformPoint(new Vector3(x, 1f, -.15f));
                var visual = UnityEngine.Object.Instantiate(model, item);
                visual.transform.localScale = Vector3.one * (slot == 0 ? 1f : .7f);
                var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(HumanTrialAuthor.Folder + "/Review/Townsfolk.mat");
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = Enumerable.Repeat(sourceMaterial, renderer.sharedMaterials.Length).ToArray();
                var contact = Anchor("Weapon grip", item, Vector3.zero);
                var dock = Anchor("Return slot " + slot, rack, rack.InverseTransformPoint(item.position));
                Add(new HumanInteractionReview.Target { Label = item.name, Task = "Face an item and press E to take it. E near the rack returns it.",
                    Contact = contact, PickupRoot = item, ReturnAnchor = dock, FollowAnimatedHand = true,
                    Use = takeEquipment, PutDown = returnEquipment }, rack.TransformPoint(new Vector3(x - .2f, 0f, -.75f)));
            }
            review.Targets = targets.ToArray(); review.Actor.Stations = stations.ToArray();
        }
        var pressButton = Definition("PressButton",
            Phase("Reach for button", "Activate_Wall_ButtonPush.FBX", 0f, .4f),
            Phase("Press button", "Activate_Wall_ButtonPush.FBX", .4f, .65f, right: 1f, marker: "Open"),
            Phase("Release button", "Activate_Wall_ButtonPush.FBX", .65f, 1f, marker: "Close"));
        if (!review.Targets.Any(t => t.Use == pressButton))
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(panel, "Add button panel");
            panel.name = "09 / Button panel";
            panel.transform.position = new Vector3(-1.6f, .7f, -5f);
            panel.transform.localScale = new Vector3(.45f, 1.4f, .2f);
            var material = GameObject.Find("Interaction room floor").GetComponent<Renderer>().sharedMaterial;
            panel.GetComponent<Renderer>().sharedMaterial = material;
            var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(button, "Add button");
            button.name = "Push button";
            button.transform.position = new Vector3(-1.6f, 1.15f, -5.14f);
            button.transform.localScale = new Vector3(.13f, .13f, .08f);
            button.GetComponent<Renderer>().sharedMaterial = material;
            var contact = Anchor("Button contact", button.transform, new Vector3(0f, 0f, -.5f));
            var station = new GameObject("Button approach").transform;
            Undo.RegisterCreatedObjectUndo(station.gameObject, "Add button station");
            station.position = new Vector3(-1.8f, 0f, -5.8f);
            review.Targets = review.Targets.Concat(new[] { new HumanInteractionReview.Target {
                Label = "Push button", Task = "E presses and releases the button.", Contact = contact,
                Hinge = button.transform, OpenOffset = new Vector3(0f, 0f, .035f),
                SpringReturn = true, UseSourceHandRotation = true, Use = pressButton } }).ToArray();
            review.Actor.Stations = review.Actor.Stations.Concat(new[] { station }).ToArray();
        }
        var wheelOpen = Definition("WheelOpen",
            Phase("Reach for wheel", "Activate_Wall_WheelValve_Open.FBX", 0f, .25f),
            Phase("Turn wheel", "Activate_Wall_WheelValve_Open.FBX", .25f, .75f, right: 1f, left: 1f, marker: "Open"),
            Phase("Release wheel", "Activate_Wall_WheelValve_Open.FBX", .75f, 1f));
        var wheelClose = Definition("WheelClose",
            Phase("Reach for wheel", "Activate_Wall_WheelValve_Close.FBX", 0f, .25f),
            Phase("Turn wheel back", "Activate_Wall_WheelValve_Close.FBX", .25f, .75f, right: 1f, left: 1f, marker: "Close"),
            Phase("Release wheel", "Activate_Wall_WheelValve_Close.FBX", .75f, 1f));
        if (!review.Targets.Any(t => t.Use == wheelOpen))
        {
            var support = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(support, "Add valve support");
            support.name = "10 / Wheel valve";
            support.transform.position = new Vector3(0f, .65f, -5f);
            support.transform.localScale = new Vector3(.2f, 1.3f, .2f);
            var material = GameObject.Find("Interaction room floor").GetComponent<Renderer>().sharedMaterial;
            support.GetComponent<Renderer>().sharedMaterial = material;
            var wheel = new GameObject("Wheel pivot").transform;
            Undo.RegisterCreatedObjectUndo(wheel.gameObject, "Add valve wheel");
            wheel.position = new Vector3(0f, 1.15f, -5.18f);
            for (int segment = 0; segment < 16; segment++)
            {
                float angle = segment * 360f / 16f;
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(part, "Add wheel rim");
                part.name = "Rim " + segment; part.transform.SetParent(wheel, false);
                part.transform.localPosition = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0f) * .28f;
                part.transform.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
                part.transform.localScale = new Vector3(.115f, .04f, .05f);
                part.GetComponent<Renderer>().sharedMaterial = material;
            }
            for (int spoke = 0; spoke < 2; spoke++)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(part, "Add wheel spoke");
                part.name = "Spoke " + spoke; part.transform.SetParent(wheel, false);
                part.transform.localRotation = Quaternion.Euler(0f, 0f, spoke * 90f);
                part.transform.localScale = new Vector3(.56f, .035f, .035f);
                part.GetComponent<Renderer>().sharedMaterial = material;
            }
            var right = Anchor("Right wheel grip", wheel, new Vector3(.28f, 0f, -.025f));
            var left = Anchor("Left wheel grip", wheel, new Vector3(-.28f, 0f, -.025f));
            var station = new GameObject("Wheel approach").transform;
            Undo.RegisterCreatedObjectUndo(station.gameObject, "Add wheel station");
            station.position = new Vector3(0f, 0f, -5.9f);
            review.Targets = review.Targets.Concat(new[] { new HumanInteractionReview.Target {
                Label = "Wheel valve", Task = "E turns the wheel; E after completion turns it back.",
                Contact = right, LeftContact = left, Hinge = wheel, OpenEuler = new Vector3(0f, 0f, 90f),
                UseSourceHandRotation = true, Use = wheelOpen, Close = wheelClose } }).ToArray();
            review.Actor.Stations = review.Actor.Stations.Concat(new[] { station }).ToArray();
        }
        const string cabinetClip = "Loot_CabinetSlidingDoor_GrabItem.fbx";
        ActorInteractionDefinition.Phase CabinetWait() => Phase("Inspect cabinet", cabinetClip, 0f, .04f, wait: true);
        ActorInteractionDefinition.Phase CabinetReverse(string name, float first, float last, float hand = 0f, string marker = "")
        {
            var phase = Phase(name, cabinetClip, first, last, right: hand, marker: marker);
            phase.ReverseAnimation = true; return phase;
        }
        ActorInteractionDefinition.Phase[] CabinetClose() => new[] {
            CabinetReverse("Reach for open panel", .34f, .45f),
            CabinetReverse("Slide panel closed", .16f, .34f, 1f, "Close"),
            CabinetReverse("Release panel", 0f, .16f) };
        var cabinet = Definition("SlidingCabinet", new[] {
            Phase("Reach for panel", cabinetClip, 0f, .16f),
            Phase("Slide panel open", cabinetClip, .16f, .34f, right: 1f, marker: "Open"),
            Phase("Withdraw hand", cabinetClip, .34f, .45f), CabinetWait() }.Concat(CabinetClose()).ToArray());
        var cabinetClose = Definition("CloseSlidingCabinet", CabinetClose());
        var cabinetCollection = Phase("Collect from cabinet", cabinetClip, .45f, .72f, marker: "Collect");
        cabinetCollection.MarkerProgress = .8f;
        var cabinetCollect = Definition("CollectSlidingCabinet", new[] {
            cabinetCollection,
            Phase("Recover after collection", cabinetClip, .72f, 1f), CabinetWait() }.Concat(CabinetClose()).ToArray());
        if (!review.Targets.Any(t => t.Use == cabinet))
        {
            var root = new GameObject("11 / Sliding cabinet").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Add sliding cabinet");
            root.position = new Vector3(4.5f, 0f, -5f);
            var material = GameObject.Find("Interaction room floor").GetComponent<Renderer>().sharedMaterial;
            Transform CabinetPart(string name, Vector3 position, Vector3 scale)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(part, "Add cabinet part");
                part.name = name; part.transform.SetParent(root, false);
                part.transform.localPosition = position; part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = material;
                return part.transform;
            }
            CabinetPart("Left side", new Vector3(-.65f, .85f, 0f), new Vector3(.06f, 1.7f, .4f));
            CabinetPart("Right side", new Vector3(.65f, .85f, 0f), new Vector3(.06f, 1.7f, .4f));
            CabinetPart("Back", new Vector3(0f, .85f, .18f), new Vector3(1.3f, 1.7f, .05f));
            CabinetPart("Shelf", new Vector3(0f, 1f, 0f), new Vector3(1.3f, .06f, .4f));
            CabinetPart("Top", new Vector3(0f, 1.7f, 0f), new Vector3(1.3f, .06f, .4f));
            CabinetPart("Fixed panel", new Vector3(.33f, 1.35f, -.16f), new Vector3(.65f, .65f, .05f));
            var panel = CabinetPart("Sliding panel", new Vector3(-.33f, 1.35f, -.23f), new Vector3(.65f, .65f, .05f));
            var contact = Anchor("Panel grip", panel, new Vector3(.22f, 0f, -.65f));
            var selection = Anchor("Cabinet selection", root, new Vector3(0f, 1.35f, -.28f));
            var station = new GameObject("Cabinet approach").transform;
            Undo.RegisterCreatedObjectUndo(station.gameObject, "Add cabinet station");
            station.position = root.position + new Vector3(-.2f, 0f, -.85f);
            review.Targets = review.Targets.Concat(new[] { new HumanInteractionReview.Target {
                Label = "Sliding cabinet", Task = "E opens or closes. Collect from container plays the optional collection action.",
                Contact = contact, SelectionAnchor = selection, LookAnchor = selection, Hinge = panel,
                OpenOffset = new Vector3(.55f, 0f, 0f), SlideSeconds = .8f, UseSourceHandRotation = true,
                Use = cabinet, Close = cabinetClose, Collect = cabinetCollect } }).ToArray();
            review.Actor.Stations = review.Actor.Stations.Concat(new[] { station }).ToArray();
        }
        var smithing = Definition("Hammer at anvil",
            Phase("Take hammer", "Survival_Build_Crafting_Hammering_Start.fbx"),
            Phase("Hammer", "Survival_Build_Crafting_Hammering_Loop.fbx", wait: true),
            Phase("Put hammer back", "Survival_Build_Crafting_Hammering_End.fbx"));
        var grinding = Definition("Grind ingredients",
            Phase("Prepare mortar", "Survival_Build_Crafting_MortarAndPestle_Enter.FBX"),
            Phase("Grind", "Survival_Build_Crafting_MortarAndPestle_Loop.FBX", wait: true),
            Phase("Finish grinding", "Survival_Build_Crafting_MortarAndPestle_Exit.FBX"));
        ActorInteractionDefinition Cooking(string action, string file) => Definition(action,
            Phase("Kneel at fire", file, 0f, .25f),
            Phase(action, file, .25f, .75f, wait: true),
            Phase("Stand from fire", file, .75f, 1f));
        var stirring = Cooking("Stir cooking pot", "Survival_CampFire_KneelDown_Cooking_StirPot.FBX");
        var roasting = Cooking("Roast meat", "Survival_CampFire_KneelDown_Cooking_Meat.FBX");
        if (!review.Targets.Any(t => t.Use == smithing))
        {
            var material = GameObject.Find("Interaction room floor").GetComponent<Renderer>().sharedMaterial;
            Transform CraftPart(string name, Transform parent, Vector3 position, Vector3 scale, PrimitiveType shape = PrimitiveType.Cube, bool collision = true)
            {
                var part = GameObject.CreatePrimitive(shape); part.name = name;
                Undo.RegisterCreatedObjectUndo(part, "Add crafting fixture");
                part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = material;
                part.GetComponent<Collider>().enabled = collision;
                return part.transform;
            }
            var targets = review.Targets.ToList(); var stations = review.Actor.Stations.ToList();
            void CraftStation(string name, Vector3 position, float height, params ActorInteractionDefinition[] actions)
            {
                var root = new GameObject(name).transform;
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Add crafting station"); root.position = position;
                var contact = Anchor("Work contact", root, new Vector3(0f, height, -.15f));
                var approach = Anchor("Work approach", root, new Vector3(0f, 0f, -.75f));
                var station = new GameObject(name + " visit").transform;
                Undo.RegisterCreatedObjectUndo(station.gameObject, "Add crafting visit"); station.position = approach.position;
                stations.Add(station);
                if (height > .5f)
                {
                    CraftPart("Work surface", root, new Vector3(0f, actions[0] == smithing ? .64f : .83f, actions[0] == smithing ? -.38f : 0f), new Vector3(1f, .12f, actions[0] == smithing ? .3f : .6f));
                    foreach (float x in new[] { -.4f, .4f }) CraftPart("Bench leg", root, new Vector3(x, actions[0] == smithing ? .3f : .4f, actions[0] == smithing ? -.38f : 0f), new Vector3(.12f, actions[0] == smithing ? .6f : .8f, actions[0] == smithing ? .3f : .45f));
                }
                if (actions[0] == smithing)
                {
                    CraftPart("Anvil base", root, new Vector3(.07f, .73f, -.4f), new Vector3(.35f, .12f, .25f));
                    CraftPart("Anvil face", root, new Vector3(.07f, .84f, -.4f), new Vector3(.6f, .1f, .25f));
                }
                else
                {
                    CraftPart(height > .5f ? "Mortar" : "Cooking pot", root, height > .5f ? new Vector3(-.03f, .94f, -.3f) : new Vector3(0f, height - .06f, 0f),
                        new Vector3(.3f, .09f, .3f), PrimitiveType.Cylinder);
                    if (height < .5f)
                        for (int log = 0; log < 3; log++)
                        {
                            var wood = CraftPart("Firewood", root, new Vector3(0f, .05f, 0f), new Vector3(.65f, .08f, .08f));
                            wood.localRotation = Quaternion.Euler(0f, log * 60f, 0f);
                        }
                }
                var configured = new List<HumanInteractionReview.StationAction>();
                for (int index = 0; index < actions.Length; index++)
                {
                    var rest = Anchor("Tool rest " + index, root, new Vector3(.3f + index * .1f, height, -.2f));
                    var tool = new GameObject(actions[index].Action + " tool").transform;
                    Undo.RegisterCreatedObjectUndo(tool.gameObject, "Add station tool"); tool.SetPositionAndRotation(rest.position, rest.rotation);
                    CraftPart("Handle", tool, new Vector3(.1f, 0f, 0f), new Vector3(.25f, .035f, .035f), collision: false);
                    CraftPart(actions[index] == smithing ? "Hammer head" : actions[index] == roasting ? "Meat" : "Tool tip", tool,
                        new Vector3(.24f, 0f, 0f), actions[index] == smithing ? new Vector3(.08f, .15f, .07f) : new Vector3(.09f, .06f, .06f), collision: false);
                    configured.Add(new HumanInteractionReview.StationAction { Definition = actions[index], Tool = tool, ToolRest = rest });
                }
                targets.Add(new HumanInteractionReview.Target { Label = name, Task = "Choose a crafting action. E finishes; Escape cancels.",
                    Contact = contact, SelectionAnchor = contact, LookAnchor = contact, Approach = approach,
                    Use = actions[0], Actions = configured.ToArray(), UseSourceHandRotation = true });
            }
            CraftStation("Smithing bench", new Vector3(-4.5f, 0f, 5.5f), 1.1f, smithing);
            CraftStation("Alchemy bench", new Vector3(0f, 0f, 5.5f), 1f, grinding);
            CraftStation("Cooking fire", new Vector3(4.5f, 0f, 5.5f), .3f, stirring, roasting);
            review.Targets = targets.ToArray(); review.Actor.Stations = stations.ToArray();
        }
        var placeWood = Phase("Place firewood", "Loot_FloorPickUp_Kneel_RightHand_Inspect_Enter.fbx", 0f, .6f, marker: "BuildCampfire");
        placeWood.MarkerProgress = .9f;
        var recoverWood = Phase("Stand from firewood", "Loot_FloorPickUp_Kneel_RightHand_Inspect_Enter.fbx", 0f, .6f);
        recoverWood.ReverseAnimation = true;
        var buildFire = Definition("Build campfire", placeWood, recoverWood);
        var strikeFire = Phase("Strike flint", "Survival_CampFire_KneelDown_StartFire_Flint.FBX", marker: "LightCampfire");
        strikeFire.MarkerProgress = .65f;
        var lightFire = Definition("Light campfire", strikeFire);
        var cookingTarget = review.Targets.First(t => t.Actions.Any(a => a.Definition == stirring));
        if (cookingTarget.Campfire == null)
        {
            var root = cookingTarget.Contact.parent;
            var fire = Undo.AddComponent<CampfireInteraction>(root.gameObject);
            var woodRoot = new GameObject("Built firewood").transform;
            Undo.RegisterCreatedObjectUndo(woodRoot.gameObject, "Add campfire state"); woodRoot.SetParent(root, false);
            foreach (var child in root.Cast<Transform>().Where(t => t.name == "Firewood").ToArray()) child.SetParent(woodRoot, true);
            fire.WoodVisual = woodRoot;
            var flameObject = new GameObject("Campfire flames");
            Undo.RegisterCreatedObjectUndo(flameObject, "Add campfire flames"); flameObject.transform.SetParent(root, false);
            flameObject.transform.localPosition = Vector3.up * .1f;
            fire.Flames = flameObject.AddComponent<ParticleSystem>();
            var main = fire.Flames.main; main.playOnAwake = false; main.loop = true;
            main.startLifetime = .5f; main.startSpeed = .4f; main.startSize = .12f;
            main.startColor = new Color(1f, .3f, .02f, .7f); main.maxParticles = 40;
            main.gravityModifier = -.1f;
            var emission = fire.Flames.emission; emission.rateOverTime = 30f;
            var shape = fire.Flames.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .12f;
            flameObject.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            fire.FireLight = flameObject.AddComponent<Light>(); fire.FireLight.color = new Color(1f, .4f, .1f); fire.FireLight.range = 3f;
            cookingTarget.Campfire = fire;
            cookingTarget.Actions = new[] { new HumanInteractionReview.StationAction { Definition = buildFire },
                new HumanInteractionReview.StationAction { Definition = lightFire } }.Concat(cookingTarget.Actions).ToArray();
            cookingTarget.Use = buildFire;
            cookingTarget.Task = "E builds, then lights the campfire. Choose a cooking action after lighting.";
            fire.ResetState(); EditorUtility.SetDirty(fire);
        }
        ActorInteractionDefinition.Phase FlaskPhase(string name, string file, float first, float last, string contact, string marker = "")
        {
            var phase = Phase(name, file, first, last, right: 1f, marker: marker);
            phase.ContactSet = contact; phase.GripRadius = .055f; phase.MarkerProgress = .95f;
            phase.Animation.BlendSeconds = .3f; phase.Seconds = Mathf.Max(.7f, phase.Seconds);
            return phase;
        }
        var pouring = Definition("Pour reagent",
            FlaskPhase("Reach flask", "Loot_TableTop_RightHand_Inspect_Enter.fbx", 0f, .55f, "FlaskRest", "TakeTool"),
            FlaskPhase("Lift flask", "Loot_TableTop_RightHand_Inspect_Enter.fbx", .55f, 1f, "FlaskLift"),
            FlaskPhase("Tilt flask", "Crafter@Item-Water.FBX", 0f, .2f, "Pour"),
            FlaskPhase("Pour reagent", "Crafter@Item-Water.FBX", .2f, .8f, "Pour", "Pour"),
            FlaskPhase("Return flask", "Loot_TableTop_RightHand_Inspect_Exit.fbx", 0f, .8f, "FlaskRest", "ReturnTool"),
            Phase("Release flask", "Loot_TableTop_RightHand_Inspect_Exit.fbx", .8f, 1f));
        var alchemy = review.Targets.First(t => t.Actions.Any(a => a.Definition == grinding));
        if (!alchemy.ContactSets.Any(c => c.Id == "Pour"))
        {
            var grip = Anchor("Pour grip", alchemy.Contact.parent, new Vector3(.21f, 1.16f, -.08f));
            alchemy.ContactSets = alchemy.ContactSets.Concat(new[] { new HumanInteractionReview.HandContacts { Id = "Pour", Right = grip } }).ToArray();
            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cup.name = "Mixing cup";
            Undo.RegisterCreatedObjectUndo(cup, "Add mixing cup"); cup.transform.SetParent(alchemy.Contact.parent, false);
            cup.transform.localPosition = new Vector3(.21f, .94f, 0f); cup.transform.localScale = new Vector3(.14f, .05f, .14f);
            cup.GetComponent<Renderer>().sharedMaterial = alchemy.Contact.parent.Find("Mortar").GetComponent<Renderer>().sharedMaterial;
        }
        if (!alchemy.Actions.Any(a => a.Definition == pouring))
        {
            var root = alchemy.Contact.parent;
            var rest = Anchor("Flask rest", root, new Vector3(.3f, .95f, -.15f));
            var tool = new GameObject("Reagent flask").transform;
            Undo.RegisterCreatedObjectUndo(tool.gameObject, "Add pouring flask");
            tool.SetPositionAndRotation(rest.position, rest.rotation);
            var material = root.Find("Mortar").GetComponent<Renderer>().sharedMaterial;
            void FlaskPart(string name, Vector3 position, Vector3 scale)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Sphere); part.name = name;
                Undo.RegisterCreatedObjectUndo(part, "Add pouring flask"); part.transform.SetParent(tool, false);
                part.transform.localPosition = position; part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = material; part.GetComponent<Collider>().enabled = false;
            }
            FlaskPart("Flask body", Vector3.zero, Vector3.one * .11f);
            FlaskPart("Flask neck", new Vector3(0f, .075f, 0f), new Vector3(.045f, .08f, .045f));
            var stream = new GameObject("Reagent stream");
            Undo.RegisterCreatedObjectUndo(stream, "Add reagent stream"); stream.transform.SetParent(tool, false);
            stream.transform.localPosition = new Vector3(0f, .12f, 0f);
            var flow = stream.AddComponent<ParticleSystem>();
            var main = flow.main; main.playOnAwake = false; main.loop = true; main.startLifetime = .12f;
            main.startSpeed = 0f; main.startSize = .018f; main.gravityModifier = 1f; main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = new Color(.25f, .7f, 1f, .8f);
            var emission = flow.emission; emission.rateOverTime = 90f;
            var shape = flow.shape; shape.enabled = false;
            stream.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            flow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            alchemy.Actions = alchemy.Actions.Concat(new[] { new HumanInteractionReview.StationAction {
                Definition = pouring, Tool = tool, ToolRest = rest, Flow = flow, FlowPhase = "Pour reagent" } }).ToArray();
        }
        var flaskAction = alchemy.Actions.First(a => a.Definition == pouring);
        if (!alchemy.ContactSets.Any(c => c.Id == "FlaskRest"))
        {
            var root = alchemy.Contact.parent;
            var upright = root.rotation * Quaternion.Euler(41.19f, 214.39f, 233.71f);
            var restGrip = Anchor("Flask rest grip", root, flaskAction.ToolRest.localPosition);
            restGrip.rotation = upright;
            var liftGrip = Anchor("Flask lift grip", root, new Vector3(.21f, 1.15f, -.22f)); liftGrip.rotation = upright;
            var pourGrip = alchemy.ContactSets.First(c => c.Id == "Pour");
            pourGrip.Right.localPosition = new Vector3(.21f, 1.1f, -.1f);
            pourGrip.Right.rotation = root.rotation * Quaternion.Euler(353.13f, 281.48f, 215.68f); pourGrip.MatchRotation = true;
            pourGrip.ToolEuler = (Quaternion.Inverse(pourGrip.Right.rotation) * root.rotation * Quaternion.Euler(115f, 0f, 0f)).eulerAngles;
            alchemy.ContactSets = alchemy.ContactSets.Concat(new[] {
                new HumanInteractionReview.HandContacts { Id = "FlaskRest", Right = restGrip, MatchRotation = true, ToolEuler = (Quaternion.Inverse(upright) * root.rotation).eulerAngles },
                new HumanInteractionReview.HandContacts { Id = "FlaskLift", Right = liftGrip, MatchRotation = true, ToolEuler = (Quaternion.Inverse(upright) * root.rotation).eulerAngles } }).ToArray();
            flaskAction.PickupAtMarker = true; flaskAction.ReturnPhase = "Return flask";
            flaskAction.ToolEuler = Quaternion.Inverse(upright).eulerAngles;
        }
        review.Actor.Interactions = definitions.ToArray();
        HumanCrateMotionAuthor.Apply();
        HumanChestMotionAuthor.FitReviewProp(review);
        foreach (var target in review.Targets.Where(t => t.Use == leverOn)) target.UseSourceHandRotation = true;
        EditorUtility.SetDirty(review); EditorUtility.SetDirty(review.Actor);
        EditorSceneManager.MarkSceneDirty(review.gameObject.scene);
        EditorSceneManager.SaveScene(review.gameObject.scene); AssetDatabase.SaveAssets();
    }
}






