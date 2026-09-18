using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>Measures authored palm contacts without applying procedural pose corrections.</summary>
public static class HumanInteractionContactAuthor
{
    [MenuItem("Tools/Actors/Human/Bake Authored Interaction Contacts")]
    static void BakeCurrent()
    {
        var review = UnityEngine.Object.FindAnyObjectByType<HumanInteractionReview>();
        if (Bake(review) > 0) EditorSceneManager.MarkSceneDirty(review.gameObject.scene);
    }

    public static int Bake(HumanInteractionReview review)
    {
        if (review == null || review.Actor == null || review.Actor.CharacterPrefab == null)
            throw new ArgumentException("Contact baking requires a review actor and character prefab.", nameof(review));
        if (Application.isPlaying) throw new InvalidOperationException("Bake contacts in Edit Mode.");
        var results = new List<(HumanInteractionReview.Target target, Vector3 offset)>();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject character = null;
        try
        {
            character = UnityEngine.Object.Instantiate(review.Actor.CharacterPrefab);
            SceneManager.MoveGameObjectToScene(character, scene);
            character.hideFlags = HideFlags.HideAndDontSave;
            character.SetActive(true);
            character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = character.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("Contact baking requires a valid Humanoid avatar.");
            if (!animator.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Contact baking requires an active Humanoid hierarchy.");
            animator.enabled = true;
            var scale = character.transform.lossyScale;
            if (!CharacterMath.IsFinite(scale) || scale.x <= 0f || Mathf.Abs(scale.x - scale.y) > .0001f || Mathf.Abs(scale.x - scale.z) > .0001f)
                throw new InvalidOperationException("Contact baking requires positive uniform character scale.");
            var rig = animator.GetComponent<ProceduralRigDefinition>();
            if (rig == null) rig = animator.gameObject.AddComponent<ProceduralRigDefinition>();
            HumanoidRigBinding.Bind(animator, rig);
            foreach (var target in review.Targets)
            {
                if (target == null || target.HasAuthoredContact || target.Use == null || target.Approach != null || target.SeatApproach != null) continue;
                _ = target.Use.Snapshot();
                ActorInteractionDefinition.Phase contact = null;
                foreach (var phase in target.Use.Phases)
                    if (phase.RightHandWeight > 0f || phase.LeftHandWeight > 0f) { contact = phase; break; }
                if (contact == null) continue;
                var clip = contact.Animation.Clip;
                if (clip == null || !clip.isHumanMotion || !float.IsFinite(clip.length) || clip.length <= 0f)
                    throw new InvalidOperationException("Contact phase requires a Humanoid clip: " + target.Label);
                float progress = string.IsNullOrEmpty(contact.Marker) ? 1f : contact.MarkerProgress;
                if (contact.ReverseAnimation) progress = 1f - progress;
                float normalized = Mathf.Lerp(contact.Animation.StartNormalized, contact.Animation.EndNormalized, progress);
                using (var graph = new ActorAnimationGraph(animator, "Authored interaction contact", 1))
                {
                    var playable = graph.AddBaseClip(0, clip);
                    playable.SetSpeed(0d);
                    graph.BaseMixer.SetInputWeight(0, 1f);
                    playable.SetTime(Math.Min(normalized * clip.length, Math.Max(0d, clip.length - .00001d)));
                    graph.Evaluate();
                    var hand = rig.Interactions[contact.RightHandWeight > 0f ? 1 : 0];
                    var palm = hand.Bones[^1].TransformPoint(hand.ContactPosition);
                    // Preserve prefab scale; the runtime offset belongs to its parent actor frame.
                    var offset = Quaternion.Inverse(character.transform.rotation) * (palm - character.transform.position);
                    if (!CharacterMath.IsFinite(offset)) throw new InvalidOperationException("Invalid sampled contact: " + target.Label);
                    results.Add((target, offset));
                }
            }
            // Commit only after every candidate validates and samples successfully.
            if (results.Count > 0) Undo.RecordObject(review, "Bake authored interaction contacts");
            foreach (var result in results)
            {
                result.target.AuthoredContactOffset = result.offset;
                result.target.HasAuthoredContact = true;
            }
            if (results.Count > 0) EditorUtility.SetDirty(review);
            return results.Count;
        }
        finally
        {
            if (character != null) UnityEngine.Object.DestroyImmediate(character);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
