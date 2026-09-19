using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HumanBookShelfAuthor
{
    const string Definitions = "Assets/Art/Interactions/Definitions/";
    static ActorInteractionDefinition Definition(string name) => AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>(Definitions + name + ".asset");
    static AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Interactions/Animations/" + name + ".fbx")
        .OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));

    public static void RefreshShelfVisuals(InteractionShelf shelf, Material atlas)
    {
        const string woodPath = "Assets/Art/Interactions/RoundOne/BookshelfWood.mat";
        var wood = AssetDatabase.LoadAssetAtPath<Material>(woodPath);
        if (wood == null)
        {
            wood = new Material(atlas) { name = "BookshelfWood" };
            wood.mainTexture = null; wood.color = new Color(.30f, .20f, .11f);
            if (wood.HasProperty("_BaseMap")) wood.SetTexture("_BaseMap", null);
            if (wood.HasProperty("_BaseColor")) wood.SetColor("_BaseColor", wood.color);
            AssetDatabase.CreateAsset(wood, woodPath);
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Interactions/RoundOne/Shelf_01.fbx");
        foreach (Transform child in shelf.transform.Cast<Transform>().ToArray())
        {
            if (child.name == "Shelf board")
            {
                float y = child.localPosition.y;
                Object.DestroyImmediate(child.gameObject);
                var board = Object.Instantiate(model, shelf.transform); board.name = "Authored shelf board";
                board.transform.localPosition = new Vector3(0f, y + .02f, .18f);
                board.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                board.transform.localScale = new Vector3(.875f, 1f, .82f);
                foreach (var renderer in board.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = atlas;
                var bounds = board.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
                var collider = board.AddComponent<BoxCollider>(); collider.center = bounds.center; collider.size = bounds.size;
            }
            else if (child.name == "Authored shelf board")
            {
                child.localPosition = new Vector3(0f, child.localPosition.y, .18f);
                child.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            else if (child.name == "Side panel" || child.name == "Back panel") child.GetComponent<Renderer>().sharedMaterial = wood;
        }
    }

    [MenuItem("Tools/Actors/Human/Add Bookshelf And Portable Items")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode before authoring the bookshelf.");
        var review = Object.FindFirstObjectByType<HumanInteractionReview>();
        if (review == null) throw new System.InvalidOperationException("Open the Human interaction review scene first.");
        foreach (var name in new[] { "GroundPickup", "TablePickup" })
        {
            var d = Definition(name);
            var prefix = name == "GroundPickup" ? "Loot_FloorPickUp_Kneel_RightHand_Inspect_" : "Loot_TableTop_RightHand_Inspect_";
            var exit = Clip(prefix + "Exit");
            var carry = d.Phases.Last();
            carry.Animation.Name = "Carry item"; carry.AllowMovement = carry.UseLocomotion = carry.WaitForInput = true;
            carry.RightHandWeight = carry.LeftHandWeight = 0f;
            var stand = new ActorInteractionDefinition.Phase { Animation = new ActorAnimationPerformanceLibrary.Phase {
                Name = "Stand with item", Clip = exit, StartNormalized = .4f, EndNormalized = 1f, BlendSeconds = .3f }, Seconds = exit.length * .6f };
            d.Phases = new[] { d.Phases[0], d.Phases[1], stand, carry }; EditorUtility.SetDirty(d);
        }
        var groundPlace = Definition("GroundPlace");
        groundPlace.Phases[0].Animation.Clip = Clip("Loot_FloorPickUp_Kneel_RightHand_Inspect_Enter");
        groundPlace.Phases[0].Animation.EndNormalized = .55f;
        groundPlace.Phases[0].Seconds = groundPlace.Phases[0].Animation.Clip.length * .55f;
        groundPlace.Phases[0].LeftHandWeight = 0f;
        groundPlace.Phases[1].Animation.StartNormalized = .4f;
        groundPlace.Phases[1].Seconds = groundPlace.Phases[1].Animation.Clip.length * .6f;
        EditorUtility.SetDirty(groundPlace);

        var shelf = Object.FindFirstObjectByType<InteractionShelf>();
        if (shelf == null)
        {
            var root = new GameObject("Bookshelf"); Undo.RegisterCreatedObjectUndo(root, "Add bookshelf");
            root.transform.SetPositionAndRotation(new Vector3(-5.4f, 0f, -.3f), Quaternion.Euler(0f, -90f, 0f));
            shelf = root.AddComponent<InteractionShelf>();
            var material = review.Targets[7].PickupRoot.GetComponentInChildren<Renderer>().sharedMaterial;
            void Board(string name, Vector3 position, Vector3 size)
            {
                var board = GameObject.CreatePrimitive(PrimitiveType.Cube); board.name = name;
                board.transform.SetParent(root.transform, false); board.transform.localPosition = position;
                board.transform.localScale = size; board.GetComponent<Renderer>().sharedMaterial = material;
            }
            foreach (float y in new[] { .12f, .8f, 1.5f }) Board("Shelf board", new Vector3(0f, y, 0f), new Vector3(1.4f, .04f, .36f));
            foreach (float x in new[] { -.7f, .7f }) Board("Side panel", new Vector3(x, .8f, 0f), new Vector3(.04f, 1.6f, .36f));
            Board("Back panel", new Vector3(0f, .8f, .18f), new Vector3(1.4f, 1.6f, .03f));
            shelf.Slots = new Transform[6];
            for (int i = 0; i < shelf.Slots.Length; i++)
            {
                var slot = new GameObject("Book slot " + (i + 1)).transform; slot.SetParent(root.transform, false);
                slot.localPosition = new Vector3(-.43f + i * .14f, .97f, -.03f);
                slot.localRotation = Quaternion.Euler(0f, 180f, -90f); shelf.Slots[i] = slot;
            }
            var targets = review.Targets.ToList(); var stations = review.Actor.Stations.ToList();
            for (int i = 0; i < 3; i++)
            {
                var source = review.Targets[6]; var book = Object.Instantiate(source.PickupRoot.gameObject);
                book.name = "Ground book " + (i + 1); book.transform.position = new Vector3(-4.3f + i * .48f, 0f, -.8f);
                var contact = book.transform.Find(source.Contact.name);
                var target = new HumanInteractionReview.Target { Label = book.name, Task = "Pick up and shelve this book.",
                    PickupRoot = book.transform, Contact = contact, Use = Definition("GroundPickup"), PutDown = groundPlace,
                    FollowAnimatedHand = true, Shelf = shelf, HasAuthoredContact = source.HasAuthoredContact,
                    AuthoredContactOffset = source.AuthoredContactOffset, MaxContactCorrection = source.MaxContactCorrection };
                targets.Add(target);
                var station = new GameObject(book.name + " approach").transform;
                station.position = book.transform.position - Vector3.forward * .75f; stations.Add(station);
            }
            review.Targets = targets.ToArray(); review.Actor.Stations = stations.ToArray();
        }
        for (int i = 0; i < review.Targets.Length; i++)
        {
            var t = review.Targets[i];
            if ((i >= 2 && i <= 6) || t.Shelf != null)
            {
                t.GroundUse = Definition("GroundPickup"); t.TableUse = Definition("TablePickup");
                t.GroundPutDown = groundPlace; t.TablePutDown = Definition("TablePlace");
            }
        }
        review.Targets[4].Shelf = review.Targets[6].Shelf = shelf;
        foreach (var slot in shelf.Slots) slot.localRotation = Quaternion.Euler(0f, 180f, -90f);
        RefreshShelfVisuals(shelf, review.Targets[7].PickupRoot.GetComponentInChildren<Renderer>().sharedMaterial);
        EditorUtility.SetDirty(review); EditorUtility.SetDirty(review.Actor); EditorUtility.SetDirty(shelf);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(review.gameObject.scene); EditorSceneManager.SaveScene(review.gameObject.scene);
    }
}
