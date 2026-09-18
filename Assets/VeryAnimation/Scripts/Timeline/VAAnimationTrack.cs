#if VERYANIMATION_TIMELINE
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Timeline;
#if UNITY_EDITOR
using UnityEngine.Assertions;
using UnityEditor;
#endif

namespace VeryAnimation
{
    [TrackColor(0.2588f, 0.2745f, 0.447f)]
    public class VAAnimationTrack : AnimationTrack
    {
        public bool blendingAdditive = false;

        /* Ideally, you would want to override it like this and set the blend mode, but this is not possible because it is not public and cannot be inherited.
        internal override Playable CreateMixerPlayableGraph(PlayableGraph graph, GameObject go, IntervalTree<RuntimeElement> tree)
        {
            var playable = base.CreateMixerPlayableGraph(graph, go, tree);
            -> AnimationLayerMixerPlayable.SetLayerAdditive
            return playable;
        }*/

        /*
        The ideal method above requires editing the contents of the Timeline, so I looked for a way to avoid this.
        The inheritable outputs and GatherProperties are used to detect Playable updates, create a temporary object and set it later.
        This is not very elegant, but there was no other way, so I went with this specification.
        outputs is enumerated during graph compilation, but it is not cached on the Timeline side and editor GUI code such as the PlayableDirector inspector also enumerates it repeatedly, so it only reacts while Application.isPlaying.
        GatherProperties covers edit mode instead. It is called when the Timeline window starts a preview and on every graph rebuild, and it is never called while Application.isPlaying.
        If I could find a single way that is reliably called immediately after TimelinePlayable.Compile and also works at runtime, I would like to use it.
        */
        public override IEnumerable<PlayableBinding> outputs
        {
            get
            {
                if (Application.isPlaying)
                    CreateTrackObject();

                return base.outputs;
            }
        }
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            base.GatherProperties(director, driver);

            if (!Application.isPlaying)
                CreateTrackObject();
        }

        private void CreateTrackObject()
        {
            if (!isSubTrack && !mutedInHierarchy && VAAnimationTrackObject.Instance == null)
            {
                var newGo = new GameObject(name);
                newGo.hideFlags |= HideFlags.HideAndDontSave;
                newGo.AddComponent<VAAnimationTrackObject>();
            }
        }

        public static void SetAllSettings()
        {
#if UNITY_6000_4_OR_NEWER
            var playableDirectors = FindObjectsByType<PlayableDirector>(FindObjectsInactive.Exclude);
#else
            var playableDirectors = FindObjectsByType<PlayableDirector>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            foreach (var playableDirector in playableDirectors)
            {
                SetSettings(playableDirector);
            }
        }

        public static void SetSettings(PlayableDirector playableDirector)
        {
            if (playableDirector == null)
                return;
            if (playableDirector.playableAsset is not TimelineAsset)
                return;
            if (!playableDirector.playableGraph.IsValid())
                return;

            var animationPlayableOutputCount = playableDirector.playableGraph.GetOutputCountByType<AnimationPlayableOutput>();
            for (int i = 0; i < animationPlayableOutputCount; i++)
            {
                var animationPlayableOutput = (AnimationPlayableOutput)playableDirector.playableGraph.GetOutputByType<AnimationPlayableOutput>(i);
                var animationPlayableOutputObj = animationPlayableOutput.GetReferenceObject();
                if (animationPlayableOutputObj is not VAAnimationTrack animationTrack)
                    continue;

                var playable = animationPlayableOutput.GetSourcePlayable();
                if (!playable.IsValid())
                    continue;

                playable = playable.GetInput(animationPlayableOutput.GetSourceOutputPort());
                while (playable.GetInputCount() > 0)
                {
                    if (playable.GetPlayableType() != typeof(AnimationLayerMixerPlayable))
                    {
                        playable = playable.GetInput(0);
                        continue;
                    }
                    var mixer = (AnimationLayerMixerPlayable)playable;

                    var childTracks = animationTrack.GetChildTracks();

                    //To avoid using Reflection, I hardcoded CanCompileClips.
                    static bool CanCompileClips(AnimationTrack animationTrack)
                    {
                        return animationTrack != null && !animationTrack.muted && (animationTrack.hasClips || (animationTrack.infiniteClip != null && !animationTrack.infiniteClip.empty));
                    }

                    int flattenTrackCount = CanCompileClips(animationTrack) ? 1 : 0;
                    foreach (var subTrack in childTracks)
                    {
                        if (CanCompileClips(subTrack as AnimationTrack))
                            flattenTrackCount++;
                    }
                    int index = mixer.GetInputCount() - flattenTrackCount;
                    if (index < 0)
                    {
                        Debug.LogError($"<color=blue>[Very Animation]</color>VAAnimationTrack '{animationTrack.name}' could not resolve the layer index. The mixer has {mixer.GetInputCount()} inputs but {flattenTrackCount} tracks are compilable. The PlayableGraph may be out of sync with the track state.", animationTrack);
                        break;
                    }

                    if (CanCompileClips(animationTrack))
                    {
#if UNITY_EDITOR
                        Assert.IsFalse(animationTrack.blendingAdditive);
#endif
                        index++;
                    }
                    foreach (var subTrack in childTracks)
                    {
                        var child = subTrack as AnimationTrack;
                        if (CanCompileClips(child))
                        {
                            if (child is VAAnimationTrack vaAnimTrack)
                            {
#if UNITY_EDITOR
                                AdditiveIssueChecker(playableDirector, vaAnimTrack);
#endif
                                if (index < mixer.GetInputCount())
                                    mixer.SetLayerAdditive((uint)index, vaAnimTrack.blendingAdditive);
                            }
                            index++;
                        }
                    }
                    break;
                }
            }
        }

#if UNITY_EDITOR
        private static readonly string[] rootMotionPropertyNames = new[]
        {
            "RootT.",
            "RootQ.",
            "MotionT.",
            "MotionQ.",
        };
        private static readonly string[][] avatarIkGoalPropertyNames = new[]
        {
            new[] { "LeftFootT.", "LeftFootQ." },
            new[] { "RightFootT.", "RightFootQ." },
            new[] { "LeftHandT.", "LeftHandQ." },
            new[] { "RightHandT.", "RightHandQ." },
        };

