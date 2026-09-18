using System;
using UnityEditor;
using UnityEngine;

/// <summary>Door contact uses the owned native high reach, then releases before the free door swing.</summary>
public static class HumanDoorMotionAuthor
{
    public static ActorInteractionDefinition.Phase[] CreatePhases(string action)
    {
        if (action != "OpenDoor" && action != "CloseDoor") throw new ArgumentException("Choose OpenDoor or CloseDoor.");
        var clip = Array.Find(AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Interactions/Animations/Activate_Wall_ButtonPush.FBX"),
            x => x is AnimationClip && !x.name.StartsWith("__preview__")) as AnimationClip;
        if (clip == null) throw new InvalidOperationException("Import the owned native ButtonPush clip first.");
        ActorInteractionDefinition.Phase Phase(string name, float first, float last, float hand, string marker = "") => new()
        {
            Animation = new ActorAnimationPerformanceLibrary.Phase { Name = name, Clip = clip,
                StartNormalized = first, EndNormalized = last, BlendSeconds = .08f },
            Seconds = (last - first) * clip.length, RightHandWeight = hand,
            Marker = marker, MarkerProgress = .55f, ForwardLeanDegrees = 0f, GripRadius = 0f
        };
        return new[] { Phase("Reach for door panel", 0f, .36f, 1f),
            Phase("Palm contact and push", .36f, .44f, 1f, action == "OpenDoor" ? "Open" : "Close"),
            Phase("Release palm", .44f, 1f, 0f) };
    }

    public static void ConfigureTarget(HumanInteractionReview.Target target)
    {
        var mesh = target.Hinge.GetComponent<MeshFilter>().sharedMesh;
        target.LidVertices = mesh.vertices; target.LidTriangles = mesh.triangles;
        target.HasAuthoredContact = true;
        target.AuthoredContactOffset = new Vector3(-.08f, 1.24f, .572f);
        target.PalmSurfaceOffset = .037f;
        target.MatchHingeToAuthoredHands = true;
        target.AuthoredHingeLever = null;
        Vector3 seed = target.Contact.position;
        seed.y = target.Hinge.GetComponent<Renderer>().bounds.min.y + target.AuthoredContactOffset.y;
        Vector3 forward = target.Contact.forward;
        if (!HumanInteractionReview.TryProjectContactSurface(target, seed - forward * .5f, forward, out var point, out _))
            throw new InvalidOperationException("The authored palm height does not intersect the visible door panel.");
        target.Contact.position = point;
    }
    public static void Apply()
    {
        foreach (string action in new[] { "OpenDoor", "CloseDoor" })
        {
            var definition = AssetDatabase.LoadAssetAtPath<ActorInteractionDefinition>("Assets/Art/Interactions/Definitions/" + action + ".asset");
            if (definition == null) throw new InvalidOperationException("Missing door definition: " + action);
            definition.Phases = CreatePhases(action); EditorUtility.SetDirty(definition);
        }
        var review = UnityEngine.Object.FindFirstObjectByType<HumanInteractionReview>();
        if (review != null)
        {
            foreach (var target in review.Targets) if (target.OpenAwayFromActor) ConfigureTarget(target);
            EditorUtility.SetDirty(review); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(review.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
    }
}



