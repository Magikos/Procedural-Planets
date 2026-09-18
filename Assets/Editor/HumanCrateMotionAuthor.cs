using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Fits the pickup grip to the authored lowest hand contact, preserving the native lift timing.</summary>
public static class HumanCrateMotionAuthor
{
    public const string SourceRigPath = "Assets/Art/Interactions/Animations/HumanM Source Model.fbx";

    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring the crate.");
        var review = UnityEngine.Object.FindAnyObjectByType<HumanInteractionReview>();
        var target = review.Targets.First(t => t.Use != null && t.Use.Action == "TwoHandCarry");
        var phases = target.Use.Phases;
        const float grip = .35f;
        phases[0].Animation.StartNormalized = 0f; phases[0].Animation.EndNormalized = grip;
        phases[0].Animation.BlendSeconds = .12f;
        phases[0].Seconds = phases[0].Animation.Clip.length * grip;
        phases[1].Animation.StartNormalized = grip; phases[1].Animation.EndNormalized = 1f;
        phases[1].Animation.BlendSeconds = .05f;
        phases[1].Seconds = phases[1].Animation.Clip.length * (1f - grip);
        phases[1].MarkerProgress = 0f;
        phases[1].RightHandWeight = phases[1].LeftHandWeight = 0f;
        target.Use.Snapshot(); EditorUtility.SetDirty(target.Use); AssetDatabase.SaveAssetIfDirty(target.Use);
        using var reference = new AuthoredAnimationReference(review.Actor.CharacterPrefab, phases[0].Animation.Clip, false);
        reference.Root.transform.GetChild(0).localScale = Vector3.one;
        reference.Sample(phases[0].Animation.Clip.length * grip);
        var rig = reference.Root.AddComponent<ProceduralRigDefinition>();
        HumanoidRigBinding.Bind(reference.Animator, rig);
        Vector3 Palm(string id)
        {
            var hand = rig.Interactions.First(h => h.Id == id);
            return reference.Root.transform.InverseTransformPoint(hand.Bones[^1].TransformPoint(hand.ContactPosition));
        }
        Vector3 right = Palm("RightHand");
        reference.Sample(phases[0].Animation.Clip.length - .00001f);
        float halfGrip = Mathf.Abs(Palm("RightHand").x - Palm("LeftHand").x) * .5f;
        var points = target.PickupRoot.GetComponentsInChildren<MeshFilter>().SelectMany(m => m.sharedMesh.vertices.Select(v => target.PickupRoot.InverseTransformPoint(m.transform.TransformPoint(v)))).ToArray();
        float front = points.Min(v => v.z);
        target.Contact.localPosition = new Vector3(halfGrip, target.Contact.localPosition.y, front);
        target.LeftContact.localPosition = new Vector3(-halfGrip, target.LeftContact.localPosition.y, front);
        target.AuthoredContactOffset = right; target.HasAuthoredContact = true;
        target.FollowAnimatedHand = false;
        EditorUtility.SetDirty(review);
        EditorSceneManager.MarkSceneDirty(review.gameObject.scene);
        EditorSceneManager.SaveScene(review.gameObject.scene);
    }
}