        private static void AdditiveIssueChecker(PlayableDirector playableDirector, VAAnimationTrack vaAnimTrack)
        {
            if (!vaAnimTrack.blendingAdditive)
                return;

            static GameObject GetBoundRootGameObject(PlayableDirector director, TrackAsset track)
            {
                while (director != null && track != null)
                {
                    var boundObj = director.GetGenericBinding(track);
                    if (boundObj is Animator anim)
                        return anim.gameObject;
                    if (boundObj is GameObject go)
                        return go;
                    track = track.parent as TrackAsset;
                }
                return null;
            }
            var bindingObject = GetBoundRootGameObject(playableDirector, vaAnimTrack);
            if (bindingObject == null)
                return;

            bool isHuman = false;
            if (bindingObject.TryGetComponent<Animator>(out var animator))
            {
                isHuman = animator.isHuman;
            }

            bool ignoreHumanoidRoot = false;
            bool[] ignoreHumanoidAvatarIKGoals = new bool[(int)(AvatarIKGoal.RightHand - AvatarIKGoal.LeftFoot) + 1];
            Assert.IsTrue(ignoreHumanoidAvatarIKGoals.Length == avatarIkGoalPropertyNames.Length);
            if (isHuman && vaAnimTrack.applyAvatarMask && vaAnimTrack.avatarMask != null)
            {
                ignoreHumanoidRoot = !vaAnimTrack.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root);
                for (int i = 0; i < ignoreHumanoidAvatarIKGoals.Length; i++)
                    ignoreHumanoidAvatarIKGoals[i] = !vaAnimTrack.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK + i);
            }

            List<EditorCurveBinding> humanoidErrorBindings = new();
            List<EditorCurveBinding> transformErrorBindings = new();

            void CheckClip(AnimationClip clip)
            {
                if (clip == null)
                    return;

                humanoidErrorBindings.Clear();
                transformErrorBindings.Clear();

                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in bindings)
                {
                    var rootMotionBinding = binding.type == typeof(Animator) && binding.path == "" &&
                                            rootMotionPropertyNames.Any(s => binding.propertyName.StartsWith(s, StringComparison.Ordinal));
                    if (isHuman)
                    {
                        if (!ignoreHumanoidRoot && rootMotionBinding)
                            humanoidErrorBindings.Add(binding);
                        for (int i = 0; i < ignoreHumanoidAvatarIKGoals.Length; i++)
                        {
                            if (!ignoreHumanoidAvatarIKGoals[i] &&
                                binding.type == typeof(Animator) && binding.path == "" &&
                                avatarIkGoalPropertyNames[i].Any(s => binding.propertyName.StartsWith(s, StringComparison.Ordinal)))
                            {
                                humanoidErrorBindings.Add(binding);
                            }
                        }
                    }
                    else
                    {
                        if (rootMotionBinding)
                            transformErrorBindings.Add(binding);
                    }
                    if (binding.type == typeof(Transform) && binding.path == "")
                        transformErrorBindings.Add(binding);
                }
                if (humanoidErrorBindings.Count > 0)
                {
                    string humanoidErrorBindingNames = string.Join(", ", humanoidErrorBindings.ConvertAll(b => b.propertyName));
                    Debug.LogWarning($"<color=blue>[Very Animation]</color>VAAnimationTrack '{vaAnimTrack.name}' has a clip '{clip.name}' that modifies root motion or IK. This may cause unexpected behavior when using additive blending. Please configure the Avatar Mask with the Humanoid root and IK disabled.\n{humanoidErrorBindingNames}", vaAnimTrack);
                }
                if (transformErrorBindings.Count > 0)
                {
                    string transformErrorBindingNames = string.Join(", ", transformErrorBindings.ConvertAll(b => b.propertyName));
                    Debug.LogWarning($"<color=blue>[Very Animation]</color>VAAnimationTrack '{vaAnimTrack.name}' has a clip '{clip.name}' that modifies the root transform. This may cause unexpected behavior when using additive blending. Please remove the animation curve that modifies the root transform.\n{transformErrorBindingNames}", vaAnimTrack);
                }
            }

            foreach (var clip in vaAnimTrack.GetClips())
            {
                var asset = clip.asset as AnimationPlayableAsset;
                if (asset != null && asset.clip != null)
                    CheckClip(asset.clip);
            }
            CheckClip(vaAnimTrack.infiniteClip);
        }
#endif
    }
}
#endif
