using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VeryAnimation
{
    internal partial class VeryAnimation
    {
        private const float TransformPositionApproximatelyThreshold = 0.001f;
        private const float TransformRotationApproximatelyThreshold = 0.01f;
        private const float TransformScaleApproximatelyThreshold = 0.1f;

        private static readonly int[] QuaternionXMirrorSwapDof = new int[] { 2, 3, 0, 1 };
        public enum AnimatorIKIndex
        {
            None = -1,
            LeftHand,
            RightHand,
            LeftFoot,
            RightFoot,
            Total
        }
        public static readonly AnimatorIKIndex[] AnimatorIKMirrorIndexes =
        {
            AnimatorIKIndex.RightHand,
            AnimatorIKIndex.LeftHand,
            AnimatorIKIndex.RightFoot,
            AnimatorIKIndex.LeftFoot,
        };
        public static readonly HumanBodyBones[] AnimatorIKIndex2HumanBodyBones =
        {
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
        };
        public enum AnimatorTDOFIndex
        {
            None = -1,
            LeftUpperLeg,
            RightUpperLeg,
            Spine,
            Chest,
            Neck,
            LeftShoulder,
            RightShoulder,
            UpperChest,
            LeftLowerLeg,
            RightLowerLeg,
            LeftFoot,
            RightFoot,
            Head,
            LeftUpperArm,
            RightUpperArm,
            LeftLowerArm,
            RightLowerArm,
            LeftHand,
            RightHand,
            LeftToes,
            RightToes,
            Total
        }
        public static readonly string[] AnimatorTDOFIndexStrings =
        {
            nameof(AnimatorTDOFIndex.LeftUpperLeg),
            nameof(AnimatorTDOFIndex.RightUpperLeg),
            nameof(AnimatorTDOFIndex.Spine),
            nameof(AnimatorTDOFIndex.Chest),
            nameof(AnimatorTDOFIndex.Neck),
            nameof(AnimatorTDOFIndex.LeftShoulder),
            nameof(AnimatorTDOFIndex.RightShoulder),
            nameof(AnimatorTDOFIndex.UpperChest),
            nameof(AnimatorTDOFIndex.LeftLowerLeg),
            nameof(AnimatorTDOFIndex.RightLowerLeg),
            nameof(AnimatorTDOFIndex.LeftFoot),
            nameof(AnimatorTDOFIndex.RightFoot),
            nameof(AnimatorTDOFIndex.Head),
            nameof(AnimatorTDOFIndex.LeftUpperArm),
            nameof(AnimatorTDOFIndex.RightUpperArm),
            nameof(AnimatorTDOFIndex.LeftLowerArm),
            nameof(AnimatorTDOFIndex.RightLowerArm),
            nameof(AnimatorTDOFIndex.LeftHand),
            nameof(AnimatorTDOFIndex.RightHand),
            nameof(AnimatorTDOFIndex.LeftToes),
            nameof(AnimatorTDOFIndex.RightToes),
        };
        public static readonly AnimatorTDOFIndex[] AnimatorTDOFMirrorIndexes =
        {
            AnimatorTDOFIndex.RightUpperLeg,
            AnimatorTDOFIndex.LeftUpperLeg,
            AnimatorTDOFIndex.None,
            AnimatorTDOFIndex.None,
            AnimatorTDOFIndex.None,
            AnimatorTDOFIndex.RightShoulder,
            AnimatorTDOFIndex.LeftShoulder,
            AnimatorTDOFIndex.None,
            AnimatorTDOFIndex.RightLowerLeg,
            AnimatorTDOFIndex.LeftLowerLeg,
            AnimatorTDOFIndex.RightFoot,
            AnimatorTDOFIndex.LeftFoot,
            AnimatorTDOFIndex.None,
            AnimatorTDOFIndex.RightUpperArm,
            AnimatorTDOFIndex.LeftUpperArm,
            AnimatorTDOFIndex.RightLowerArm,
            AnimatorTDOFIndex.LeftLowerArm,
            AnimatorTDOFIndex.RightHand,
            AnimatorTDOFIndex.LeftHand,
            AnimatorTDOFIndex.RightToes,
            AnimatorTDOFIndex.LeftToes,
        };
        public static readonly HumanBodyBones[] AnimatorTDOFIndex2HumanBodyBones =
        {
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.Neck,
            HumanBodyBones.LeftShoulder,
            HumanBodyBones.RightShoulder,
            HumanBodyBones.UpperChest,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftToes,
            HumanBodyBones.RightToes,
        };

        public static readonly HumanBodyBones[] HumanBodyMirrorBones =
        {
            (HumanBodyBones)(-1),           //Hips = 0,
            HumanBodyBones.RightUpperLeg,   //LeftUpperLeg = 1,
            HumanBodyBones.LeftUpperLeg,    //RightUpperLeg = 2,
            HumanBodyBones.RightLowerLeg,   //LeftLowerLeg = 3,
            HumanBodyBones.LeftLowerLeg,    //RightLowerLeg = 4,
            HumanBodyBones.RightFoot,       //LeftFoot = 5,
            HumanBodyBones.LeftFoot,        //RightFoot = 6,
            (HumanBodyBones)(-1),           //Spine = 7,
            (HumanBodyBones)(-1),           //Chest = 8,
            (HumanBodyBones)(-1),           //Neck = 9,
            (HumanBodyBones)(-1),           //Head = 10,
            HumanBodyBones.RightShoulder,   //LeftShoulder = 11,
            HumanBodyBones.LeftShoulder,    //RightShoulder = 12,
            HumanBodyBones.RightUpperArm,   //LeftUpperArm = 13,
            HumanBodyBones.LeftUpperArm,    //RightUpperArm = 14,
            HumanBodyBones.RightLowerArm,   //LeftLowerArm = 15,
            HumanBodyBones.LeftLowerArm,    //RightLowerArm = 16,
            HumanBodyBones.RightHand,       //LeftHand = 17,
            HumanBodyBones.LeftHand,        //RightHand = 18,
            HumanBodyBones.RightToes,       //LeftToes = 19,
            HumanBodyBones.LeftToes,        //RightToes = 20,
            HumanBodyBones.RightEye,        //LeftEye = 21,
            HumanBodyBones.LeftEye,         //RightEye = 22,
            (HumanBodyBones)(-1),           //Jaw = 23,
            HumanBodyBones.RightThumbProximal,      //LeftThumbProximal = 24,
            HumanBodyBones.RightThumbIntermediate,  //LeftThumbIntermediate = 25,
            HumanBodyBones.RightThumbDistal,        //LeftThumbDistal = 26,
            HumanBodyBones.RightIndexProximal,      //LeftIndexProximal = 27,
            HumanBodyBones.RightIndexIntermediate,  //LeftIndexIntermediate = 28,
            HumanBodyBones.RightIndexDistal,        //LeftIndexDistal = 29,
            HumanBodyBones.RightMiddleProximal,     //LeftMiddleProximal = 30,
            HumanBodyBones.RightMiddleIntermediate, //LeftMiddleIntermediate = 31,
            HumanBodyBones.RightMiddleDistal,       //LeftMiddleDistal = 32,
            HumanBodyBones.RightRingProximal,       //LeftRingProximal = 33,
            HumanBodyBones.RightRingIntermediate,   //LeftRingIntermediate = 34,
            HumanBodyBones.RightRingDistal,         //LeftRingDistal = 35,
            HumanBodyBones.RightLittleProximal,     //LeftLittleProximal = 36,
            HumanBodyBones.RightLittleIntermediate, //LeftLittleIntermediate = 37,
            HumanBodyBones.RightLittleDistal,       //LeftLittleDistal = 38,
            HumanBodyBones.LeftThumbProximal,       //RightThumbProximal = 39,
            HumanBodyBones.LeftThumbIntermediate,   //RightThumbIntermediate = 40,
            HumanBodyBones.LeftThumbDistal,         //RightThumbDistal = 41,
            HumanBodyBones.LeftIndexProximal,       //RightIndexProximal = 42,
            HumanBodyBones.LeftIndexIntermediate,   //RightIndexIntermediate = 43,
            HumanBodyBones.LeftIndexDistal,         //RightIndexDistal = 44,
            HumanBodyBones.LeftMiddleProximal,      //RightMiddleProximal = 45,
            HumanBodyBones.LeftMiddleIntermediate,  //RightMiddleIntermediate = 46,
            HumanBodyBones.LeftMiddleDistal,        //RightMiddleDistal = 47,
            HumanBodyBones.LeftRingProximal,        //RightRingProximal = 48,
            HumanBodyBones.LeftRingIntermediate,    //RightRingIntermediate = 49,
            HumanBodyBones.LeftRingDistal,          //RightRingDistal = 50,
            HumanBodyBones.LeftLittleProximal,      //RightLittleProximal = 51,
            HumanBodyBones.LeftLittleIntermediate,  //RightLittleIntermediate = 52,
            HumanBodyBones.LeftLittleDistal,        //RightLittleDistal = 53,
            (HumanBodyBones)(-1),                   //UpperChest = 54,
        };

        public class HumanVirtualBone
        {
            public HumanBodyBones boneA;
            public HumanBodyBones boneB;
            public float leap;
            public Quaternion addRotation = Quaternion.identity;
            public Vector3 limitSign = Vector3.one;
        }
        public static readonly HumanVirtualBone[][] HumanVirtualBones =
        {
            null, //Hips = 0,
            null, //LeftUpperLeg = 1,
            null, //RightUpperLeg = 2,
            null, //LeftLowerLeg = 3,
            null, //RightLowerLeg = 4,
            null, //LeftFoot = 5,
            null, //RightFoot = 6,
            null, //Spine = 7,
            new HumanVirtualBone[] { new() { boneA = HumanBodyBones.Spine, boneB = HumanBodyBones.Head, leap = 0.15f } }, //Chest = 8,
            new HumanVirtualBone[] { new() { boneA = HumanBodyBones.UpperChest, boneB = HumanBodyBones.Head, leap = 0.8f },
                                        new() { boneA = HumanBodyBones.Chest, boneB = HumanBodyBones.Head, leap = 0.8f },
                                        new() { boneA = HumanBodyBones.Spine, boneB = HumanBodyBones.Head, leap = 0.85f } }, //Neck = 9,
            null, //Head = 10,
            new HumanVirtualBone[] { new() { boneA = HumanBodyBones.LeftUpperArm, boneB = HumanBodyBones.RightUpperArm, leap = 0.2f, limitSign = new Vector3(1f, 1f, -1f) } }, //LeftShoulder = 11,
            new HumanVirtualBone[] { new() { boneA = HumanBodyBones.RightUpperArm, boneB = HumanBodyBones.LeftUpperArm, leap = 0.2f } }, //RightShoulder = 12,
            null, //LeftUpperArm = 13,
            null, //RightUpperArm = 14,
            null, //LeftLowerArm = 15,
            null, //RightLowerArm = 16,
            null, //LeftHand = 17,
            null, //RightHand = 18,
            null, //LeftToes = 19,
            null, //RightToes = 20,
            null, //LeftEye = 21,
            null, //RightEye = 22,
            null, //Jaw = 23,
            null, //LeftThumbProximal = 24,
            null, //LeftThumbIntermediate = 25,
            null, //LeftThumbDistal = 26,
            null, //LeftIndexProximal = 27,
            null, //LeftIndexIntermediate = 28,
            null, //LeftIndexDistal = 29,
            null, //LeftMiddleProximal = 30,
            null, //LeftMiddleIntermediate = 31,
            null, //LeftMiddleDistal = 32,
            null, //LeftRingProximal = 33,
            null, //LeftRingIntermediate = 34,
            null, //LeftRingDistal = 35,
            null, //LeftLittleProximal = 36,
            null, //LeftLittleIntermediate = 37,
            null, //LeftLittleDistal = 38,
            null, //RightThumbProximal = 39,
            null, //RightThumbIntermediate = 40,
            null, //RightThumbDistal = 41,
            null, //RightIndexProximal = 42,
            null, //RightIndexIntermediate = 43,
            null, //RightIndexDistal = 44,
            null, //RightMiddleProximal = 45,
            null, //RightMiddleIntermediate = 46,
            null, //RightMiddleDistal = 47,
            null, //RightRingProximal = 48,
            null, //RightRingIntermediate = 49,
            null, //RightRingDistal = 50,
            null, //RightLittleProximal = 51,
            null, //RightLittleIntermediate = 52,
            null, //RightLittleDistal = 53,
            new HumanVirtualBone[] { new() { boneA = HumanBodyBones.Chest, boneB = HumanBodyBones.Head, leap = 0.2f },
                                        new() { boneA = HumanBodyBones.Spine, boneB = HumanBodyBones.Head, leap = 0.3f } }, //UpperChest = 54,
        };

        public class AnimatorTDOF
        {
            public AnimatorTDOFIndex index;
            public HumanBodyBones parent;
            public Vector3 mirror = new(1f, 1f, -1f);
        }
        public static readonly AnimatorTDOF[] HumanBonesAnimatorTDOFIndex =
        {
            null, //Hips = 0,
            new() { index = AnimatorTDOFIndex.LeftUpperLeg, parent = HumanBodyBones.Hips }, //LeftUpperLeg = 1,
            new() { index = AnimatorTDOFIndex.RightUpperLeg, parent = HumanBodyBones.Hips }, //RightUpperLeg = 2,
            new() { index = AnimatorTDOFIndex.LeftLowerLeg, parent = HumanBodyBones.LeftUpperLeg }, //LeftLowerLeg = 3,
            new() { index = AnimatorTDOFIndex.RightLowerLeg, parent = HumanBodyBones.RightUpperLeg }, //RightLowerLeg = 4,
            new() { index = AnimatorTDOFIndex.LeftFoot, parent = HumanBodyBones.LeftLowerLeg }, //LeftFoot = 5,
            new() { index = AnimatorTDOFIndex.RightFoot, parent = HumanBodyBones.RightLowerLeg }, //RightFoot = 6,
            new() { index = AnimatorTDOFIndex.Spine, parent = HumanBodyBones.Hips }, //Spine = 7,
            new() { index = AnimatorTDOFIndex.Chest, parent = HumanBodyBones.Spine }, //Chest = 8,
            new() { index = AnimatorTDOFIndex.Neck, parent = HumanBodyBones.UpperChest }, //Neck = 9,
            new() { index = AnimatorTDOFIndex.Head, parent = HumanBodyBones.Neck }, //Head = 10,
            new() { index = AnimatorTDOFIndex.LeftShoulder, parent = HumanBodyBones.UpperChest }, //LeftShoulder = 11,
            new() { index = AnimatorTDOFIndex.RightShoulder, parent = HumanBodyBones.UpperChest }, //RightShoulder = 12,
            new() { index = AnimatorTDOFIndex.LeftUpperArm, parent = HumanBodyBones.LeftShoulder, mirror = new Vector3(1f, -1f, 1f) }, //LeftUpperArm = 13,
            new() { index = AnimatorTDOFIndex.RightUpperArm, parent = HumanBodyBones.RightShoulder, mirror = new Vector3(1f, -1f, 1f) }, //RightUpperArm = 14,
            new() { index = AnimatorTDOFIndex.LeftLowerArm, parent = HumanBodyBones.LeftUpperArm, mirror = new Vector3(1f, -1f, 1f)  }, //LeftLowerArm = 15,
            new() { index = AnimatorTDOFIndex.RightLowerArm, parent = HumanBodyBones.RightUpperArm, mirror = new Vector3(1f, -1f, 1f)  }, //RightLowerArm = 16,
            new() { index = AnimatorTDOFIndex.LeftHand, parent = HumanBodyBones.LeftLowerArm, mirror = new Vector3(1f, -1f, 1f)  }, //LeftHand = 17,
            new() { index = AnimatorTDOFIndex.RightHand, parent = HumanBodyBones.RightLowerArm, mirror = new Vector3(1f, -1f, 1f)  }, //RightHand = 18,
            new() { index = AnimatorTDOFIndex.LeftToes, parent = HumanBodyBones.LeftFoot }, //LeftToes = 19,
            new() { index = AnimatorTDOFIndex.RightToes, parent = HumanBodyBones.RightFoot }, //RightToes = 20,
            null, //LeftEye = 21,
            null, //RightEye = 22,
            null, //Jaw = 23,
            null, //LeftThumbProximal = 24,
            null, //LeftThumbIntermediate = 25,
            null, //LeftThumbDistal = 26,
            null, //LeftIndexProximal = 27,
            null, //LeftIndexIntermediate = 28,
            null, //LeftIndexDistal = 29,
            null, //LeftMiddleProximal = 30,
            null, //LeftMiddleIntermediate = 31,
            null, //LeftMiddleDistal = 32,
            null, //LeftRingProximal = 33,
            null, //LeftRingIntermediate = 34,
            null, //LeftRingDistal = 35,
            null, //LeftLittleProximal = 36,
            null, //LeftLittleIntermediate = 37,
            null, //LeftLittleDistal = 38,
            null, //RightThumbProximal = 39,
            null, //RightThumbIntermediate = 40,
            null, //RightThumbDistal = 41,
            null, //RightIndexProximal = 42,
            null, //RightIndexIntermediate = 43,
            null, //RightIndexDistal = 44,
            null, //RightMiddleProximal = 45,
            null, //RightMiddleIntermediate = 46,
            null, //RightMiddleDistal = 47,
            null, //RightRingProximal = 48,
            null, //RightRingIntermediate = 49,
            null, //RightRingDistal = 50,
            null, //RightLittleProximal = 51,
            null, //RightLittleIntermediate = 52,
            null, //RightLittleDistal = 53,
            new() { index = AnimatorTDOFIndex.UpperChest, parent = HumanBodyBones.Chest }, //UpperChest = 54,
        };

        public static readonly HumanBodyBones[] HumanPoseHaveMassBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.UpperChest,
            HumanBodyBones.Neck,
            HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder,
            HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftToes,
            HumanBodyBones.RightToes,
            HumanBodyBones.LeftEye,
            HumanBodyBones.RightEye,
            HumanBodyBones.Jaw,
        };

        #region Clips
        public List<AnimationClip> GetAnimationClips()
        {
            if (VAW.GameObject != null)
            {
                var clips = AnimationUtility.GetAnimationClips(VAW.GameObject).Distinct().ToList();
                clips.RemoveAll(x => x == null);
                clips.Sort((clipA, clipB) => clipA.name.CompareTo(clipB.name));
                return clips;
            }
            else
            {
                return new List<AnimationClip>();
            }
        }

        public List<AnimationClip> GetLayerAnimationClips(int layerIndex)
        {
            var layerClips = new List<AnimationClip>();
            if (VAW.Animator != null && VAW.Animator.runtimeAnimatorController != null)
            {
                var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
                var owc = VAW.Animator.runtimeAnimatorController as AnimatorOverrideController;
                if (!VAW.Animator.isInitialized)
                    VAW.Animator.Rebind();
                if (VAW.Animator.layerCount > 0 && layerIndex < VAW.Animator.layerCount)
                {
                    void FindStateMachine(AnimatorStateMachine stateMachine)
                    {
                        foreach (var state in stateMachine.states)
                        {
                            void FindMotion(Motion motion)
                            {
                                if (motion != null)
                                {
                                    if (motion is UnityEditor.Animations.BlendTree)
                                    {
                                        var blendTree = motion as UnityEditor.Animations.BlendTree;
                                        for (int i = 0; i < blendTree.children.Length; i++)
                                        {
                                            FindMotion(blendTree.children[i].motion);
                                        }
                                    }
                                    else if (motion is AnimationClip)
                                    {
                                        var clip = motion as AnimationClip;
                                        if (owc != null)
                                            clip = owc[clip];
                                        layerClips.Add(clip);
                                    }
                                    else
                                    {
                                        Debug.LogErrorFormat("<color=blue>[Very Animation]</color>unknown support type {0}", motion);
                                    }
                                }
                            }

                            var motion = ac.GetStateEffectiveMotion(state.state, layerIndex);
                            FindMotion(motion);
                        }
                        foreach (var cstateMachine in stateMachine.stateMachines)
                        {
                            FindStateMachine(cstateMachine.stateMachine);
                        }
                    }

                    var stateMachine = UAnimatorController.FindEffectiveRootStateMachine(ac, layerIndex);
                    FindStateMachine(stateMachine);
                }
            }
            layerClips = layerClips.Distinct().ToList();
            layerClips.RemoveAll(x => x == null);
            layerClips.Sort((clipA, clipB) => clipA.name.CompareTo(clipB.name));
            return layerClips;
        }
        #endregion

        #region Avatar
        public float GetHumanoidAvatarAxisLength(HumanBodyBones humanoidIndex)
        {
            return UAvatar.GetAxisLength(AnimatorAvatar, (int)humanoidIndex);
        }
        public Quaternion GetHumanoidAvatarPreRotation(HumanBodyBones humanoidIndex)
        {
            return UAvatar.GetPreRotation(AnimatorAvatar, (int)humanoidIndex);
        }
        public Quaternion GetHumanoidAvatarPostRotation(HumanBodyBones humanoidIndex)
        {
            return UAvatar.GetPostRotation(AnimatorAvatar, (int)humanoidIndex);
        }
        public Quaternion GetHumanoidAvatarZYPostQ(HumanBodyBones humanoidIndex, Quaternion parentQ, Quaternion q)
        {
            return UAvatar.GetZYPostQ(AnimatorAvatar, (int)humanoidIndex, parentQ, q);
        }
        public Quaternion GetHumanoidAvatarZYRoll(HumanBodyBones humanoidIndex)
        {
            return UAvatar.GetZYRoll(AnimatorAvatar, (int)humanoidIndex, Vector3.zero);
        }
        public Vector3 GetHumanoidAvatarLimitSign(HumanBodyBones humanoidIndex)
        {
            return UAvatar.GetLimitSign(AnimatorAvatar, (int)humanoidIndex);
        }
        public Vector3 GetHumanoidAvatarPreAxis(GameObject[] humanoidBones, HumanBodyBones humanoidIndex, int dof)
        {
            var axis = Vector3.zero;
            axis[dof] = 1f;
            return (humanoidBones[(int)humanoidIndex].transform.parent.rotation * GetHumanoidAvatarPreRotation(humanoidIndex)) * axis * GetHumanoidAvatarLimitSign(humanoidIndex)[dof];
        }
        public Vector3 GetHumanoidAvatarPostAxis(GameObject[] humanoidBones, HumanBodyBones humanoidIndex, int dof)
        {
            var axis = Vector3.zero;
            axis[dof] = 1f;
            return (humanoidBones[(int)humanoidIndex].transform.rotation * GetHumanoidAvatarPostRotation(humanoidIndex)) * axis * GetHumanoidAvatarLimitSign(humanoidIndex)[dof];
        }
        #endregion

        #region WeightUpdateFrame
        private class WeightUpdateFrame
        {
            public WeightUpdateFrame()
            {
                Frames = new Dictionary<int, float>();
            }
            public void Add(int frame, float weight)
            {
                if (Frames.TryGetValue(frame, out float outWeight))
                {
                    if (Mathf.Abs(outWeight) > Mathf.Abs(weight))
                        Frames[frame] = weight;
                }
                else
                {
                    Frames.Add(frame, weight);
                }
            }
            public void Clear()
            {
                Frames.Clear();
            }
            public bool IsEmpty()
            {
                return Frames.Count == 0;
            }

            public Dictionary<int, float> Frames { get; private set; }
        }
        #endregion

        #region AnimatorRootCorrection
        private class AnimatorRootCorrection
        {
            public bool update;
            public bool disable;
            public int[] muscleIndexes;
            public AnimationCurve[] rootTCurves = new AnimationCurve[3];
            public AnimationCurve[] rootQCurves = new AnimationCurve[4];
            public AnimationCurve[] muscleCurves;
            //Save
            [Serializable, System.Diagnostics.DebuggerDisplay("Position({position}), Rotation({rotation})")]
            public struct TransformSave
            {
                public Vector3 position;
                public Quaternion rotation;
            }
            public List<TransformSave> hipSaves = new();
            public List<TransformSave> rootSaves = new();
            public List<float>[] muscleValueSaves;

            public TransformSave[] frameRootSaves;

            public HumanPose humanPose;

            public WeightUpdateFrame updateFrame = new();

            public Vector3 GetRootT(float time) => AnimationCommon.EvaluateVector3(rootTCurves, time);
            public Quaternion GetRootQ(float time) => AnimationCommon.EvaluateQuaternionNormalized(rootQCurves, time);
        }
        private AnimatorRootCorrection updateAnimatorRootCorrection;
        private void InitializeAnimatorRootCorrection()
        {
            if (!IsHuman) return;

            updateAnimatorRootCorrection = new AnimatorRootCorrection();

            {
                var muscles = new List<int>(HumanPoseHaveMassBones.Length * 3);
                for (int i = 0; i < HumanPoseHaveMassBones.Length; i++)
                {
                    for (int dof = 0; dof < 3; dof++)
                    {
                        var muscleIndex = HumanTrait.MuscleFromBone((int)HumanPoseHaveMassBones[i], dof);
                        if (muscleIndex >= 0)
                            muscles.Add(muscleIndex);
                    }
                }
                updateAnimatorRootCorrection.muscleIndexes = muscles.ToArray();
            }
            updateAnimatorRootCorrection.muscleCurves = new AnimationCurve[updateAnimatorRootCorrection.muscleIndexes.Length];
            updateAnimatorRootCorrection.muscleValueSaves = new List<float>[updateAnimatorRootCorrection.muscleIndexes.Length];
            for (int i = 0; i < updateAnimatorRootCorrection.muscleValueSaves.Length; i++)
                updateAnimatorRootCorrection.muscleValueSaves[i] = new List<float>();
            updateAnimatorRootCorrection.humanPose.muscles = new float[HumanTrait.MuscleCount];
        }
        private void ReleaseAnimatorRootCorrection()
        {
            updateAnimatorRootCorrection = null;
        }
        private void EnableAnimatorRootCorrection(AnimationCurve curve, int keyIndex)
        {
            if (!IsHuman) return;
            if (rootCorrectionMode == RootCorrectionMode.Disable) return;
            if (keyIndex < 0 || keyIndex >= curve.length) return;

            var currentTime = curve[keyIndex].time;
            var beforeTime = 0f;
            var afterTime = CurrentClip.length;
            if (rootCorrectionMode == RootCorrectionMode.Full)
            {
                if (keyIndex > 0)
                    beforeTime = curve[keyIndex - 1].time;
                if (keyIndex + 1 < curve.length)
                    afterTime = curve[keyIndex + 1].time;
            }
            EnableAnimatorRootCorrection(currentTime, beforeTime, afterTime);
        }
        private void EnableAnimatorRootCorrection(float currentTime, float beforeTime, float afterTime)
        {
            if (!IsHuman) return;
            if (rootCorrectionMode == RootCorrectionMode.Disable) return;

            updateAnimatorRootCorrection.update = true;

            var currentFrame = GetTimeFrame(currentTime);
            updateAnimatorRootCorrection.updateFrame.Add(currentFrame, 0f);

            if (rootCorrectionMode == RootCorrectionMode.Full)
            {
                var beforeFrame = GetTimeFrame(beforeTime);
                var afterFrame = GetTimeFrame(afterTime);
                updateAnimatorRootCorrection.updateFrame.Add(beforeFrame, 1f);
                updateAnimatorRootCorrection.updateFrame.Add(afterFrame, -1f);
                for (int frame = currentFrame - 1; frame > beforeFrame; frame--)
                {
                    updateAnimatorRootCorrection.updateFrame.Add(frame, 0f);
                }
                for (int frame = currentFrame + 1; frame < afterFrame; frame++)
                {
                    updateAnimatorRootCorrection.updateFrame.Add(frame, 0f);
                }
            }
        }
        private void DisableAnimatorRootCorrection()
        {
            if (!IsHuman) return;
            updateAnimatorRootCorrection.disable = true;
        }
        private void ResetAnimatorRootCorrection()
        {
            if (!IsHuman) return;
            updateAnimatorRootCorrection.update = false;
            updateAnimatorRootCorrection.disable = false;
            updateAnimatorRootCorrection.updateFrame.Clear();
        }
        private void SaveAnimatorRootCorrection(bool forceUpdate)
        {
            if (!IsHuman) return;

            var lastFrame = GetLastFrame();

            #region NotUpdateCheck
            if (!forceUpdate)
            {
                if (!HumanoidHasTDoF)
                {
                    if (updateAnimatorRootCorrection.rootSaves.Count == lastFrame + 1)
                        return;
                }
                else
                {
                    if (updateAnimatorRootCorrection.hipSaves.Count == lastFrame + 1)
                        return;
                }
            }
            #endregion

            ResetAnimatorRootCorrection();

            #region Clear
            {
                updateAnimatorRootCorrection.hipSaves.Clear();
                updateAnimatorRootCorrection.rootSaves.Clear();
                foreach (var saves in updateAnimatorRootCorrection.muscleValueSaves)
                    saves.Clear();
            }
            #endregion

            if (!HumanoidHasTDoF)
            {
                #region Not TDoF
                for (int i = 0; i < 3; i++)
                    updateAnimatorRootCorrection.rootTCurves[i] = GetEditorCurveCache(AnimationCommon.Binding.RootT[i]);
                for (int i = 0; i < 4; i++)
                    updateAnimatorRootCorrection.rootQCurves[i] = GetEditorCurveCache(AnimationCommon.Binding.RootQ[i]);
                for (int i = 0; i < updateAnimatorRootCorrection.muscleIndexes.Length; i++)
                    updateAnimatorRootCorrection.muscleCurves[i] = GetEditorCurveCache(AnimatorMuscleBindings[updateAnimatorRootCorrection.muscleIndexes[i]]);
                for (int frame = 0; frame <= lastFrame; frame++)
                {
                    var time = GetFrameTime(frame);
                    var rootT = updateAnimatorRootCorrection.GetRootT(time);
                    var rootQ = updateAnimatorRootCorrection.GetRootQ(time);
                    updateAnimatorRootCorrection.rootSaves.Add(new AnimatorRootCorrection.TransformSave()
                    {
                        position = rootT,
                        rotation = rootQ,
                    });
                    for (int i = 0; i < updateAnimatorRootCorrection.muscleIndexes.Length; i++)
                    {
                        var curve = updateAnimatorRootCorrection.muscleCurves[i];
                        updateAnimatorRootCorrection.muscleValueSaves[i].Add(curve?.Evaluate(time) ?? 0f);
                    }
                }
                #endregion
            }
            else
            {
                #region Has TDoF
                Skeleton.SetApplyIK(false);
                Skeleton.SetTransformOrigin();
                var tHip = Skeleton.HumanoidHipsTransform;
                for (int frame = 0; frame <= lastFrame; frame++)
                {
                    var time = GetFrameTime(frame);
                    Skeleton.SampleAnimationLegacy(CurrentClip, time);
                    updateAnimatorRootCorrection.hipSaves.Add(new AnimatorRootCorrection.TransformSave()
                    {
                        position = tHip.position,
                        rotation = (tHip.rotation * HumanoidPostHipRotation) * HumanoidPreHipRotationInverse,
                    });
                }
                #endregion
            }
        }
        private void UpdateAnimatorRootCorrection()
        {
            if (IsHuman &&
                rootCorrectionMode != RootCorrectionMode.Disable &&
                !updatePoseFixAnimation &&
                updateAnimatorRootCorrection.update &&
                !updateAnimatorRootCorrection.disable &&
                !updateAnimatorRootCorrection.updateFrame.IsEmpty() &&
                !IsWriteLockBone(RootMotionBoneIndex) &&
                BeginChangeAnimationCurve(CurrentClip, "Change RootCorrection"))
            {
                var lastFrame = GetLastFrame();

                #region Cache
                {
                    for (int i = 0; i < 3; i++)
                    {
                        updateAnimatorRootCorrection.rootTCurves[i] = GetAnimationCurveAnimatorRootT(i);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        updateAnimatorRootCorrection.rootQCurves[i] = GetAnimationCurveAnimatorRootQ(i);
                    }
                    #region FrameRootSaves
                    {
                        if (updateAnimatorRootCorrection.frameRootSaves == null || updateAnimatorRootCorrection.frameRootSaves.Length < lastFrame + 1)
                        {
                            updateAnimatorRootCorrection.frameRootSaves = new AnimatorRootCorrection.TransformSave[(lastFrame + 1) * 2];
                        }
                        foreach (var pair in updateAnimatorRootCorrection.updateFrame.Frames)
                        {
                            var frame = pair.Key;
                            if (frame > lastFrame)
                                frame = lastFrame;
                            var time = GetFrameTime(frame);

                            updateAnimatorRootCorrection.frameRootSaves[frame].position = updateAnimatorRootCorrection.GetRootT(time);
                            updateAnimatorRootCorrection.frameRootSaves[frame].rotation = updateAnimatorRootCorrection.GetRootQ(time);
                        }
                    }
                    #endregion
                }
                #endregion

                if (!HumanoidHasTDoF)
                {
                    #region Not TDoF
                    Skeleton.SetApplyIK(false);
                    Skeleton.SetTransformOrigin();
                    #region Cache
                    {
                        for (int i = 0; i < updateAnimatorRootCorrection.muscleIndexes.Length; i++)
                        {
                            updateAnimatorRootCorrection.muscleCurves[i] = GetEditorCurveCache(AnimatorMuscleBindings[updateAnimatorRootCorrection.muscleIndexes[i]]);
                        }
                    }
                    #endregion
                    foreach (var pair in updateAnimatorRootCorrection.updateFrame.Frames)
                    {
                        var frame = pair.Key;
                        if (frame > lastFrame)
                            continue;
                        var tHip = Skeleton.HumanoidHipsTransform;
                        var time = GetFrameTime(frame);
                        #region Before
                        {
                            var tframe = frame;
                            if (tframe >= updateAnimatorRootCorrection.rootSaves.Count)
                                tframe = updateAnimatorRootCorrection.rootSaves.Count - 1;
                            updateAnimatorRootCorrection.humanPose.bodyPosition = updateAnimatorRootCorrection.rootSaves[tframe].position;
                            updateAnimatorRootCorrection.humanPose.bodyRotation = updateAnimatorRootCorrection.rootSaves[tframe].rotation;
                            for (int i = 0; i < updateAnimatorRootCorrection.muscleIndexes.Length; i++)
                            {
                                var muscleIndex = updateAnimatorRootCorrection.muscleIndexes[i];
                                updateAnimatorRootCorrection.humanPose.muscles[muscleIndex] = updateAnimatorRootCorrection.muscleValueSaves[i][tframe];
                            }
                            Skeleton.HumanPoseHandler.SetHumanPose(ref updateAnimatorRootCorrection.humanPose);
                        }
                        var hipBeforeRot = (tHip.rotation * HumanoidPostHipRotation) * HumanoidPreHipRotationInverse;
                        var hipBeforePos = tHip.position;
                        #endregion
                        #region RootQ
                        Quaternion rootQ;
                        {
                            updateAnimatorRootCorrection.humanPose.bodyPosition = updateAnimatorRootCorrection.frameRootSaves[frame].position;
                            updateAnimatorRootCorrection.humanPose.bodyRotation = updateAnimatorRootCorrection.frameRootSaves[frame].rotation;
                            for (int i = 0; i < updateAnimatorRootCorrection.muscleIndexes.Length; i++)
                            {
                                if (updateAnimatorRootCorrection.muscleCurves[i] == null) continue;
                                var muscleIndex = updateAnimatorRootCorrection.muscleIndexes[i];
                                updateAnimatorRootCorrection.humanPose.muscles[muscleIndex] = updateAnimatorRootCorrection.muscleCurves[i].Evaluate(time);
                            }
                            Skeleton.HumanPoseHandler.SetHumanPose(ref updateAnimatorRootCorrection.humanPose);
                            {
                                var hipNowRot = (tHip.rotation * HumanoidPostHipRotation) * HumanoidPreHipRotationInverse;
                                var offset = hipBeforeRot * Quaternion.Inverse(hipNowRot);
                                rootQ = offset * updateAnimatorRootCorrection.humanPose.bodyRotation;
                                #region FixReverseRotation
                                {
                                    var rot = rootQ * Quaternion.Inverse(updateAnimatorRootCorrection.GetRootQ(time));
                                    if (rot.w < 0f)
                                    {
                                        for (int i = 0; i < 4; i++)
                                            rootQ[i] = -rootQ[i];
                                    }
                                }
                                #endregion
                            }
                        }
                        for (int i = 0; i < 4; i++)
                        {
                            var curve = updateAnimatorRootCorrection.rootQCurves[i];
                            float value = rootQ[i];
                            AnimationCommon.SetKeyframe(curve, time, value);
                        }
                        updateAnimatorRootCorrection.humanPose.bodyRotation = rootQ;
                        Skeleton.HumanPoseHandler.SetHumanPose(ref updateAnimatorRootCorrection.humanPose);
                        #endregion
                        #region RootT
                        Vector3 rootT;
                        {
                            var hipNowPos = tHip.position;
                            var offset = ((hipNowPos - hipBeforePos)) * (1f / Skeleton.Animator.humanScale);
                            rootT = updateAnimatorRootCorrection.humanPose.bodyPosition - offset;
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            var curve = updateAnimatorRootCorrection.rootTCurves[i];
                            float value = rootT[i];
                            AnimationCommon.SetKeyframe(curve, time, value);
                        }
                        #endregion
                    }
                    Skeleton.SetTransformStart();
                    #endregion
                }
                else
                {
                    #region Has TDoF
                    Skeleton.SetApplyIK(false);
                    Skeleton.SetTransformOrigin();
                    foreach (var pair in updateAnimatorRootCorrection.updateFrame.Frames)
                    {
                        var frame = pair.Key;
                        if (frame > lastFrame)
                            continue;
                        var tHip = Skeleton.HumanoidHipsTransform;
                        var time = GetFrameTime(frame);
                        Skeleton.SampleAnimationLegacy(CurrentClip, time);
                        {
                            #region Before
                            Vector3 hipBeforePos;
                            Quaternion hipBeforeRot;
                            {
                                var tframe = frame;
                                if (tframe >= updateAnimatorRootCorrection.hipSaves.Count)
                                    tframe = updateAnimatorRootCorrection.hipSaves.Count - 1;
                                hipBeforePos = updateAnimatorRootCorrection.hipSaves[tframe].position;
                                hipBeforeRot = updateAnimatorRootCorrection.hipSaves[tframe].rotation;
                            }
                            #endregion
                            var hipNowPos = tHip.position;
                            var hipNowRot = (tHip.rotation * HumanoidPostHipRotation) * HumanoidPreHipRotationInverse;
                            #region RootQ
                            Quaternion rootQ;
                            Quaternion rotationOffset;
                            {
                                rotationOffset = hipBeforeRot * Quaternion.Inverse(hipNowRot);
                                updateAnimatorRootCorrection.humanPose.bodyRotation = updateAnimatorRootCorrection.frameRootSaves[frame].rotation;
                                rootQ = rotationOffset * updateAnimatorRootCorrection.humanPose.bodyRotation;
                                #region FixReverseRotation
                                {
                                    var rot = rootQ * Quaternion.Inverse(updateAnimatorRootCorrection.GetRootQ(time));
                                    if (rot.w < 0f)
                                    {
                                        for (int i = 0; i < 4; i++)
                                            rootQ[i] = -rootQ[i];
                                    }
                                }
                                #endregion
                            }
                            for (int i = 0; i < 4; i++)
                            {
                                var curve = updateAnimatorRootCorrection.rootQCurves[i];
                                float value = rootQ[i];
                                AnimationCommon.SetKeyframe(curve, time, value);
                            }
                            #endregion
                            #region RootT
                            Vector3 rootT;
                            {
                                updateAnimatorRootCorrection.humanPose.bodyPosition = updateAnimatorRootCorrection.frameRootSaves[frame].position;
                                var bodyPosition = updateAnimatorRootCorrection.humanPose.bodyPosition * Skeleton.Animator.humanScale;
                                var worldRootPosition = Skeleton.GameObject.transform.localToWorldMatrix.MultiplyPoint3x4(bodyPosition);
                                hipNowPos = worldRootPosition + rotationOffset * (hipNowPos - worldRootPosition);
                                var offset = ((hipNowPos - hipBeforePos)) * (1f / Skeleton.Animator.humanScale);
                                rootT = updateAnimatorRootCorrection.humanPose.bodyPosition - offset;
                            }
                            for (int i = 0; i < 3; i++)
                            {
                                var curve = updateAnimatorRootCorrection.rootTCurves[i];
                                float value = rootT[i];
                                AnimationCommon.SetKeyframe(curve, time, value);
                            }
                            #endregion
                        }
                    }
                    Skeleton.SetTransformStart();
                    #endregion
                }

                #region SmoothTangent
                {
                    foreach (var pair in updateAnimatorRootCorrection.updateFrame.Frames)
                    {
                        var frame = pair.Key;
                        var weight = pair.Value;
                        var time = GetFrameTime(frame);
                        for (int i = 0; i < 4; i++)
                        {
                            var keyIndex = AnimationCommon.FindKeyframeAtTime(updateAnimatorRootCorrection.rootQCurves[i], time);
                            if (keyIndex >= 0)
                                updateAnimatorRootCorrection.rootQCurves[i].SmoothTangents(keyIndex, weight);
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            var keyIndex = AnimationCommon.FindKeyframeAtTime(updateAnimatorRootCorrection.rootTCurves[i], time);
                            if (keyIndex >= 0)
                                updateAnimatorRootCorrection.rootTCurves[i].SmoothTangents(keyIndex, weight);
                        }
                        AddHumanoidFootIK(time, weight);
                    }
                }
                #endregion

                #region Write
                {
                    for (int i = 0; i < 4; i++)
                    {
                        SetAnimationCurveAnimatorRootQ(i, updateAnimatorRootCorrection.rootQCurves[i]);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        SetAnimationCurveAnimatorRootT(i, updateAnimatorRootCorrection.rootTCurves[i]);
                    }
                }
                #endregion
            }
        }
        public bool ForceUpdateCurrentFrameAnimatorRootCorrectionImmediate()
        {
            if (!IsHuman) return false;
            if (rootCorrectionMode == RootCorrectionMode.Disable)
                return false;

            var saveUpdateAnimatorRootCorrection_update = updateAnimatorRootCorrection.update;
            var saveUpdateAnimatorRootCorrection_updateFrame = updateAnimatorRootCorrection.updateFrame;
            try
            {
                updateAnimatorRootCorrection.update = true;
                updateAnimatorRootCorrection.updateFrame = new WeightUpdateFrame();
                updateAnimatorRootCorrection.updateFrame.Add(UAw.GetCurrentFrame(), 0f);

                UpdateAnimatorRootCorrection();
            }
            finally
            {
                updateAnimatorRootCorrection.update = saveUpdateAnimatorRootCorrection_update;
                updateAnimatorRootCorrection.updateFrame = saveUpdateAnimatorRootCorrection_updateFrame;
            }

            return true;
        }
        private bool IsAnimatorRootCorrectionBone(HumanBodyBones humanoidIndex)
        {
            return ((humanoidIndex >= HumanBodyBones.Hips && humanoidIndex <= HumanBodyBones.Jaw) ||
                    humanoidIndex == HumanBodyBones.UpperChest);
        }
        #endregion

        #region FootIK
        private class HumanoidFootIK
        {
            public class IkCurves
            {
                public AnimationCurve[] ikT = new AnimationCurve[3];
                public AnimationCurve[] ikQ = new AnimationCurve[4];
            }
            public IkCurves[] ikCurves;

            public WeightUpdateFrame updateFrame = new();

            public HumanoidFootIK()
            {
                ikCurves = new IkCurves[AnimatorIKIndex.RightFoot - AnimatorIKIndex.LeftFoot + 1];
                for (int i = 0; i < ikCurves.Length; i++)
                {
                    ikCurves[i] = new IkCurves();
                }
            }

            public void Clear()
            {
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < ikCurves.Length; j++)
                        ikCurves[j].ikT[i] = null;
                }
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < ikCurves.Length; j++)
                        ikCurves[j].ikQ[i] = null;
                }
                updateFrame.Clear();
            }
        }
        private HumanoidFootIK humanoidFootIK;

        private void InitializeHumanoidFootIK()
        {
            if (!IsHuman) return;
            humanoidFootIK = new HumanoidFootIK();
        }
        private void ReleaseHumanoidFootIK()
        {
            humanoidFootIK = null;
        }
        public bool IsEnableUpdateHumanoidFootIK()
        {
            if (!IsHuman)
                return false;

#if VERYANIMATION_TIMELINE
            if (UAw.GetLinkedWithTimeline())
                return UAw.GetTimelineApplyFootIK();
#endif
            return (optionsAutoFootIK);
        }
        private void AddHumanoidFootIK(float time, float weight = 0f)
        {
            if (!IsHuman) return;
            if (time < 0f || time > CurrentClip.length) return;

            var frame = GetTimeFrame(time);
            humanoidFootIK.updateFrame.Add(frame, weight);
        }
        private bool UpdateHumanoidFootIK()
        {
            if (!IsHuman)
                return false;

            bool update = false;
            if (IsEnableUpdateHumanoidFootIK() &&
                !humanoidFootIK.updateFrame.IsEmpty())
            {
                var lastFrame = GetLastFrame();
                #region Tmp
                for (var ikIndex = AnimatorIKIndex.LeftFoot; ikIndex <= AnimatorIKIndex.RightFoot; ikIndex++)
                {
                    int index = ikIndex - AnimatorIKIndex.LeftFoot;
                    for (int i = 0; i < 3; i++)
                    {
                        humanoidFootIK.ikCurves[index].ikT[i] = GetAnimationCurveAnimatorIkT(ikIndex, i);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        humanoidFootIK.ikCurves[index].ikQ[i] = GetAnimationCurveAnimatorIkQ(ikIndex, i);
                    }
                }
                #endregion
                #region Set
                {
                    Skeleton.SetApplyIK(false);
                    Skeleton.SetTransformStart();
                    var localToWorldRotation = TransformPoseSave.StartRotation;
                    var worldToLocalMatrix = TransformPoseSave.StartMatrix.inverse;
                    var humanScale = Skeleton.Animator.humanScale;
                    var leftFeetBottomHeight = Skeleton.Animator.leftFeetBottomHeight;
                    var rightFeetBottomHeight = Skeleton.Animator.rightFeetBottomHeight;
                    var postLeftFoot = GetHumanoidAvatarPostRotation(HumanBodyBones.LeftFoot);
                    var postRightFoot = GetHumanoidAvatarPostRotation(HumanBodyBones.RightFoot);
                    foreach (var pair in humanoidFootIK.updateFrame.Frames)
                    {
                        var frame = pair.Key;
                        if (frame > lastFrame)
                            frame = lastFrame;
                        var time = GetFrameTime(frame);
                        Skeleton.SampleAnimation(CurrentClip, time);
                        var rootT = GetAnimationValueAnimatorRootT(time);
                        var rootQ = GetAnimationValueAnimatorRootQ(time);
                        for (var ikIndex = AnimatorIKIndex.LeftFoot; ikIndex <= AnimatorIKIndex.RightFoot; ikIndex++)
                        {
                            var humanoidIndex = AnimatorIKIndex2HumanBodyBones[(int)ikIndex];
                            if (IsWriteLockBone(humanoidIndex))
                                continue;
                            var t = Skeleton.HumanoidBones[(int)humanoidIndex].transform;
                            t.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                            Vector3 ikGoalPosition = position;
                            Quaternion ikGoalRotation = rotation;
                            {
                                {
                                    Quaternion postRotation = Quaternion.identity;
                                    switch (ikIndex)
                                    {
                                        case AnimatorIKIndex.LeftFoot: postRotation = postLeftFoot; break;
                                        case AnimatorIKIndex.RightFoot: postRotation = postRightFoot; break;
                                    }
                                    ikGoalRotation *= postRotation;
                                }
                                if (ikIndex == AnimatorIKIndex.LeftFoot || ikIndex == AnimatorIKIndex.RightFoot)
                                {
                                    Vector3 footBottom = new(ikIndex == AnimatorIKIndex.LeftFoot ? leftFeetBottomHeight : rightFeetBottomHeight, 0, 0);
                                    ikGoalPosition += ikGoalRotation * footBottom;
                                }
                                ikGoalPosition = worldToLocalMatrix.MultiplyPoint3x4(ikGoalPosition);
                                ikGoalRotation = Quaternion.Inverse(localToWorldRotation) * ikGoalRotation;
                                (ikGoalPosition, ikGoalRotation) = AnimationCommon.CalcAvatarIKGoal(ikGoalPosition, ikGoalRotation, rootT, rootQ, humanScale);
                            }
                            int index = ikIndex - AnimatorIKIndex.LeftFoot;
                            for (int i = 0; i < 3; i++)
                                AnimationCommon.SetKeyframe(humanoidFootIK.ikCurves[index].ikT[i], time, ikGoalPosition[i]);
                            for (int i = 0; i < 4; i++)
                                AnimationCommon.SetKeyframe(humanoidFootIK.ikCurves[index].ikQ[i], time, ikGoalRotation[i]);
                        }
                    }
                    Skeleton.SetTransformStart();
                }
                #endregion
                #region SmoothTangent
                {
                    foreach (var pair in humanoidFootIK.updateFrame.Frames)
                    {
                        var frame = pair.Key;
                        if (frame > lastFrame)
                            frame = lastFrame;
                        var weight = pair.Value;
                        var time = GetFrameTime(frame);
                        for (var ikIndex = AnimatorIKIndex.LeftFoot; ikIndex <= AnimatorIKIndex.RightFoot; ikIndex++)
                        {
                            int index = ikIndex - AnimatorIKIndex.LeftFoot;
                            for (int i = 0; i < 3; i++)
                            {
                                var keyIndex = AnimationCommon.FindKeyframeAtTime(humanoidFootIK.ikCurves[index].ikT[i], time);
                                if (keyIndex >= 0)
                                    humanoidFootIK.ikCurves[index].ikT[i].SmoothTangents(keyIndex, weight);
                            }
                            for (int i = 0; i < 4; i++)
                            {
                                var keyIndex = AnimationCommon.FindKeyframeAtTime(humanoidFootIK.ikCurves[index].ikQ[i], time);
                                if (keyIndex >= 0)
                                    humanoidFootIK.ikCurves[index].ikQ[i].SmoothTangents(keyIndex, weight);
                            }
                        }
                    }
                }
                #endregion
                #region Write
                for (var ikIndex = AnimatorIKIndex.LeftFoot; ikIndex <= AnimatorIKIndex.RightFoot; ikIndex++)
                {
                    int index = ikIndex - AnimatorIKIndex.LeftFoot;
                    for (int i = 0; i < 3; i++)
                    {
                        SetAnimationCurveAnimatorIkT(ikIndex, i, humanoidFootIK.ikCurves[index].ikT[i]);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        SetAnimationCurveAnimatorIkQ(ikIndex, i, humanoidFootIK.ikCurves[index].ikQ[i]);
                    }
                }
                #endregion

                update = true;
            }
            humanoidFootIK.Clear();
            return update;
        }
        #endregion

        #region Collision
        private class Collision
        {
            public int savedObjectDataFrame = -1;

            [System.Diagnostics.DebuggerDisplay("{renderer.name}")]
            public class EditObjectData
            {
                public const int VertexGroupCount = 300;

                public Renderer renderer;
                public UnityEngine.Object nearestPrefabInstanceRoot;
                public MeshFilter meshFilter;
                //Save
                public Matrix4x4 saveLocalToWorldMatrix;
                public Mesh saveMesh;
                public bool createSaveMesh;
                public Mesh updateMesh;
                public bool createUpdateMesh;
                //Calc
                public List<Vector3> saveVertices = new();
                public List<Vector3> updateVertices = new();
                public Bounds bounds;
                public bool isMove;
                [System.Diagnostics.DebuggerDisplay("\"({bounds}, VertexCount: {vertices.Count}\")")]
                public class VertexGroup
                {
                    public Bounds bounds;
                    public List<int> vertices = new();
                    //Calc
                    public List<int> intersectsTriangleGroups = new();
                    public float rate;
                    public Vector3 hitPosition;
                }
                public VertexGroup[] vertexGroups;

                public void ResetUpdateCalc()
                {
                    saveVertices.Clear();
                    updateVertices.Clear();
                    bounds = new Bounds();
                    isMove = false;
                }
                public void SetUpdateCalc()
                {
                    saveMesh.GetVertices(saveVertices);
                    updateMesh.GetVertices(updateVertices);
                    Matrix4x4 localToWorldMatrix;
                    {
                        if (renderer is SkinnedMeshRenderer)
                        {
                            var rt = renderer.transform;
                            localToWorldMatrix = Matrix4x4.TRS(rt.position, rt.rotation, Vector3.one);
                        }
                        else
                            localToWorldMatrix = renderer.localToWorldMatrix;
                    }
                    {
                        var groupNum = Mathf.CeilToInt(updateVertices.Count / (float)VertexGroupCount);
                        if (vertexGroups == null || vertexGroups.Length != groupNum)
                            vertexGroups = new VertexGroup[groupNum];
                        Parallel.For(0, groupNum, i =>
                        {
                            if (vertexGroups[i] == null)
                                vertexGroups[i] = new VertexGroup();
                            vertexGroups[i].vertices.Clear();
                            vertexGroups[i].bounds = new Bounds();

                            GetVertexGroupRange(i, out int begin, out int end);
                            for (int v = begin; v < end; v++)
                            {
                                saveVertices[v] = saveLocalToWorldMatrix.MultiplyPoint3x4(saveVertices[v]);
                                updateVertices[v] = localToWorldMatrix.MultiplyPoint3x4(updateVertices[v]);

                                const float IgnoreMin = 0.0001f;
                                if (Mathf.Abs(saveVertices[v].x - updateVertices[v].x) < IgnoreMin &&
                                    Mathf.Abs(saveVertices[v].y - updateVertices[v].y) < IgnoreMin &&
                                    Mathf.Abs(saveVertices[v].z - updateVertices[v].z) < IgnoreMin)
                                    continue;

                                if (vertexGroups[i].vertices.Count == 0)
                                    vertexGroups[i].bounds = new Bounds(saveVertices[v], Vector3.zero);
                                else
                                    vertexGroups[i].bounds.Encapsulate(saveVertices[v]);
                                vertexGroups[i].bounds.Encapsulate(updateVertices[v]);
                                vertexGroups[i].vertices.Add(v);
                            }
                        });
                        {
                            bounds = new Bounds();
                            isMove = false;
                            for (int i = 0; i < groupNum; i++)
                            {
                                if (vertexGroups[i].vertices.Count <= 0)
                                    continue;
                                if (!isMove)
                                {
                                    bounds = vertexGroups[i].bounds;
                                    isMove = true;
                                }
                                else
                                {
                                    bounds.Encapsulate(vertexGroups[i].bounds);
                                }
                            }
                        }
                    }
                }
                public void GetVertexGroupRange(int groupIndex, out int begin, out int end)
                {
                    begin = groupIndex * VertexGroupCount;
                    end = begin + VertexGroupCount;
                    if (end > updateVertices.Count)
                        end = updateVertices.Count;
                }
            }
            public Dictionary<Renderer, EditObjectData> editObjectData = new();

            public abstract class CollisionRendererData
            {
                public const int VertexGroupCount = 300;
                public const int TriangleGroupCount = 30;

                public Renderer renderer;
                public UnityEngine.Object nearestPrefabInstanceRoot;

                public float savedTime;
                public Matrix4x4 savedLocalToWorldMatrix;
                public List<Vector3> vertices = new();
                public List<int> triangles = new();
                public List<int> tmpTriangles = new();
                public Bounds bounds;
                public Bounds[] triangleBounds;
                public Vector3[] triangleNormals;
                public Bounds[] triangleGroupBounds;

                public bool HasBuffer => vertices.Count > 0;

                public virtual void Release() { }

                public virtual void ResetCalc()
                {
                    savedTime = -1f;
                    vertices.Clear();
                    triangles.Clear();
                    tmpTriangles.Clear();
                }
                public void SetCalc(float time)
                {
                    savedTime = time;
                    savedLocalToWorldMatrix = renderer.transform.localToWorldMatrix;
                    var mesh = GetBakedMesh();
                    mesh.GetVertices(vertices);
                    {
                        triangles.Clear();
                        for (int i = 0; i < mesh.subMeshCount; i++)
                        {
                            if (mesh.GetTopology(i) == MeshTopology.Triangles)
                            {
                                mesh.GetTriangles(tmpTriangles, i);
                                triangles.AddRange(tmpTriangles);
                            }
                        }
                        tmpTriangles.Clear();
                    }
                    {
                        Matrix4x4 localToWorldMatrix;
                        if (renderer is SkinnedMeshRenderer)
                        {
                            var rt = renderer.transform;
                            localToWorldMatrix = Matrix4x4.TRS(rt.position, rt.rotation, Vector3.one);
                        }
                        else
                            localToWorldMatrix = renderer.localToWorldMatrix;
                        var groupNum = Mathf.CeilToInt(vertices.Count / (float)VertexGroupCount);
                        Parallel.For(0, groupNum, i =>
                        {
                            GetVertexGroupRange(i, out int begin, out int end);
                            for (int v = begin; v < end; v++)
                            {
                                vertices[v] = localToWorldMatrix.MultiplyPoint3x4(vertices[v]);
                            }
                        });
                    }
                    {
                        Assert.IsTrue(TriangleGroupCount % 3 == 0);
                        var groupNum = Mathf.CeilToInt(triangles.Count / (float)TriangleGroupCount);
                        if (triangleBounds == null || triangleBounds.Length != triangles.Count / 3)
                            triangleBounds = new Bounds[triangles.Count / 3];
                        if (triangleNormals == null || triangleNormals.Length != triangles.Count / 3)
                            triangleNormals = new Vector3[triangles.Count / 3];
                        if (triangleGroupBounds == null || triangleGroupBounds.Length != groupNum)
                            triangleGroupBounds = new Bounds[groupNum];
                        Parallel.For(0, groupNum, i =>
                        {
                            GetTriangleGroupRange(i, out int begin, out int end);
                            for (int triangleIndex = begin; triangleIndex < end; triangleIndex++)
                            {
                                var vt = triangleIndex * 3;
                                {
                                    triangleBounds[triangleIndex] = new Bounds(vertices[triangles[vt + 0]], Vector3.zero);
                                    triangleBounds[triangleIndex].Encapsulate(vertices[triangles[vt + 1]]);
                                    triangleBounds[triangleIndex].Encapsulate(vertices[triangles[vt + 2]]);
                                }
                                {
                                    triangleNormals[triangleIndex] = Vector3.Cross(vertices[triangles[vt + 0]] - vertices[triangles[vt + 1]],
                                                                                    vertices[triangles[vt + 1]] - vertices[triangles[vt + 2]]).normalized;
                                }
                                if (triangleIndex == begin)
                                    triangleGroupBounds[i] = triangleBounds[triangleIndex];
                                else
                                    triangleGroupBounds[i].Encapsulate(triangleBounds[triangleIndex]);
                            }
                        });
                        if (groupNum > 0)
                        {
                            bounds = triangleGroupBounds[0];
                            for (int i = 1; i < groupNum; i++)
                                bounds.Encapsulate(triangleGroupBounds[i]);
                        }
                    }
                }

                public void GetVertexGroupRange(int groupIndex, out int begin, out int end)
                {
                    begin = groupIndex * VertexGroupCount;
                    end = begin + VertexGroupCount;
                    if (end > vertices.Count)
                        end = vertices.Count;
                }
                public void GetTriangleGroupRange(int groupIndex, out int begin, out int end)
                {
                    begin = groupIndex * TriangleGroupCount;
                    end = begin + TriangleGroupCount;
                    if (end > triangles.Count)
                        end = triangles.Count;
                    begin /= 3;
                    end /= 3;
                }

                public abstract Mesh GetCurrentMesh();
                public abstract Mesh GetBakedMesh();
            }
            [System.Diagnostics.DebuggerDisplay("{renderer.name}")]
            public class CollisionMeshRendererData : CollisionRendererData
            {
                public MeshRenderer meshRenderer;
                public MeshFilter meshFilter;

                public override Mesh GetCurrentMesh()
                {
                    if (meshFilter != null)
                        return meshFilter.sharedMesh;
                    return null;
                }
                public override Mesh GetBakedMesh()
                {
                    return GetCurrentMesh();
                }
            }
            [System.Diagnostics.DebuggerDisplay("{renderer.name}")]
            public class CollisionSkinnedMeshRendererData : CollisionRendererData
            {
                public SkinnedMeshRenderer skinnedMeshRenderer;
                public Mesh bakedMesh;
                public bool baked;

                public override void Release()
                {
                    base.Release();

                    if (bakedMesh != null)
                    {
                        Mesh.DestroyImmediate(bakedMesh);
                        bakedMesh = null;
                    }
                    baked = false;
                }

                public override void ResetCalc()
                {
                    base.ResetCalc();

                    baked = false;
                }

                public override Mesh GetCurrentMesh()
                {
                    if (skinnedMeshRenderer != null)
                        return skinnedMeshRenderer.sharedMesh;
                    return null;
                }
                public override Mesh GetBakedMesh()
                {
                    if (skinnedMeshRenderer != null)
                    {
                        var mesh = skinnedMeshRenderer.sharedMesh;
                        if (mesh != null)
                        {
                            if (bakedMesh == null)
                            {
                                bakedMesh = new Mesh();
                                bakedMesh.hideFlags |= HideFlags.HideAndDontSave;
                            }
                            if (!baked)
                            {
                                skinnedMeshRenderer.BakeMesh(bakedMesh);
                                baked = true;
                            }
                            return bakedMesh;
                        }
                    }
                    return null;
                }
            }
            public Dictionary<Renderer, CollisionRendererData> collisionObjectData;

            public List<int> updateBoneIndexes = new();
            public Dictionary<int, int> updateCurveBoneIndexes = new();

            public PointSignal collisionSignal = new();

            public void Release()
            {
                if (editObjectData != null)
                {
                    foreach (var pair in editObjectData)
                    {
                        if (pair.Value.createSaveMesh && pair.Value.saveMesh != null)
                            Mesh.DestroyImmediate(pair.Value.saveMesh);
                        if (pair.Value.createUpdateMesh && pair.Value.updateMesh != null)
                            Mesh.DestroyImmediate(pair.Value.updateMesh);
                    }
                    editObjectData = null;
                }
                ReleaseCollisionObjectData();
            }
            public void ReleaseCollisionObjectData()
            {
                if (collisionObjectData != null)
                {
                    foreach (var pair in collisionObjectData)
                    {
                        pair.Value.Release();
                    }
                    collisionObjectData = null;
                }
            }
        }
        private Collision collision;
        private void ReleaseCollision()
        {
            collision?.Release();
            collision = null;
        }
        private void SaveCollision(bool forceUpdate)
        {
            if (!extraOptionsCollision)
            {
                ReleaseCollision();
                return;
            }
            collision ??= new Collision();

            var currentFrame = UAw.GetCurrentFrame();

            #region NotUpdateCheck
            if (!forceUpdate)
            {
                if (collision.savedObjectDataFrame == currentFrame)
                    return;
            }
            #endregion

            collision.savedObjectDataFrame = currentFrame;

            #region Collision.EditObjectData
            foreach (var renderer in Renderers)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.enabled)
                    continue;
                if (renderer is not (MeshRenderer or SkinnedMeshRenderer))
                    continue;

                if (!collision.editObjectData.TryGetValue(renderer, out Collision.EditObjectData data))
                {
                    data = new Collision.EditObjectData
                    {
                        renderer = renderer,
                        nearestPrefabInstanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject)
                    };
                    if (renderer is MeshRenderer meshRenderer)
                    {
                        data.meshFilter = meshRenderer.GetComponent<MeshFilter>();
                    }
                    collision.editObjectData.Add(renderer, data);
                }

                if (renderer is MeshRenderer)
                {
                    data.saveLocalToWorldMatrix = renderer.localToWorldMatrix;
                    if (data.meshFilter != null)
                        data.saveMesh = data.meshFilter.sharedMesh;
                }
                else if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                {
                    var rt = renderer.transform;
                    data.saveLocalToWorldMatrix = Matrix4x4.TRS(rt.position, rt.rotation, Vector3.one);
                    if (skinnedMeshRenderer.sharedMesh == null)
                        continue;
                    if (data.saveMesh == null)
                    {
                        data.saveMesh = new Mesh();
                        data.saveMesh.hideFlags |= HideFlags.HideAndDontSave;
                        data.createSaveMesh = true;
                    }
                    skinnedMeshRenderer.BakeMesh(data.saveMesh);
                }
            }
            #endregion

            #region collision.collisionObjectData 
            if (collision.collisionObjectData == null)
            {
                collision.collisionObjectData = new Dictionary<Renderer, Collision.CollisionRendererData>();
                void AddGameObject(GameObject go)
                {
                    static bool CheckHideFlags(Transform t)
                    {
                        if ((t.gameObject.hideFlags & (HideFlags.HideAndDontSave | HideFlags.NotEditable)) != 0)
                            return false;

                        if (t.parent != null)
                            return CheckHideFlags(t.parent);
                        else
                            return true;
                    }

                    #region MeshRenderer
                    {
                        var meshRenderers = go.GetComponentsInChildren<MeshRenderer>(true);
                        if (meshRenderers != null && meshRenderers.Length > 0)
                        {
                            foreach (var meshRenderer in meshRenderers)
                            {
                                if (meshRenderer == null || !meshRenderer.enabled || !meshRenderer.gameObject.activeInHierarchy)
                                    continue;
                                if (!CheckHideFlags(meshRenderer.transform))
                                    continue;
                                var prefab = PrefabUtility.GetNearestPrefabInstanceRoot(meshRenderer.gameObject);
                                collision.collisionObjectData.Add(meshRenderer, new Collision.CollisionMeshRendererData()
                                {
                                    renderer = meshRenderer,
                                    nearestPrefabInstanceRoot = prefab,
                                    meshRenderer = meshRenderer,
                                    meshFilter = meshRenderer.GetComponent<MeshFilter>(),
                                });
                            }
                        }
                    }
                    #endregion
                    #region SkinnedMeshRenderer
                    {
                        var skinnedMeshRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        if (skinnedMeshRenderers != null && skinnedMeshRenderers.Length > 0)
                        {
                            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
                            {
                                if (skinnedMeshRenderer == null || !skinnedMeshRenderer.enabled || !skinnedMeshRenderer.gameObject.activeInHierarchy)
                                    continue;
                                if (!CheckHideFlags(skinnedMeshRenderer.transform))
                                    continue;
                                var prefab = PrefabUtility.GetNearestPrefabInstanceRoot(skinnedMeshRenderer.gameObject);
                                collision.collisionObjectData.Add(skinnedMeshRenderer, new Collision.CollisionSkinnedMeshRendererData()
                                {
                                    renderer = skinnedMeshRenderer,
                                    nearestPrefabInstanceRoot = prefab,
                                    skinnedMeshRenderer = skinnedMeshRenderer,
                                });
                            }
                        }
                    }
                    #endregion
                }

                if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                {
                    var scene = PrefabStageUtility.GetCurrentPrefabStage().scene;
                    if (scene.isLoaded)
                    {
                        foreach (var go in scene.GetRootGameObjects())
                            AddGameObject(go);
                    }
                }
                else
                {
                    for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                    {
                        var scene = SceneManager.GetSceneAt(sceneIndex);
                        if (scene.isLoaded)
                        {
                            foreach (var go in scene.GetRootGameObjects())
                                AddGameObject(go);
                        }
                    }
                }
            }
            #endregion
        }
        private void UpdateCollision()
        {
            if (!extraOptionsCollision || updatePoseFixAnimation || curvesWasModified.Count <= 0 ||
                collision == null || collision.editObjectData == null || collision.collisionObjectData == null)
            {
                return;
            }

            #region Ready
            #region DataReady
            {
                foreach (var pair in collision.editObjectData)
                {
                    if (collision.collisionObjectData.TryGetValue(pair.Key, out Collision.CollisionRendererData data))
                        data.ResetCalc();
                }
                if (extraOptionsSynchronizeAnimation)
                {
                    foreach (var pair in collision.collisionObjectData)
                    {
                        var data = pair.Value;
                        if (data.savedTime != CurrentTime)
                            data.ResetCalc();
                    }
                }
            }
            #endregion
            #region updateBoneIndexes
            bool hasHumanRoot = false;
            {
                collision.updateBoneIndexes.Clear();
                collision.updateCurveBoneIndexes.Clear();
                foreach (var pair in curvesWasModified)
                {
                    if (pair.Value.deleted != AnimationUtility.CurveModifiedType.CurveModified)
                        continue;
                    var boneIndex = GetBoneIndexFromCurveBinding(pair.Value.binding);
                    collision.updateCurveBoneIndexes[pair.Key] = boneIndex;
                    if (boneIndex >= 0)
                    {
                        if (!collision.updateBoneIndexes.Contains(boneIndex))
                        {
                            collision.updateBoneIndexes.Add(boneIndex);
                            if (IsHuman && boneIndex == 0)
                                hasHumanRoot = true;
                        }
                    }
                }
                collision.updateBoneIndexes.Sort((a, b) => BoneHierarchyLevels[a] - BoneHierarchyLevels[b]);
            }
            #endregion
            #endregion

            const int MaxIterations = 3;
            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                bool endFlag = true;
                foreach (var updateBoneIndex in collision.updateBoneIndexes)
                {
                    endFlag = true;

                    SampleAnimation(EditObjectFlag.SceneObject);

                    #region Update
                    foreach (var pair in collision.editObjectData)
                    {
                        var renderer = pair.Key;
                        var data = pair.Value;

                        data.ResetUpdateCalc();

                        if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.enabled)
                            continue;

                        if (renderer is MeshRenderer)
                        {
                            if (data.meshFilter == null)
                                continue;

                            data.updateMesh = data.meshFilter.sharedMesh;
                        }
                        else if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                        {
                            if (skinnedMeshRenderer.sharedMesh == null)
                                continue;
                            if (data.updateMesh == null)
                            {
                                data.updateMesh = new Mesh();
                                data.updateMesh.hideFlags |= HideFlags.HideAndDontSave;
                                data.createUpdateMesh = true;
                            }

                            skinnedMeshRenderer.BakeMesh(data.updateMesh);
                        }
                        if (data.saveMesh == null || data.updateMesh == null ||
                            data.saveMesh.vertexCount != data.updateMesh.vertexCount)
                            continue;

                        data.SetUpdateCalc();
                    }
                    #endregion

                    #region Calc
                    float rate = 1f;
                    Vector3 hitPosition = Vector3.zero;
                    foreach (var pair in collision.editObjectData)
                    {
                        var renderer = pair.Key;
                        var data = pair.Value;

                        #region IgnoreCheck
                        if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.enabled)
                            continue;
                        if (!data.isMove)
                            continue;
                        #endregion

                        foreach (var meshPair in collision.collisionObjectData)
                        {
                            var meshData = meshPair.Value;

                            #region IgnoreCheck
                            if (meshData.renderer == null || !meshData.renderer.gameObject.activeInHierarchy || !meshData.renderer.enabled)
                                continue;
                            if (meshData.GetCurrentMesh() == null)
                                continue;
                            if (renderer == meshData.renderer)
                                continue;
                            if (data.nearestPrefabInstanceRoot != null && data.nearestPrefabInstanceRoot == meshData.nearestPrefabInstanceRoot)
                                continue;
                            {
                                if (collision.editObjectData.TryGetValue(meshPair.Key, out Collision.EditObjectData otherData))
                                {
                                    if (otherData.isMove)
                                        continue;
                                }
                            }
                            #endregion

                            if (meshData.HasBuffer && meshData.savedLocalToWorldMatrix != meshData.renderer.transform.localToWorldMatrix)
                                meshData.ResetCalc();

                            if (!meshData.HasBuffer)
                            {
                                //If it is uncertain but you do not create information, exclude it here
                                if (meshData.renderer is MeshRenderer)
                                {
                                    if (!data.bounds.Intersects(meshData.renderer.bounds))
                                        continue;
                                }
                                else
                                {
                                    var uncertainBounds = meshData.renderer.bounds;
                                    uncertainBounds.Expand(uncertainBounds.size);
                                    if (!data.bounds.Intersects(uncertainBounds))
                                        continue;
                                }
                            }

                            #region DataReady
                            if (!meshData.HasBuffer)   //First time only
                                meshData.SetCalc(CurrentTime);
                            #endregion

                            //Certain exclusion decision that created the information
                            if (!data.bounds.Intersects(meshData.bounds))
                                continue;

                            #region EditObjectVertex vs CollisionObjectTriangle
                            Parallel.ForEach(data.vertexGroups, group =>
                            {
                                group.rate = 1f;
                                if (group.vertices.Count > 0 &&
                                    group.bounds.Intersects(meshData.bounds))
                                {
                                    group.intersectsTriangleGroups.Clear();
                                    for (int tGroup = 0; tGroup < meshData.triangleGroupBounds.Length; tGroup++)
                                    {
                                        if (!meshData.triangleGroupBounds[tGroup].Intersects(group.bounds))
                                            continue;
                                        group.intersectsTriangleGroups.Add(tGroup);
                                    }
                                    if (group.intersectsTriangleGroups.Count > 0)
                                    {
                                        #region EditMeshVertex vs OtherMeshTriangle
                                        float minDistance = float.MaxValue;
                                        foreach (var v in group.vertices)
                                        {
                                            var worldRay = new Ray(data.saveVertices[v], (data.updateVertices[v] - data.saveVertices[v]).normalized);

                                            {
                                                if (!meshData.bounds.IntersectRay(worldRay, out float distance))
                                                    continue;
                                                if (minDistance < distance)
                                                    continue;
                                                if (Vector3.Dot(data.updateVertices[v] - worldRay.GetPoint(distance), worldRay.direction) < 0f)
                                                    continue;
                                            }

                                            foreach (var tGroup in group.intersectsTriangleGroups)
                                            {
                                                {
                                                    if (!meshData.triangleGroupBounds[tGroup].IntersectRay(worldRay, out float distance))
                                                        continue;
                                                    if (minDistance < distance)
                                                        continue;
                                                }

                                                meshData.GetTriangleGroupRange(tGroup, out int begin, out int end);
                                                for (int triangleIndex = begin; triangleIndex < end; triangleIndex++)
                                                {
                                                    {
                                                        if (!meshData.triangleBounds[triangleIndex].IntersectRay(worldRay, out float distance))
                                                            continue;
                                                        if (minDistance < distance)
                                                            continue;
                                                    }

                                                    if (Vector3.Dot(worldRay.direction, meshData.triangleNormals[triangleIndex]) >= 0f)
                                                        continue;

                                                    var vt = triangleIndex * 3;

                                                    if (!EditorCommon.Ray_Triangle(worldRay,
                                                                                    meshData.vertices[meshData.triangles[vt + 0]],
                                                                                    meshData.vertices[meshData.triangles[vt + 1]],
                                                                                    meshData.vertices[meshData.triangles[vt + 2]],
                                                                                    out Vector3 posP))
                                                    {
                                                        continue;
                                                    }

                                                    var vecAP = posP - data.saveVertices[v];
                                                    var vecAB = data.updateVertices[v] - data.saveVertices[v];
                                                    var subRate = vecAP.magnitude / vecAB.magnitude;
                                                    if (subRate < group.rate)
                                                    {
                                                        group.rate = subRate;
                                                        group.hitPosition = posP;
                                                        minDistance = Mathf.Min(minDistance, Vector3.Distance(posP, worldRay.origin));
                                                    }
                                                }
                                            }
                                        }
                                        #endregion
                                    }
                                }
                            });
                            foreach (var group in data.vertexGroups)
                            {
                                if (group.rate < rate)
                                {
                                    rate = group.rate;
                                    hitPosition = group.hitPosition;
                                }
                            }
                            #endregion
                        }
                    }
                    if (rate >= 1f)
                        break;
                    if (iteration < MaxIterations - 1)
                        rate = Mathf.Max(rate - 0.01f, 0f);    //Minute extrusion
                    else
                        rate = 0f;
                    #endregion

                    #region Write
                    {
                        bool written = false;
                        foreach (var pair in curvesWasModified)
                        {
                            if (pair.Value.deleted != AnimationUtility.CurveModifiedType.CurveModified)
                                continue;
                            if (!hasHumanRoot)
                            {
                                if (!collision.updateCurveBoneIndexes.TryGetValue(pair.Key, out int boneIndex))
                                {
                                    boneIndex = GetBoneIndexFromCurveBinding(pair.Value.binding);
                                    collision.updateCurveBoneIndexes.Add(pair.Key, boneIndex);
                                }
                                if (updateBoneIndex != boneIndex)
                                    continue;
                            }
                            var curve = GetEditorCurveCache(pair.Value.binding);
                            if (curve == null || pair.Value.beforeCurve == null)
                                continue;
                            var beforeValue = pair.Value.beforeCurve.Evaluate(CurrentTime);
                            var currentValue = curve.Evaluate(CurrentTime);
                            if (!Mathf.Approximately(beforeValue, currentValue))
                            {
                                var newValue = Mathf.LerpUnclamped(beforeValue, currentValue, rate);
                                if (!Mathf.Approximately(newValue, currentValue))
                                {
                                    AnimationCommon.SetKeyframe(curve, CurrentTime, newValue);
                                    SetEditorCurveCache(pair.Value.binding, curve);
                                    written = true;
                                }
                            }
                        }
                        if (!written)
                            break;
                    }
                    #endregion

                    collision.collisionSignal.Fire(hitPosition, 0.5f, Color.red);
                    endFlag = false;
                }
                if (endFlag)
                    break;
            }
        }
        public bool DrawCollision()
        {
            if (collision == null)
                return false;
            return collision.collisionSignal.Draw();
        }
        #endregion

        #region AnimationPlayable
        private class AnimationPlayable
        {
            public UAnimationMotionXToDeltaPlayable uAnimationMotionXToDeltaPlayable;
            public UAnimationOffsetPlayable uAnimationOffsetPlayable;
            public UAnimationClipPlayable uAnimationClipPlayable;

            public Playable animationOffsetPlayable;
            public AnimationClipPlayable defaultPosePlayable;

            public AnimationPlayable()
            {
                uAnimationMotionXToDeltaPlayable = new UAnimationMotionXToDeltaPlayable();
                uAnimationOffsetPlayable = new UAnimationOffsetPlayable();
                uAnimationClipPlayable = new UAnimationClipPlayable();
                Release();
            }
            public void Release()
            {
                animationOffsetPlayable = Playable.Null;
                defaultPosePlayable = (AnimationClipPlayable)Playable.Null;
            }
        }
        private AnimationPlayable animationPlayable;
        private AvatarMask blankAvatarMask;
        private AnimationClip defaultPoseClip;

        private void InitializeAnimationPlayable()
        {
            ReleaseAnimationPlayable();

            animationPlayable = new AnimationPlayable();
            blankAvatarMask = new AvatarMask();
            blankAvatarMask.hideFlags |= HideFlags.HideAndDontSave;
        }
        private void ReleaseAnimationPlayable()
        {
            animationPlayable?.Release();
            animationPlayable = null;
            if (blankAvatarMask != null)
            {
                AvatarMask.DestroyImmediate(blankAvatarMask);
                blankAvatarMask = null;
            }
            ClearDefaultPoseClip();
        }
        private void ClearDefaultPoseClip()
        {
            if (defaultPoseClip == null)
                return;

            AnimationClip.DestroyImmediate(defaultPoseClip);
            defaultPoseClip = null;
        }

        private void ReadyDefaultPoseClip()
        {
            if (defaultPoseClip != null)
                return;

            defaultPoseClip = new AnimationClip() { name = "VA DefaultPose" };
            defaultPoseClip.hideFlags |= HideFlags.HideAndDontSave;

            UAnimationMode.RevertPropertyModificationsForGameObject(VAW.GameObject);

            var rDatas = new Dictionary<EditorCurveBinding, ObjectReferenceKeyframe[]>();
            var fDatas = new Dictionary<EditorCurveBinding, AnimationCurve>();
            for (int boneIndex = 0; boneIndex < Bones.Length; boneIndex++)
            {
                var bindings = AnimationUtility.GetAnimatableBindings(Bones[boneIndex], VAW.GameObject);
                foreach (var binding in bindings)
                {
                    if (binding.isPPtrCurve)
                    {
                        AnimationUtility.GetObjectReferenceValue(VAW.GameObject, binding, out var data);
                        var curve = new ObjectReferenceKeyframe[]
                        {
                            new()
                            {
                                time = 0f,
                                value = data,
                            }
                        };
                        rDatas.TryAdd(binding, curve);
                    }
                    else
                    {
                        AnimationCurve curve = new();
                        {
                            AnimationUtility.GetFloatValue(VAW.GameObject, binding, out var value);
                            curve.AddKey(new Keyframe(0f, value));
                        }
                        fDatas.TryAdd(binding, curve);
                    }
                }
            }
            AnimationCommon.SetObjectReferenceCurves(defaultPoseClip, rDatas);
            AnimationCommon.SetEditorCurves(defaultPoseClip, fDatas);

            SetUpdateSampleAnimation();
        }
        #endregion

        #region HandPoseSet
        [Serializable]
        public class HandPoseSet
        {
            public PoseTemplate poseTemplate;
            public string[] leftMusclePropertyNames;
            public string[] rightMusclePropertyNames;

            public void SetLeft()
            {
                poseTemplate.musclePropertyNames = leftMusclePropertyNames;
            }
            public void SetRight()
            {
                poseTemplate.musclePropertyNames = rightMusclePropertyNames;
            }

            [NonSerialized]
            public Texture2D iconLeft;
            [NonSerialized]
            public Texture2D iconRight;
        }
        [SerializeField]
        public List<HandPoseSet> handPoseSetList;

        private void InitializeHandPoseSetList()
        {
            ReleaseHandPoseSetList();

            handPoseSetList = new List<HandPoseSet>();
        }
        private void ReleaseHandPoseSetList()
        {
            if (handPoseSetList != null)
            {
                foreach (var item in handPoseSetList)
                {
                    if (item == null)
                        continue;
                    if (item.iconLeft != null)
                    {
                        Texture2D.DestroyImmediate(item.iconLeft);
                        item.iconLeft = null;
                    }
                    if (item.iconRight != null)
                    {
                        Texture2D.DestroyImmediate(item.iconRight);
                        item.iconRight = null;
                    }
                }
                handPoseSetList.Clear();
            }
        }
        #endregion

        #region BlendShapeSet
        [Serializable]
        public class BlendShapeSet
        {
            public PoseTemplate poseTemplate;
            [NonSerialized]
            public Texture2D icon;
        }
        [SerializeField]
        public List<BlendShapeSet> blendShapeSetList;

        private void InitializeBlendShapeSetList()
        {
            ReleaseBlendShapeSetList();

            blendShapeSetList = new List<BlendShapeSet>();
        }
        private void ReleaseBlendShapeSetList()
        {
            if (blendShapeSetList != null)
            {
                foreach (var item in blendShapeSetList)
                {
                    if (item == null)
                        continue;
                    if (item.icon != null)
                    {
                        Texture2D.DestroyImmediate(item.icon);
                        item.icon = null;
                    }
                }
                blendShapeSetList.Clear();
            }
        }
        #endregion

        #region AnimationHelpers
        public HumanBodyBones GetHumanVirtualBoneParentBone(HumanBodyBones bone)
        {
            if (!IsHuman) return (HumanBodyBones)(-1);
            var vbs = HumanVirtualBones[(int)bone];
            if (vbs != null)
            {
                foreach (var vb in vbs)
                {
                    if (HumanoidBones[(int)vb.boneA] == null) continue;
                    return vb.boneA;
                }
            }
            return (HumanBodyBones)(-1);
        }
        public Vector3 GetHumanVirtualBoneLimitSign(HumanBodyBones bone)
        {
            if (!IsHuman) return Vector3.one;
            var vbs = HumanVirtualBones[(int)bone];
            if (vbs != null)
            {
                foreach (var vb in vbs)
                {
                    if (HumanoidBones[(int)vb.boneA] == null) continue;
                    return vb.limitSign;
                }
            }
            return Vector3.one;
        }

        public Vector3 GetHumanVirtualBonePosition(HumanBodyBones bone)
        {
            if (!IsHuman) return Vector3.zero;
            var vbs = HumanVirtualBones[(int)bone];
            if (vbs != null)
            {
                foreach (var vb in vbs)
                {
                    if (Skeleton.HumanoidBones[(int)vb.boneA] == null || Skeleton.HumanoidBones[(int)vb.boneB] == null) continue;
                    var posA = Skeleton.HumanoidBones[(int)vb.boneA].transform.position;
                    var posB = Skeleton.HumanoidBones[(int)vb.boneB].transform.position;
                    return Vector3.Lerp(posA, posB, vb.leap);
                }
            }
            return Vector3.zero;
        }
        public Quaternion GetHumanVirtualBoneRotation(HumanBodyBones bone)
        {
            if (!IsHuman) return Quaternion.identity;
            var vbs = HumanVirtualBones[(int)bone];
            if (vbs != null)
            {
                foreach (var vb in vbs)
                {
                    if (Skeleton.HumanoidBones[(int)vb.boneA] == null) continue;
                    var vRotation = Vector3.zero;
                    for (int i = 0; i < 3; i++)
                    {
                        var mi = HumanTrait.MuscleFromBone((int)bone, i);
                        if (mi >= 0)
                        {
                            var muscle = GetAnimationValueAnimatorMuscle(mi);
                            vRotation[i] = Mathf.Lerp(HumanoidMuscleLimit[(int)bone].min[i], HumanoidMuscleLimit[(int)bone].max[i], (muscle + 1f) / 2f);
                        }
                    }
                    var qRotation = Quaternion.Euler(vRotation);
                    var parentRotation = Skeleton.HumanoidBones[(int)vb.boneA].transform.rotation * GetHumanoidAvatarPostRotation(vb.boneA);
                    return parentRotation * qRotation;
                }
            }
            return Quaternion.identity;
        }
        public Quaternion GetHumanVirtualBoneParentRotation(HumanBodyBones bone)
        {
            if (!IsHuman) return Quaternion.identity;
            var vbs = HumanVirtualBones[(int)bone];
            if (vbs != null)
            {
                foreach (var vb in vbs)
                {
                    if (Skeleton.HumanoidBones[(int)vb.boneA] == null) continue;
                    return Skeleton.HumanoidBones[(int)vb.boneA].transform.rotation * GetHumanoidAvatarPostRotation(vb.boneA) * vb.addRotation;
                }
            }
            return Quaternion.identity;
        }

        public Vector3 GetHumanWorldRootPosition()
        {
            if (!IsHuman) return Vector3.zero;
            var bodyPosition = GetAnimationValueAnimatorRootT() * Skeleton.Animator.humanScale;
            return TransformPoseSave.StartMatrix.MultiplyPoint3x4(bodyPosition);
        }
        public Vector3 GetHumanLocalRootPosition(Vector3 pos)
        {
            if (!IsHuman) return Vector3.zero;
            var bodyPosition = TransformPoseSave.StartMatrix.inverse.MultiplyPoint3x4(pos);
            return bodyPosition / Skeleton.Animator.humanScale;
        }
        public Quaternion GetHumanWorldRootRotation()
        {
            if (!IsHuman) return Quaternion.identity;
            return TransformPoseSave.StartRotation * GetAnimationValueAnimatorRootQ();
        }
        public Quaternion GetHumanLocalRootRotation(Quaternion rot)
        {
            if (!IsHuman) return Quaternion.identity;
            return Quaternion.Inverse(TransformPoseSave.StartRotation) * rot;
        }

        public Vector3 GetAnimatorWorldMotionPosition()
        {
            if (Skeleton.Animator == null) return Vector3.zero;
            var scale = 1f;
            if (IsHuman) scale = Skeleton.Animator.humanScale;
            var bodyPosition = GetAnimationValueAnimatorMotionT() * scale;
            return TransformPoseSave.StartMatrix.MultiplyPoint3x4(bodyPosition);
        }
        public Vector3 GetAnimatorLocalMotionPosition(Vector3 pos)
        {
            if (Skeleton.Animator == null) return Vector3.zero;
            var scale = 1f;
            if (IsHuman) scale = Skeleton.Animator.humanScale;
            var bodyPosition = TransformPoseSave.StartMatrix.inverse.MultiplyPoint3x4(pos);
            return bodyPosition / scale;
        }
        public Quaternion GetAnimatorWorldMotionRotation()
        {
            if (Skeleton.Animator == null) return Quaternion.identity;
            return TransformPoseSave.StartRotation * GetAnimationValueAnimatorMotionQ();
        }
        public Quaternion GetAnimatorLocalMotionRotation(Quaternion rot)
        {
            if (Skeleton.Animator == null) return Quaternion.identity;
            return Quaternion.Inverse(TransformPoseSave.StartRotation) * rot;
        }

        public class PlayingAnimationInfo
        {
            public AnimationClip clip;
            public float time;
            public float length;
            public AnimatorStateMachine stateMachine;
        }
        public bool GetPlayingAnimationInfo(out PlayingAnimationInfo[] playingAnimationsInfo)
        {
            playingAnimationsInfo = null;

            if (!EditorApplication.isPlaying) return false;
            if (VAW.Animator != null && VAW.Animator.runtimeAnimatorController != null && VAW.Animator.isInitialized)
            {
                GetPlayingAnimationInfo_Animator(out playingAnimationsInfo);
            }
            else if (VAW.Animation != null)
            {
                GetPlayingAnimationInfo_Animation(out playingAnimationsInfo);
            }
            return playingAnimationsInfo != null && playingAnimationsInfo.Length > 0;
        }

        private void GetPlayingAnimationInfo_Animator(out PlayingAnimationInfo[] playingAnimationsInfo)
        {
            playingAnimationsInfo = null;
            var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
            var owc = VAW.Animator.runtimeAnimatorController as AnimatorOverrideController;
            if (ac == null || VAW.Animator.layerCount <= 0)
                return;

            var layers = ac.layers;
            playingAnimationsInfo = new PlayingAnimationInfo[layers.Length];
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                var currentAnimatorStateInfo = VAW.Animator.GetCurrentAnimatorStateInfo(layerIndex);
                AnimationClip resultClip = null;
                float resultTime = 0f;
                float resultLength = 0f;
                bool FindStateMachine(AnimatorStateMachine stateMachine)
                {
                    foreach (var state in stateMachine.states)
                    {
                        if (state.state.nameHash != currentAnimatorStateInfo.shortNameHash ||
                            currentAnimatorStateInfo.length <= 0f)
                            continue;

                        AnimationClip FindMotion(Motion motion)
                        {
                            if (motion != null)
                            {
                                if (motion is UnityEditor.Animations.BlendTree)
                                {
                                    #region BlendTree
                                    var blendTree = motion as UnityEditor.Animations.BlendTree;
                                    switch (blendTree.blendType)
                                    {
                                        case BlendTreeType.Simple1D:
                                            #region 1D
                                            {
                                                var param = VAW.Animator.GetFloat(blendTree.blendParameter);
                                                float near = float.MaxValue;
                                                int index = -1;
                                                for (int i = 0; i < blendTree.children.Length; i++)
                                                {
                                                    var offset = Mathf.Abs(blendTree.children[i].threshold - param);
                                                    if (offset < near)
                                                    {
                                                        index = i;
                                                        near = offset;
                                                    }
                                                }
                                                if (index >= 0)
                                                {
                                                    return FindMotion(blendTree.children[index].motion);
                                                }
                                            }
                                            #endregion
                                            break;
                                        case BlendTreeType.SimpleDirectional2D:
                                        case BlendTreeType.FreeformDirectional2D:
                                        case BlendTreeType.FreeformCartesian2D:
                                            #region 2D
                                            {
                                                var paramX = VAW.Animator.GetFloat(blendTree.blendParameter);
                                                var paramY = VAW.Animator.GetFloat(blendTree.blendParameterY);
                                                float near = float.MaxValue;
                                                int index = -1;
                                                for (int i = 0; i < blendTree.children.Length; i++)
                                                {
                                                    var offsetX = Mathf.Abs(blendTree.children[i].position.x - paramX);
                                                    var offsetY = Mathf.Abs(blendTree.children[i].position.y - paramY);
                                                    if (offsetX + offsetY < near)
                                                    {
                                                        index = i;
                                                        near = offsetX + offsetY;
                                                    }
                                                }
                                                if (index >= 0)
                                                {
                                                    return FindMotion(blendTree.children[index].motion);
                                                }
                                            }
                                            #endregion
                                            break;
                                        case BlendTreeType.Direct:
                                            #region Direct
                                            {
                                                float max = float.MinValue;
                                                int index = -1;
                                                for (int i = 0; i < blendTree.children.Length; i++)
                                                {
                                                    var param = VAW.Animator.GetFloat(blendTree.children[i].directBlendParameter);
                                                    if (param >= max)
                                                    {
                                                        index = i;
                                                        max = param;
                                                    }
                                                }
                                                if (index >= 0)
                                                {
                                                    return FindMotion(blendTree.children[index].motion);
                                                }
                                            }
                                            #endregion
                                            break;
                                        default:
                                            Assert.IsTrue(false, "not support type");
                                            break;
                                    }
                                    #endregion
                                }
                                else if (motion is AnimationClip)
                                {
                                    return motion as AnimationClip;
                                }
                                else
                                {
                                    Debug.LogWarningFormat("<color=blue>[Very Animation]</color>unknown support type {0}", motion);
                                }
                            }
                            return null;
                        }

                        var motion = ac.GetStateEffectiveMotion(state.state, layerIndex);
                        var clip = FindMotion(motion);
                        if (clip == null)
                            continue;
                        if (owc != null)
                            clip = owc[clip];
                        resultClip = clip;
                        if (resultClip.isLooping)
                        {
                            resultTime = currentAnimatorStateInfo.length * (currentAnimatorStateInfo.normalizedTime % 1f);
                        }
                        else
                        {
                            if (currentAnimatorStateInfo.normalizedTime > 1f)
                                resultTime = currentAnimatorStateInfo.length;
                            else
                                resultTime = currentAnimatorStateInfo.length * currentAnimatorStateInfo.normalizedTime;
                        }
                        resultLength = currentAnimatorStateInfo.length;
                        return true;
                    }
                    foreach (var cstateMachine in stateMachine.stateMachines)
                    {
                        if (FindStateMachine(cstateMachine.stateMachine))
                            return true;
                    }
                    return false;
                }

                var stateMachine = UAnimatorController.FindEffectiveRootStateMachine(ac, layerIndex);
                if (FindStateMachine(stateMachine))
                {
                    playingAnimationsInfo[layerIndex] = new PlayingAnimationInfo()
                    {
                        clip = resultClip,
                        time = resultTime,
                        length = resultLength,
                        stateMachine = ac.layers[layerIndex].stateMachine,
                    };
                }
            }
        }

        private void GetPlayingAnimationInfo_Animation(out PlayingAnimationInfo[] playingAnimationsInfo)
        {
            playingAnimationsInfo = null;
            List<PlayingAnimationInfo> infos = null;
            foreach (AnimationState state in VAW.Animation)
            {
                if (!state.enabled || state.length <= 0f) continue;
                var dstClip = state.clip;
                var time = state.time;
                var dstTime = time;
                var dstLength = state.length;
                switch (state.wrapMode)
                {
                    case WrapMode.Loop:
                        {
                            var loop = Mathf.FloorToInt(time / state.length);
                            dstTime -= loop * state.length;
                        }
                        break;
                    case WrapMode.PingPong:
                        {
                            var loop = Mathf.FloorToInt(time / state.length);
                            dstTime -= loop * state.length;
                            if (loop % 2 != 0)
                                dstTime = state.length - dstTime;
                        }
                        break;
                    default:
                        dstTime = Mathf.Min(dstTime, state.length);
                        break;
                }
                infos ??= new List<PlayingAnimationInfo>();
                infos.Add(new PlayingAnimationInfo()
                {
                    clip = dstClip,
                    time = dstTime,
                    length = dstLength,
                });
            }
            if (infos != null)
                playingAnimationsInfo = infos.ToArray();
        }

        private bool TryGetMuscleDof(int muscleIndex, out int humanoidIndex, out int dof)
        {
            humanoidIndex = -1;
            dof = -1;
            if (muscleIndex < 0 || muscleIndex >= HumanTrait.MuscleCount)
                return false;
            humanoidIndex = HumanTrait.BoneFromMuscle(muscleIndex);
            if (humanoidIndex < 0)
                return false;
            for (int i = 0; i < 3; i++)
            {
                if (HumanTrait.MuscleFromBone(humanoidIndex, i) == muscleIndex)
                {
                    dof = i;
                    return true;
                }
            }
            return false;
        }

        public float Muscle2EulerAngle(int muscleIndex, float muscleValue)
        {
            if (!TryGetMuscleDof(muscleIndex, out var humanoidIndex, out var dof))
                return 0f;
            if (muscleValue < 0f)
            {
                return Mathf.LerpUnclamped(0f, HumanoidMuscleLimit[humanoidIndex].min[dof], Mathf.Abs(muscleValue));
            }
            else if (muscleValue > 0f)
            {
                return Mathf.LerpUnclamped(0f, HumanoidMuscleLimit[humanoidIndex].max[dof], Mathf.Abs(muscleValue));
            }
            else
            {
                return 0f;
            }
        }
        public float EulerAngle2Muscle(int muscleIndex, float degree)
        {
            if (!TryGetMuscleDof(muscleIndex, out var humanoidIndex, out var dof))
                return 0f;
            if (degree < 0f)
            {
                var limit = HumanoidMuscleLimit[humanoidIndex].min[dof];
                if (limit == 0f)
                    return 0f;
                return -(degree / limit);
            }
            else if (degree > 0f)
            {
                var limit = HumanoidMuscleLimit[humanoidIndex].max[dof];
                if (limit == 0f)
                    return 0f;
                return degree / limit;
            }
            else
            {
                return 0f;
            }
        }

        public int GetMirrorMuscleIndex(int muscleIndex)
        {
            if (muscleIndex < 0) return -1;
            var humanIndex = HumanTrait.BoneFromMuscle(muscleIndex);
            if (humanIndex < 0) return -1;
            if (HumanBodyMirrorBones[humanIndex] < 0) return -1;
            for (int i = 0; i < 3; i++)
            {
                if (muscleIndex == HumanTrait.MuscleFromBone(humanIndex, i))
                    return HumanTrait.MuscleFromBone((int)HumanBodyMirrorBones[humanIndex], i);
            }
            return -1;
        }
        public Vector3 GetMirrorBoneLocalPosition(int boneIndex, Vector3 localPosition)
        {
            var rootInv = Quaternion.Inverse(BoneSaveTransforms[0].rotation);
            var local = localPosition - BoneSaveTransforms[boneIndex].localPosition;
            var parentRot = (rootInv * BoneSaveTransforms[boneIndex].rotation) * Quaternion.Inverse(BoneSaveTransforms[boneIndex].localRotation);
            var world = parentRot * local;
            world.x = -world.x;
            if (MirrorBoneIndexes[boneIndex] >= 0)
            {
                var mparentRot = (rootInv * BoneSaveTransforms[MirrorBoneIndexes[boneIndex]].rotation) * Quaternion.Inverse(BoneSaveTransforms[MirrorBoneIndexes[boneIndex]].localRotation);
                var mlocal = Quaternion.Inverse(mparentRot) * world;
                return BoneSaveTransforms[MirrorBoneIndexes[boneIndex]].localPosition + mlocal;
            }
            else
            {
                var mlocal = Quaternion.Inverse(parentRot) * world;
                return BoneSaveTransforms[boneIndex].localPosition + mlocal;
            }
        }
        public Quaternion GetMirrorBoneLocalRotation(int boneIndex, Quaternion localRotation)
        {
            var rootInv = Quaternion.Inverse(BoneSaveTransforms[0].rotation);
            var parentRot = (rootInv * BoneSaveTransforms[boneIndex].rotation) * Quaternion.Inverse(BoneSaveTransforms[boneIndex].localRotation);
            var wrot = parentRot * localRotation;
            if (MirrorBoneIndexes[boneIndex] >= 0 && MirrorBoneDatas[boneIndex].rootBoneIndex >= 0)
            {
                var rootRot = rootInv * BoneSaveTransforms[MirrorBoneDatas[boneIndex].rootBoneIndex].rotation;
                wrot *= Quaternion.Inverse(Quaternion.Inverse(rootRot) * (rootInv * BoneSaveTransforms[boneIndex].rotation));
                {
                    wrot *= Quaternion.Inverse(rootRot);
                    wrot = new Quaternion(wrot.x, -wrot.y, -wrot.z, wrot.w);
                    wrot *= rootRot;
                }
                wrot *= Quaternion.Inverse(rootRot) * (rootInv * BoneSaveTransforms[MirrorBoneIndexes[boneIndex]].rotation);
                var mparentRot = (rootInv * BoneSaveTransforms[MirrorBoneIndexes[boneIndex]].rotation) * Quaternion.Inverse(BoneSaveTransforms[MirrorBoneIndexes[boneIndex]].localRotation);
                return Quaternion.Inverse(mparentRot) * wrot;
            }
            else
            {
                var rootRot = rootInv * BoneSaveTransforms[boneIndex].rotation;
                wrot *= Quaternion.Inverse(rootRot);
                wrot = new Quaternion(wrot.x, -wrot.y, -wrot.z, wrot.w);
                wrot *= rootRot;
                return Quaternion.Inverse(parentRot) * wrot;
            }
        }
        public Vector3 GetMirrorBoneLocalScale(int boneIndex, Vector3 localScale)
        {
            if (MirrorBoneIndexes[boneIndex] >= 0)
            {
                var rootInv = Quaternion.Inverse(BoneSaveTransforms[0].rotation);
                var local = new Vector3(BoneSaveTransforms[boneIndex].localScale.x != 0f ? localScale.x / BoneSaveTransforms[boneIndex].localScale.x : 0f,
                                        BoneSaveTransforms[boneIndex].localScale.y != 0f ? localScale.y / BoneSaveTransforms[boneIndex].localScale.y : 0f,
                                        BoneSaveTransforms[boneIndex].localScale.z != 0f ? localScale.z / BoneSaveTransforms[boneIndex].localScale.z : 0f);
                var parentRot = (rootInv * BoneSaveTransforms[boneIndex].rotation);
                var world = parentRot * local;
                world.x = -world.x;
                var mparentRot = (rootInv * BoneSaveTransforms[MirrorBoneIndexes[boneIndex]].rotation);
                var mlocal = Quaternion.Inverse(mparentRot) * world;
                {
                    var minus = new Vector3(local.x < 0f ? 1f : 0f, local.y < 0f ? 1f : 0f, local.z < 0f ? 1f : 0f);
                    minus = parentRot * minus;
                    minus = Quaternion.Inverse(mparentRot) * minus;
                    for (int i = 0; i < 3; i++)
                    {
                        mlocal[i] = Mathf.Abs(mlocal[i]);
                        if (Mathf.Abs(minus[i]) > 0.5f)
                            mlocal[i] *= -1f;
                    }
                }
                return Vector3.Scale(BoneSaveTransforms[MirrorBoneIndexes[boneIndex]].localScale, mlocal);
            }
            else
            {
                return localScale;
            }
        }
        public string GetMirrorBlendShape(SkinnedMeshRenderer renderer, string name)
        {
            if (MirrorBlendShape.TryGetValue(renderer, out Dictionary<string, string> nameTable))
            {
                if (nameTable.TryGetValue(name, out string mirrorName))
                {
                    return mirrorName;
                }
            }
            return null;
        }

        public int GetLastFrame() => EditorCommon.GetLastFrame(CurrentClip.length, CurrentClip.frameRate);
        public float GetFrameTime(int frame) => EditorCommon.GetFrameTime(frame, CurrentClip.frameRate);
        public int GetTimeFrame(float time) => EditorCommon.GetTimeFrameRound(time, CurrentClip.frameRate);

        private float GetFrameSnapTime(float time = -1f)
        {
            if (time < 0f)
                return EditorCommon.SnapToFrame(CurrentTime, CurrentClip.frameRate);
            else
                return EditorCommon.SnapToFrame(time, CurrentClip.frameRate);
        }
        public float GetTotalClipLength()
        {
            var length = CurrentClip.length;
            if (animationMode == AnimationMode.Layers)
            {
                foreach (var item in CurrentLayerClips)
                {
                    if (item.Value == null)
                        continue;
                    length = Mathf.Max(length, item.Value.length);
                }
            }
            return length;
        }

        public int FindBeforeNearKeyframeAtTime(AnimationCurve curve, float time) => AnimationCommon.FindBeforeNearKeyframeAtTime(curve, GetFrameSnapTime(time), CurrentClip.frameRate);
        public int FindBeforeNearKeyframeAtTime(ObjectReferenceKeyframe[] keys, float time) => AnimationCommon.FindBeforeNearKeyframeAtTime(keys, GetFrameSnapTime(time), CurrentClip.frameRate);
        public int FindBeforeNearKeyframeAtTime(AnimationEvent[] events, float time) => AnimationCommon.FindBeforeNearKeyframeAtTime(events, GetFrameSnapTime(time), CurrentClip.frameRate);
        public int FindAfterNearKeyframeAtTime(AnimationCurve curve, float time) => AnimationCommon.FindAfterNearKeyframeAtTime(curve, GetFrameSnapTime(time), CurrentClip.frameRate);
        public int FindAfterNearKeyframeAtTime(ObjectReferenceKeyframe[] keys, float time) => AnimationCommon.FindAfterNearKeyframeAtTime(keys, GetFrameSnapTime(time), CurrentClip.frameRate);
        public int FindAfterNearKeyframeAtTime(AnimationEvent[] events, float time) => AnimationCommon.FindAfterNearKeyframeAtTime(events, GetFrameSnapTime(time), CurrentClip.frameRate);

        public Quaternion FixReverseRotationQuaternion(AnimationCurve[] curves, float time, Quaternion rotation) => AnimationCommon.FixReverseRotationQuaternion(curves, time, rotation, CurrentClip.frameRate);
        public Vector3 FixReverseRotationEuler(AnimationCurve[] curves, float time, Vector3 eulerAngles) => AnimationCommon.FixReverseRotationEuler(curves, time, eulerAngles, CurrentClip.frameRate);

        private void ActionAllAnimatorState(AnimationClip clip, Action<UnityEditor.Animations.AnimatorState> action)
        {
            var ac = AnimationCommon.GetAnimatorController(VAW.Animator);
            if (ac == null) return;

            var layers = ac.layers;
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                void ActionStateMachine(AnimatorStateMachine stateMachine)
                {
                    foreach (var state in stateMachine.states)
                    {
                        var motion = ac.GetStateEffectiveMotion(state.state, layerIndex);
                        if (motion is UnityEditor.Animations.BlendTree)
                        {
                            void ActionBlendTree(UnityEditor.Animations.BlendTree blendTree)
                            {
                                if (blendTree.children == null) return;
                                var children = blendTree.children;
                                for (int j = 0; j < children.Length; j++)
                                {
                                    if (children[j].motion is UnityEditor.Animations.BlendTree)
                                    {
                                        ActionBlendTree(children[j].motion as UnityEditor.Animations.BlendTree);
                                    }
                                    else
                                    {
                                        if (children[j].motion == clip)
                                        {
                                            action(state.state);
                                        }
                                    }
                                }
                            }

                            ActionBlendTree(motion as UnityEditor.Animations.BlendTree);
                        }
                        else
                        {
                            if (motion == clip)
                            {
                                action(state.state);
                            }
                        }
                    }
                    foreach (var childStateMachine in stateMachine.stateMachines)
                    {
                        ActionStateMachine(childStateMachine.stateMachine);
                    }
                }

                var stateMachine = UAnimatorController.FindEffectiveRootStateMachine(ac, layerIndex);
                ActionStateMachine(stateMachine);
            }
        }

        private Transform GetTransformFromPath(string path)
        {
            var root = Skeleton.GameObject.transform;
            if (!string.IsNullOrEmpty(path))
            {
                var splits = path.Split('/');
                for (int i = 0; i < splits.Length; i++)
                {
                    bool contains = false;
                    for (int j = 0; j < root.childCount; j++)
                    {
                        if (root.GetChild(j).name == splits[i])
                        {
                            root = root.GetChild(j);
                            contains = true;
                            break;
                        }
                    }
                    if (!contains) return null;
                }
            }
            return root;
        }
        #endregion

        private class OnCurveWasModifiedData
        {
            public EditorCurveBinding binding;
            public AnimationUtility.CurveModifiedType deleted;
            public AnimationCurve beforeCurve;
        }
        private readonly Dictionary<int, OnCurveWasModifiedData> curvesWasModified = new();
        private readonly Dictionary<int, OnCurveWasModifiedData> curvesWasModifiedStopped = new();
        private bool OnCurveWasModifiedStop = false;
        private bool OnCurveWasModifiedIgnore = false;
        private void OnCurveWasModified(AnimationClip clip, EditorCurveBinding binding, AnimationUtility.CurveModifiedType deleted)
        {
            if (IsEditError) return;

            if (CurrentClip != clip || !IsCheckChangeClipClearEditorCurveCache(clip))
                return;

            if (deleted == AnimationUtility.CurveModifiedType.ClipModified)
            {
                ClearEditorCurveCache();
                return;
            }

            if (OnCurveWasModifiedIgnore) return;

            AnimationCurve beforeCurve = null;
            if (IsContainsEditorCurveCache(binding))
            {
                beforeCurve = GetEditorCurveCache(binding);
            }
            if (deleted == AnimationUtility.CurveModifiedType.CurveModified ||
                deleted == AnimationUtility.CurveModifiedType.CurveDeleted)
            {
                if (editorCurveCacheDic != null)
                {
                    if (editorCurveCacheDic.ContainsKey(GetEditorCurveBindingHashCode(binding)))
                    {
                        RemoveEditorCurveCache(binding);
                    }
                }
                if (binding.type == typeof(Transform) &&
                    binding.propertyName.StartsWith(URotationCurveInterpolation.PrefixForInterpolation[(int)URotationCurveInterpolation.Mode.NonBaked], StringComparison.Ordinal))
                {
                    var bindingSub = binding;
                    foreach (var propertyName in AnimationCommon.PropertyName.Rotation[(int)URotationCurveInterpolation.Mode.RawQuaternions])
                    {
                        bindingSub.propertyName = propertyName;
                        OnCurveWasModified(clip, bindingSub, deleted);
                    }
                    return;
                }
            }

            AddOnCurveWasModified(binding, deleted, beforeCurve);
        }
        private void AddOnCurveWasModified(EditorCurveBinding binding, AnimationUtility.CurveModifiedType deleted, AnimationCurve beforeCurve)
        {
            var hash = GetEditorCurveBindingHashCode(binding);
            Dictionary<int, OnCurveWasModifiedData> dic = !OnCurveWasModifiedStop ? curvesWasModified : curvesWasModifiedStopped;
            if (dic.TryGetValue(hash, out OnCurveWasModifiedData data))
            {
                if (data.deleted == AnimationUtility.CurveModifiedType.CurveModified &&
                    data.deleted != deleted)
                {
                    data.deleted = deleted;
                }
                if (data.beforeCurve == null && beforeCurve != null)
                {
                    data.beforeCurve = beforeCurve;
                }
            }
            else
            {
                dic.Add(hash, new OnCurveWasModifiedData() { binding = binding, deleted = deleted, beforeCurve = beforeCurve });
            }
        }
        private void SetOnCurveWasModifiedStop(bool flag)
        {
            OnCurveWasModifiedStop = flag;
            if (!flag)
            {
                foreach (var pair in curvesWasModifiedStopped)
                {
                    AddOnCurveWasModified(pair.Value.binding, pair.Value.deleted, pair.Value.beforeCurve);
                }
            }
            curvesWasModifiedStopped.Clear();
        }
        private void ResetOnCurveWasModifiedStop()
        {
            OnCurveWasModifiedStop = false;
            curvesWasModifiedStopped.Clear();
        }
        private void ActionCurrentChangedKeyframes(OnCurveWasModifiedData data, Action<AnimationCurve, int> action, bool valueCheckOnly)
        {
            var curve = GetEditorCurveCache(data.binding);
            if (curve != null)
            {
                if (data.beforeCurve != null)
                {
                    if (valueCheckOnly)
                    {
                        for (int i = 0; i < curve.length; i++)
                        {
                            if (AnimationCommon.FindKeyframeIndexValueOnly(data.beforeCurve, curve, i) < 0)
                            {
                                //Debug.LogFormat("<color=red>[FindKeyframeIndexValueOnly]</color>Found changed keyframe at time {0} in '{1}' '{2}' index {3}", curve[i].time, data.binding.path, data.binding.propertyName, i);
                                action(curve, i);
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < curve.length; i++)
                        {
                            if (AnimationCommon.FindKeyframeIndex(data.beforeCurve, curve, i) < 0)
                            {
                                //Debug.LogFormat("<color=blue>[FindKeyframeIndex]</color>Found changed keyframe at time {0} in '{1}' '{2}' index {3}", curve[i].time, data.binding.path, data.binding.propertyName, i);
                                action(curve, i);
                            }
                        }
                    }
                }
                else
                {
                    //Debug.LogWarningFormat("<color=blue>[Very Animation]</color>Lost before cache '{0}'", data.binding.path, data.binding.propertyName);
                }
            }
        }
        private void ActionBeforeChangedKeyframes(OnCurveWasModifiedData data, Action<AnimationCurve, int> action)
        {
            var curve = GetEditorCurveCache(data.binding);
            if (curve != null)
            {
                if (data.beforeCurve != null)
                {
                    for (int i = 0; i < data.beforeCurve.length; i++)
                    {
                        if (AnimationCommon.FindKeyframeAtTime(curve, data.beforeCurve[i].time) < 0)
                            action(data.beforeCurve, i);
                    }
                }
                else
                {
                    //Debug.LogWarningFormat("<color=blue>[Very Animation]</color>Lost before cache '{0}'", data.binding.path, data.binding.propertyName);
                }
            }
        }

        private bool IsCurvesWasModifiedChangeCurrentValue(OnCurveWasModifiedData data)
        {
            var currentCurve = GetEditorCurveCache(data.binding);
            if (data.beforeCurve == null && currentCurve != null)
                return true;
            else if (data.beforeCurve != null && currentCurve == null)
                return true;
            else if (data.beforeCurve == null && currentCurve == null)
                return false;
            var valueNow = currentCurve.Evaluate(CurrentTime);
            var valueBefore = data.beforeCurve.Evaluate(CurrentTime);
            return !Mathf.Approximately(valueBefore, valueNow);
        }

        #region EditorCurveBinding
        internal EditorCurveBinding[] AnimatorMuscleBindings { get; private set; }
        internal EditorCurveBinding[][] AnimatorIkTBindings { get; private set; }
        internal EditorCurveBinding[][] AnimatorIkQBindings { get; private set; }
        internal EditorCurveBinding[][] AnimatorTDOFBindings { get; private set; }
        private void CreateEditorCurveBindingPropertyNames()
        {
            {
                AnimatorMuscleBindings = new EditorCurveBinding[MusclePropertyName.PropertyNames.Length];
                for (int i = 0; i < MusclePropertyName.PropertyNames.Length; i++)
                {
                    AnimatorMuscleBindings[i] = EditorCurveBinding.FloatCurve("", typeof(Animator), MusclePropertyName.PropertyNames[i]);
                }
            }
            {
                AnimatorIkTBindings = new EditorCurveBinding[(int)AnimatorIKIndex.Total][];
                AnimatorIkQBindings = new EditorCurveBinding[(int)AnimatorIKIndex.Total][];
                for (int i = 0; i < (int)AnimatorIKIndex.Total; i++)
                {
                    AnimatorIkTBindings[i] = new EditorCurveBinding[3];
                    for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                        AnimatorIkTBindings[i][dofIndex] = EditorCurveBinding.FloatCurve("", typeof(Animator), $"{(AnimatorIKIndex)i}T{AnimationCommon.PropertyName.DotDof[dofIndex]}");
                    AnimatorIkQBindings[i] = new EditorCurveBinding[4];
                    for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                        AnimatorIkQBindings[i][dofIndex] = EditorCurveBinding.FloatCurve("", typeof(Animator), $"{(AnimatorIKIndex)i}Q{AnimationCommon.PropertyName.DotDof[dofIndex]}");
                }
            }
            {
                AnimatorTDOFBindings = new EditorCurveBinding[(int)AnimatorTDOFIndex.Total][];
                for (int i = 0; i < (int)AnimatorTDOFIndex.Total; i++)
                {
                    AnimatorTDOFBindings[i] = new EditorCurveBinding[3];
                    for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                        AnimatorTDOFBindings[i][dofIndex] = EditorCurveBinding.FloatCurve("", typeof(Animator), $"{AnimatorTDOFIndex2HumanBodyBones[i]}TDOF{AnimationCommon.PropertyName.DotDof[dofIndex]}");
                }
            }
        }
        public EditorCurveBinding AnimationCurveBindingAnimatorCustom(string propertyName)
        {
            return EditorCurveBinding.FloatCurve("", typeof(Animator), propertyName);
        }
        public EditorCurveBinding AnimationCurveBindingTransformPosition(int boneIndex, int dofIndex)
        {
            return EditorCurveBinding.FloatCurve(BonePaths[boneIndex], typeof(Transform), AnimationCommon.PropertyName.Position[dofIndex]);
        }
        public EditorCurveBinding AnimationCurveBindingTransformRotation(int boneIndex, int dofIndex, URotationCurveInterpolation.Mode mode)
        {
            return EditorCurveBinding.FloatCurve(BonePaths[boneIndex], typeof(Transform), AnimationCommon.PropertyName.Rotation[(int)mode][dofIndex]);
        }
        public EditorCurveBinding AnimationCurveBindingTransformScale(int boneIndex, int dofIndex)
        {
            return EditorCurveBinding.FloatCurve(BonePaths[boneIndex], typeof(Transform), AnimationCommon.PropertyName.Scale[dofIndex]);
        }
        public EditorCurveBinding AnimationCurveBindingBlendShape(SkinnedMeshRenderer renderer, string name)
        {
            return EditorCurveBinding.FloatCurve(GetGameObjectPath(renderer.gameObject), typeof(SkinnedMeshRenderer), $"blendShape.{name}");
        }
        public EditorCurveBinding AnimationCurveBindingCustomProperty(int boneIndex, Type type, string propertyName)
        {
            return EditorCurveBinding.FloatCurve(BonePaths[boneIndex], type, propertyName);
        }

        public int GetBoneIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type == typeof(Animator))
            {
                AnimatorIKIndex ikIndex;
                AnimatorTDOFIndex tdofIndex;
                int muscleIndex;
                if (IsAnimatorRootCurveBinding(binding) ||
                    IsAnimatorMotionCurveBinding(binding))
                {
                    return 0;
                }
                else if ((ikIndex = GetIkTIndexFromCurveBinding(binding)) > AnimatorIKIndex.None ||
                        (ikIndex = GetIkQIndexFromCurveBinding(binding)) > AnimatorIKIndex.None)
                {
                    var humanoidIndex = (int)AnimatorIKIndex2HumanBodyBones[(int)ikIndex];
                    if (humanoidIndex >= 0)
                        return HumanoidIndex2boneIndex[humanoidIndex];
                }
                else if ((tdofIndex = GetTDOFIndexFromCurveBinding(binding)) >= 0)
                {
                    var humanoidIndex = (int)AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex];
                    if (humanoidIndex >= 0)
                        return HumanoidIndex2boneIndex[humanoidIndex];
                }
                else if ((muscleIndex = GetMuscleIndexFromCurveBinding(binding)) >= 0)
                {
                    var humanoidIndex = HumanTrait.BoneFromMuscle(muscleIndex);
                    if (humanoidIndex >= 0)
                        return HumanoidIndex2boneIndex[humanoidIndex];
                }
            }
            return GetBoneIndexFromPath(binding.path);
        }
        public int GetBoneIndexFromPath(string path)
        {
            if (BonePathDictionary.TryGetValue(path, out int boneIndex))
            {
                return boneIndex;
            }
            return -1;
        }
        public int GetRootTDofIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) return -1;
            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
            {
                if (binding == AnimationCommon.Binding.RootT[dofIndex])
                    return dofIndex;
            }
            return -1;
        }
        public int GetRootQDofIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) return -1;
            for (int dofIndex = 0; dofIndex < 4; dofIndex++)
            {
                if (binding == AnimationCommon.Binding.RootQ[dofIndex])
                    return dofIndex;
            }
            return -1;
        }
        public int GetMotionTDofIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) return -1;
            for (int dofIndex = 0; dofIndex < 3; dofIndex++)
            {
                if (binding == AnimationCommon.Binding.MotionT[dofIndex])
                    return dofIndex;
            }
            return -1;
        }
        public int GetMotionQDofIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) return -1;
            for (int dofIndex = 0; dofIndex < 4; dofIndex++)
            {
                if (binding == AnimationCommon.Binding.MotionQ[dofIndex])
                    return dofIndex;
            }
            return -1;
        }
        public int GetMuscleIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) return -1;
            return GetMuscleIndexFromPropertyName(binding.propertyName);
        }
        public int GetMuscleIndexFromPropertyName(string propertyName)
        {
            if (MusclePropertyName.PropertyNameDic.TryGetValue(propertyName, out int muscleIndex))
            {
                return muscleIndex;
            }
            return -1;
        }
        public AnimatorIKIndex GetIkTIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) 
                return AnimatorIKIndex.None;
            for (int ikIndex = 0; ikIndex < (int)AnimatorIKIndex.Total; ikIndex++)
            {
                for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                {
                    if (binding == AnimatorIkTBindings[ikIndex][dofIndex])
                        return (AnimatorIKIndex)ikIndex;
                }
            }
            return AnimatorIKIndex.None;
        }
        public AnimatorIKIndex GetIkQIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) 
                return AnimatorIKIndex.None;
            for (int ikIndex = 0; ikIndex < (int)AnimatorIKIndex.Total; ikIndex++)
            {
                for (int dofIndex = 0; dofIndex < 4; dofIndex++)
                {
                    if (binding == AnimatorIkQBindings[ikIndex][dofIndex])
                        return (AnimatorIKIndex)ikIndex;
                }
            }
            return AnimatorIKIndex.None;
        }
        public AnimatorTDOFIndex GetTDOFIndexFromCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Animator)) return AnimatorTDOFIndex.None;
            var indexOf = binding.propertyName.IndexOf("TDOF.", StringComparison.Ordinal);
            if (indexOf < 0) return AnimatorTDOFIndex.None;
            var name = binding.propertyName[..indexOf];
            for (int tdofIndex = 0; tdofIndex < (int)AnimatorTDOFIndex.Total; tdofIndex++)
            {
                if (name == AnimatorTDOFIndexStrings[tdofIndex])
                    return (AnimatorTDOFIndex)tdofIndex;
            }
            return AnimatorTDOFIndex.None;
        }
        public EditorCurveBinding? GetMirrorAnimationCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type == typeof(Animator))
            {
                int muscleIndex;
                AnimatorIKIndex ikTIndex, ikQIndex;
                AnimatorTDOFIndex tdofIndex;
                if (IsAnimatorRootCurveBinding(binding))
                {
                    return null;
                }
                else if ((muscleIndex = GetMuscleIndexFromCurveBinding(binding)) >= 0)
                {
                    var mmuscleIndex = GetMirrorMuscleIndex(muscleIndex);
                    if (mmuscleIndex < 0) return null;
                    return AnimatorMuscleBindings[mmuscleIndex];
                }
                else if ((ikTIndex = GetIkTIndexFromCurveBinding(binding)) != AnimatorIKIndex.None)
                {
                    var mikTIndex = AnimatorIKMirrorIndexes[(int)ikTIndex];
                    if (mikTIndex < 0) return null;
                    var dofIndex = AnimationCommon.GetDOFIndex(binding);
                    if (dofIndex < 0) return null;
                    return AnimatorIkTBindings[(int)mikTIndex][dofIndex];
                }
                else if ((ikQIndex = GetIkQIndexFromCurveBinding(binding)) != AnimatorIKIndex.None)
                {
                    var mikQIndex = AnimatorIKMirrorIndexes[(int)ikQIndex];
                    if (mikQIndex < 0) return null;
                    var dofIndex = AnimationCommon.GetDOFIndex(binding);
                    if (dofIndex < 0) return null;
                    var mdof = QuaternionXMirrorSwapDof[dofIndex];
                    return AnimatorIkQBindings[(int)mikQIndex][mdof];
                }
                else if ((tdofIndex = GetTDOFIndexFromCurveBinding(binding)) != AnimatorTDOFIndex.None)
                {
                    var mtdofIndex = AnimatorTDOFMirrorIndexes[(int)tdofIndex];
                    if (mtdofIndex < 0) return null;
                    var dofIndex = AnimationCommon.GetDOFIndex(binding);
                    if (dofIndex < 0) return null;
                    return AnimatorTDOFBindings[(int)mtdofIndex][dofIndex];
                }
                else
                {
                    return null;
                }
            }
            else if (binding.type == typeof(Transform))
            {
                var boneIndex = GetBoneIndexFromCurveBinding(binding);
                if (boneIndex < 0) return null;
                if (MirrorBoneIndexes[boneIndex] < 0) return null;
                binding.path = BonePaths[MirrorBoneIndexes[boneIndex]];
                if (!VAW.EditorSettings.SettingGenericMirrorScale)
                {
                    if (IsTransformScaleCurveBinding(binding))
                        return null;
                }
                return binding;
            }
            else if (IsSkinnedMeshRendererBlendShapeCurveBinding(binding))
            {
                var boneIndex = GetBoneIndexFromCurveBinding(binding);
                if (boneIndex < 0) return null;
                if (!Bones[boneIndex].TryGetComponent<SkinnedMeshRenderer>(out var renderer)) return null;
                if (!MirrorBlendShape.TryGetValue(renderer, out Dictionary<string, string> nameTable)) return null;
                if (!nameTable.TryGetValue(AnimationCommon.PropertyName2BlendShapeName(binding.propertyName), out string mirrorName)) return null;
                binding.propertyName = AnimationCommon.BlendShapeName2PropertyName(mirrorName);
                return binding;
            }
            else
            {
                return null;
            }
        }

        public bool IsAnimatorRootCurveBinding(EditorCurveBinding binding)
        {
            return (GetRootTDofIndexFromCurveBinding(binding) >= 0 ||
                    GetRootQDofIndexFromCurveBinding(binding) >= 0);
        }
        public bool IsAnimatorMotionCurveBinding(EditorCurveBinding binding)
        {
            return (GetMotionTDofIndexFromCurveBinding(binding) >= 0 ||
                    GetMotionQDofIndexFromCurveBinding(binding) >= 0);
        }
        public bool IsAnimatorReservedPropertyName(string propertyName)
        {
            for (int dof = 0; dof < 3; dof++)
            {
                if (propertyName == AnimationCommon.Binding.RootT[dof].propertyName)
                    return true;
            }
            for (int dof = 0; dof < 4; dof++)
            {
                if (propertyName == AnimationCommon.Binding.RootQ[dof].propertyName)
                    return true;
            }
            for (int dof = 0; dof < 3; dof++)
            {
                if (propertyName == AnimationCommon.Binding.MotionT[dof].propertyName)
                    return true;
            }
            for (int dof = 0; dof < 4; dof++)
            {
                if (propertyName == AnimationCommon.Binding.MotionQ[dof].propertyName)
                    return true;
            }
            for (var i = 0; i < (int)AnimatorIKIndex.Total; i++)
            {
                for (int dof = 0; dof < 3; dof++)
                {
                    if (propertyName == AnimatorIkTBindings[i][dof].propertyName)
                        return true;
                }
                for (int dof = 0; dof < 4; dof++)
                {
                    if (propertyName == AnimatorIkQBindings[i][dof].propertyName)
                        return true;
                }
            }
            for (var i = 0; i < (int)AnimatorTDOFIndex.Total; i++)
            {
                for (int dof = 0; dof < 3; dof++)
                {
                    if (propertyName == AnimatorTDOFBindings[i][dof].propertyName)
                        return true;
                }
            }
            if (GetMuscleIndexFromPropertyName(propertyName) >= 0)
                return true;

            return false;
        }
        public bool IsTransformPositionCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Transform)) return false;
            return binding.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal);
        }
        public bool IsTransformRotationCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Transform)) return false;
            for (int i = 0; i < URotationCurveInterpolation.PrefixForInterpolation.Length; i++)
            {
                if (URotationCurveInterpolation.PrefixForInterpolation[i] == null) continue;
                if (binding.propertyName.StartsWith(URotationCurveInterpolation.PrefixForInterpolation[i], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
        public bool IsTransformScaleCurveBinding(EditorCurveBinding binding)
        {
            if (binding.type != typeof(Transform)) return false;
            return binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal);
        }
        public bool IsSkinnedMeshRendererBlendShapeCurveBinding(EditorCurveBinding binding)
        {
            return binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);
        }
        #endregion

        #region HumanPose

        public void GetHumanPoseCurve(ref HumanPose humanPose, float time = -1f)
        {
            humanPose.bodyPosition = GetAnimationValueAnimatorRootT(time);
            humanPose.bodyRotation = GetAnimationValueAnimatorRootQ(time);
            humanPose.muscles = new float[HumanTrait.MuscleCount];
            for (int i = 0; i < humanPose.muscles.Length; i++)
            {
                humanPose.muscles[i] = GetAnimationValueAnimatorMuscle(i, time);
            }
        }
        #endregion

        #region EditorCurveCache
        #region EditorCurveBindingCache
        private Dictionary<string, Dictionary<Type, Dictionary<string, int>>> editorCurveBindingHashCacheDic;
        private void ClearEditorCurveBindingHashCode()
        {
            editorCurveBindingHashCacheDic ??= new Dictionary<string, Dictionary<Type, Dictionary<string, int>>>();
            editorCurveBindingHashCacheDic.Clear();
        }
        public int GetEditorCurveBindingHashCode(EditorCurveBinding binding)
        {
            if (binding.path == null || binding.propertyName == null)
                return binding.GetHashCode();

            editorCurveBindingHashCacheDic ??= new Dictionary<string, Dictionary<Type, Dictionary<string, int>>>();

            int hashCode;
            if (editorCurveBindingHashCacheDic.TryGetValue(binding.path, out Dictionary<Type, Dictionary<string, int>> typeNameDic))
            {
                if (typeNameDic.TryGetValue(binding.type, out Dictionary<string, int> propertyNameDic))
                {
                    if (propertyNameDic.TryGetValue(binding.propertyName, out hashCode))
                    {
                        return hashCode;
                    }
                    else
                    {
                        hashCode = binding.GetHashCode();
                        propertyNameDic.Add(binding.propertyName, hashCode);
                    }
                }
                else
                {
                    propertyNameDic = new Dictionary<string, int>();
                    hashCode = binding.GetHashCode();
                    propertyNameDic.Add(binding.propertyName, hashCode);
                    typeNameDic.Add(binding.type, propertyNameDic);
                }
            }
            else
            {
                typeNameDic = new Dictionary<Type, Dictionary<string, int>>();
                hashCode = binding.GetHashCode();
                {
                    var propertyNameDic = new Dictionary<string, int>
                    {
                        { binding.propertyName, hashCode }
                    };
                    typeNameDic.Add(binding.type, propertyNameDic);
                }
                editorCurveBindingHashCacheDic.Add(binding.path, typeNameDic);
            }

            return hashCode;
        }
        #endregion

        private AnimationClip editorCurveCacheClip;
        private bool editorCurveCacheDirty;
        private class EditorCurveCacheDicData
        {
            public EditorCurveCacheDicData(AnimationCurve curve)
            {
                this.curve = curve;
                beforeKeys = Array.Empty<Keyframe>();
            }

            public AnimationCurve curve;
            public Keyframe[] beforeKeys;
        }
        private Dictionary<int, EditorCurveCacheDicData> editorCurveCacheDic;

        private Dictionary<int, EditorCurveBinding> editorCurveDelayWriteDic;

        private struct EditorCurveWasModifiedDicData
        {
            public EditorCurveBinding binding;
            public AnimationUtility.CurveModifiedType type;
        }
        private Dictionary<int, EditorCurveWasModifiedDicData> editorCurveWasModifiedDic;

        public void ClearEditorCurveCache()
        {
            ClearEditorCurveBindingHashCode();

            editorCurveCacheClip = null;
            editorCurveCacheDic ??= new Dictionary<int, EditorCurveCacheDicData>();
            editorCurveCacheDic.Clear();

            editorCurveCacheDirty = true;
        }
        private void RemoveEditorCurveCache(EditorCurveBinding binding)
        {
            CheckChangeClipClearEditorCurveCache();
            if (editorCurveCacheDic == null) return;
            editorCurveCacheDic.Remove(GetEditorCurveBindingHashCode(binding));
        }
        private bool IsCheckChangeClipClearEditorCurveCache(AnimationClip clip)
        {
            return clip == editorCurveCacheClip;
        }
        private void CheckChangeClipClearEditorCurveCache()
        {
            if (!IsCheckChangeClipClearEditorCurveCache(CurrentClip))
            {
                ClearEditorCurveCache();
                editorCurveCacheClip = CurrentClip;
            }
        }
        private bool IsContainsEditorCurveCache(EditorCurveBinding binding)
        {
            CheckChangeClipClearEditorCurveCache();
            if (editorCurveCacheDic == null)
                return false;
            var hash = GetEditorCurveBindingHashCode(binding);
            return editorCurveCacheDic.ContainsKey(hash);
        }
        private AnimationCurve GetEditorCurveCache(EditorCurveBinding binding)
        {
            CheckChangeClipClearEditorCurveCache();
            if (editorCurveCacheDic == null)
                return null;
            var hash = GetEditorCurveBindingHashCode(binding);
            if (!editorCurveCacheDic.TryGetValue(hash, out EditorCurveCacheDicData data))
            {
                var curve = AnimationUtility.GetEditorCurve(CurrentClip, binding);     //If an error occurs on this line, execute Tools/Fix Errors.
                data = new EditorCurveCacheDicData(curve);
                if (curve != null)
                {
                    int len = curve.length;
                    if (data.beforeKeys.Length != len)
                        data.beforeKeys = new Keyframe[len];
                    for (int i = 0; i < len; i++)
                        data.beforeKeys[i] = curve[i];
                }
                editorCurveCacheDic.Add(hash, data);
            }
            return data.curve;
        }
        private void SetEditorCurveCache(EditorCurveBinding binding, AnimationCurve curve)
        {
            CheckChangeClipClearEditorCurveCache();
            editorCurveCacheDic ??= new Dictionary<int, EditorCurveCacheDicData>();
            editorCurveDelayWriteDic ??= new Dictionary<int, EditorCurveBinding>();
            editorCurveWasModifiedDic ??= new Dictionary<int, EditorCurveWasModifiedDicData>();
            var hash = GetEditorCurveBindingHashCode(binding);
            editorCurveDelayWriteDic[hash] = binding;
            if (!editorCurveCacheDic.TryGetValue(hash, out EditorCurveCacheDicData data))
            {
                data = new EditorCurveCacheDicData(curve);
            }
            else
            {
                data.curve = curve;
            }
            {
                var type = curve != null ? AnimationUtility.CurveModifiedType.CurveModified : AnimationUtility.CurveModifiedType.CurveDeleted;
                if (binding.type == typeof(Transform))
                {
                    if (binding.propertyName.StartsWith(URotationCurveInterpolation.PrefixForInterpolation[(int)URotationCurveInterpolation.Mode.RawQuaternions], StringComparison.Ordinal))
                    {
                        var bindingSub = binding;
                        for (int dof = 0; dof < 3; dof++)
                        {
                            bindingSub.propertyName = AnimationCommon.PropertyName.Rotation[(int)URotationCurveInterpolation.Mode.NonBaked][dof];
                            RemoveEditorCurveCache(bindingSub);
                            editorCurveWasModifiedDic[GetEditorCurveBindingHashCode(bindingSub)] = new EditorCurveWasModifiedDicData() { binding = bindingSub, type = type };
                        }
                    }
                    else if (binding.propertyName.StartsWith(URotationCurveInterpolation.PrefixForInterpolation[(int)URotationCurveInterpolation.Mode.NonBaked], StringComparison.Ordinal))
                    {
                        var bindingSub = binding;
                        for (int dof = 0; dof < 4; dof++)
                        {
                            bindingSub.propertyName = AnimationCommon.PropertyName.Rotation[(int)URotationCurveInterpolation.Mode.RawQuaternions][dof];
                            RemoveEditorCurveCache(bindingSub);
                        }
                        editorCurveWasModifiedDic[hash] = new EditorCurveWasModifiedDicData() { binding = binding, type = type };
                    }
                    else
                    {
                        editorCurveWasModifiedDic[hash] = new EditorCurveWasModifiedDicData() { binding = binding, type = type };
                    }
                }
                else
                {
                    editorCurveWasModifiedDic[hash] = new EditorCurveWasModifiedDicData() { binding = binding, type = type };
                }
                {
                    AddOnCurveWasModified(binding, type, new AnimationCurve(data.beforeKeys));
                }
            }
            if (curve != null)
            {
                int len = curve.length;
                if (data.beforeKeys.Length != len)
                    data.beforeKeys = new Keyframe[len];
                for (int i = 0; i < len; i++)
                    data.beforeKeys[i] = curve[i];
            }
            else
            {
                data.beforeKeys = Array.Empty<Keyframe>();
            }
            editorCurveCacheDic[hash] = data;
        }
        private AnimationCurve GetOrCreateEditorCurveCache(EditorCurveBinding binding, float defaultValue, bool notNull)
        {
            var curve = GetEditorCurveCache(binding);
            if (curve == null && notNull)
            {
                curve = new();
                AnimationCommon.SetKeyframe(curve, 0f, defaultValue);
                AnimationCommon.SetKeyframe(curve, Mathf.Max(CurrentTime, GetTotalClipLength()), defaultValue);
                SetEditorCurveCache(binding, curve);
                //Created
                UAw.ForceRefresh();
                SetAnimationWindowSynchroSelection();
            }
            return curve;
        }
        public void UpdateSyncEditorCurveClip()
        {
            if (editorCurveDelayWriteDic != null && editorCurveDelayWriteDic.Count > 0)
            {
                bool refreshAw = false;
                bool updated = false;
                foreach (var pair in editorCurveDelayWriteDic)
                {
                    if (!editorCurveCacheDic.TryGetValue(pair.Key, out EditorCurveCacheDicData data))
                        continue;
                    UAnimationUtility.Internal_SetEditorCurve(editorCurveCacheClip, pair.Value, data.curve, false);
                    updated = true;
                    if (!refreshAw &&
                        UAw.IsSelectedItemCurvesDummySwapped &&
                        !UAw.ContainsSelectedItemCurvesDummySwapped(pair.Value))
                    {
                        refreshAw = true;
                    }
                }
                if (updated)
                {
                    UAnimationUtility.Internal_SyncEditorCurves(editorCurveCacheClip);
                }
                if (refreshAw)
                {
                    UAw.ClearSelectedItemCurvesDummySwapped();
                }
                editorCurveDelayWriteDic.Clear();
            }

            if (editorCurveWasModifiedDic != null && editorCurveWasModifiedDic.Count > 0)
            {
                var bindings = new List<EditorCurveBinding>();
                OnCurveWasModifiedIgnore = true;
                try
                {
                    foreach (var pair in editorCurveWasModifiedDic)
                    {
                        UAnimationUtility.Internal_InvokeOnCurveWasModified(editorCurveCacheClip, pair.Value.binding, pair.Value.type);

                        #region PropertyFilterByBindings
                        if (VAW.EditorSettings.SettingPropertyStyle == EditorSettings.PropertyStyle.Filter)
                        {
                            if (pair.Value.type == AnimationUtility.CurveModifiedType.CurveModified &&
                                animationWindowFilterBindings != null &&
                                !animationWindowFilterBindings.Contains(pair.Value.binding))
                            {
                                animationWindowFilterBindings.Add(pair.Value.binding);
                                bindings.Add(pair.Value.binding);
                            }
                        }
                        #endregion
                    }
                    editorCurveWasModifiedDic.Clear();
                }
                finally
                {
                    OnCurveWasModifiedIgnore = false;
                }
                if (bindings.Count > 0)
                    SetAnimationWindowSynchroSelection(bindings);
            }
        }
        #endregion

        #region PoseTemplate
        [Flags]
        public enum PoseFlags : uint
        {
            Humanoid = (1 << 0),
            Generic = (1 << 1),
            BlendShape = (1 << 2),
            All = uint.MaxValue,
        }
        public void SavePoseTemplate(PoseTemplate poseTemplate, PoseFlags flags = PoseFlags.All)
        {
            poseTemplate.Reset();
            poseTemplate.isHuman = IsHuman;
            #region Humanoid
            if (IsHuman && (flags & PoseFlags.Humanoid) != 0)
            {
                poseTemplate.haveRootT = true;
                poseTemplate.rootT = GetAnimationValueAnimatorRootT();
                poseTemplate.haveRootQ = true;
                poseTemplate.rootQ = GetAnimationValueAnimatorRootQ();
                {
                    var muscleList = new Dictionary<string, float>();
                    for (int muscleIndex = 0; muscleIndex < MusclePropertyName.PropertyNames.Length; muscleIndex++)
                        muscleList.Add(MusclePropertyName.PropertyNames[muscleIndex], GetAnimationValueAnimatorMuscle(muscleIndex));
                    poseTemplate.musclePropertyNames = EditorCommon.CopyArrayOrNull(muscleList.Keys);
                    poseTemplate.muscleValues = EditorCommon.CopyArrayOrNull(muscleList.Values);
                }
                {
                    var tdofIndices = new Dictionary<AnimatorTDOFIndex, Vector3>();
                    for (AnimatorTDOFIndex tdofIndex = 0; tdofIndex < AnimatorTDOFIndex.Total; tdofIndex++)
                        tdofIndices.Add(tdofIndex, GetAnimationValueAnimatorTDOF(tdofIndex));
                    poseTemplate.tdofIndices = EditorCommon.CopyArrayOrNull(tdofIndices.Keys);
                    poseTemplate.tdofValues = EditorCommon.CopyArrayOrNull(tdofIndices.Values);
                }
                {
                    var ikIndices = new Dictionary<AnimatorIKIndex, PoseTemplate.IKData>();
                    for (AnimatorIKIndex ikIndex = 0; ikIndex < AnimatorIKIndex.Total; ikIndex++)
                    {
                        ikIndices.Add(ikIndex, new PoseTemplate.IKData()
                        {
                            position = GetAnimationValueAnimatorIkT(ikIndex),
                            rotation = GetAnimationValueAnimatorIkQ(ikIndex),
                        });
                    }
                    poseTemplate.ikIndices = EditorCommon.CopyArrayOrNull(ikIndices.Keys);
                    poseTemplate.ikValues = EditorCommon.CopyArrayOrNull(ikIndices.Values);
                }
            }
            #endregion
            #region Generic
            if ((flags & PoseFlags.Generic) != 0)
            {
                var transformList = new Dictionary<string, PoseTemplate.TransformData>();
                for (int boneIndex = 0; boneIndex < Bones.Length; boneIndex++)
                {
                    if (transformList.ContainsKey(BonePaths[boneIndex]))
                        continue;
                    if (IsConflictBone(boneIndex))
                    {
                        var t = Skeleton.Bones[boneIndex].transform;
                        transformList.Add(BonePaths[boneIndex], new PoseTemplate.TransformData()
                        {
                            position = t.localPosition,
                            rotation = t.localRotation,
                            scale = t.localScale,
                        });
                    }
                    else
                    {
                        transformList.Add(BonePaths[boneIndex], new PoseTemplate.TransformData()
                        {
                            position = GetAnimationValueTransformPosition(boneIndex),
                            rotation = GetAnimationValueTransformRotation(boneIndex),
                            scale = GetAnimationValueTransformScale(boneIndex),
                        });
                    }
                }
                poseTemplate.transformPaths = EditorCommon.CopyArrayOrNull(transformList.Keys);
                poseTemplate.transformValues = EditorCommon.CopyArrayOrNull(transformList.Values);
            }
            #endregion
            #region BlendShape
            if ((flags & PoseFlags.BlendShape) != 0)
            {
                var blendShapeList = new Dictionary<string, PoseTemplate.BlendShapeData>();
                foreach (var renderer in Renderers)
                {
                    var smr = renderer as SkinnedMeshRenderer;
                    if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount <= 0) continue;
                    var mesh = smr.sharedMesh;
                    var path = GetGameObjectPath(smr.gameObject);
                    if (blendShapeList.ContainsKey(path))
                        continue;
                    var data = new PoseTemplate.BlendShapeData()
                    {
                        names = new string[mesh.blendShapeCount],
                        weights = new float[mesh.blendShapeCount],
                    };
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        data.names[i] = mesh.GetBlendShapeName(i);
                        data.weights[i] = GetAnimationValueBlendShape(smr, data.names[i]);
                    }
                    blendShapeList.Add(path, data);
                }
                poseTemplate.blendShapePaths = EditorCommon.CopyArrayOrNull(blendShapeList.Keys);
                poseTemplate.blendShapeValues = EditorCommon.CopyArrayOrNull(blendShapeList.Values);
            }
            #endregion
        }
        public void SaveSelectionPoseTemplate(PoseTemplate poseTemplate, PoseFlags flags = PoseFlags.All)
        {
            bool selectRoot = SelectionGameObjectsIndexOf(VAW.GameObject) >= 0;
            var selectHumanoidIndexes = SelectionGameObjectsHumanoidIndex();
            var selectMuscleIndexes = SelectionGameObjectsMuscleIndex();
            var selectAnimatorIKTargetsHumanoidIndexes = animatorIK.SelectionAnimatorIKTargetsHumanoidIndexes();
            var selectOriginalIKTargetsBoneIndexes = originalIK.SelectionOriginalIKTargetsBoneIndexes();
            //
            poseTemplate.Reset();
            poseTemplate.isHuman = IsHuman;
            #region Humanoid
            if (IsHuman && (flags & PoseFlags.Humanoid) != 0)
            {
                if (selectRoot)
                {
                    poseTemplate.haveRootT = true;
                    poseTemplate.rootT = GetAnimationValueAnimatorRootT();
                    poseTemplate.haveRootQ = true;
                    poseTemplate.rootQ = GetAnimationValueAnimatorRootQ();
                }
                {
                    var muscleList = new Dictionary<string, float>();
                    for (int muscleIndex = 0; muscleIndex < MusclePropertyName.PropertyNames.Length; muscleIndex++)
                    {
                        if (selectMuscleIndexes.Contains(muscleIndex) ||
                            selectAnimatorIKTargetsHumanoidIndexes.Contains((HumanBodyBones)HumanTrait.BoneFromMuscle(muscleIndex)))
                        {
                            muscleList.Add(MusclePropertyName.PropertyNames[muscleIndex], GetAnimationValueAnimatorMuscle(muscleIndex));
                        }
                    }
                    poseTemplate.musclePropertyNames = EditorCommon.CopyArrayOrNull(muscleList.Keys);
                    poseTemplate.muscleValues = EditorCommon.CopyArrayOrNull(muscleList.Values);
                }
                {
                    var tdofIndices = new Dictionary<AnimatorTDOFIndex, Vector3>();
                    for (AnimatorTDOFIndex tdofIndex = 0; tdofIndex < AnimatorTDOFIndex.Total; tdofIndex++)
                    {
                        if (selectHumanoidIndexes.Contains(AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex]) ||
                            selectAnimatorIKTargetsHumanoidIndexes.Contains(AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex]))
                        {
                            tdofIndices.Add(tdofIndex, GetAnimationValueAnimatorTDOF(tdofIndex));
                        }
                    }
                    poseTemplate.tdofIndices = EditorCommon.CopyArrayOrNull(tdofIndices.Keys);
                    poseTemplate.tdofValues = EditorCommon.CopyArrayOrNull(tdofIndices.Values);
                }
                {
                    var ikIndices = new Dictionary<AnimatorIKIndex, PoseTemplate.IKData>();
                    for (AnimatorIKIndex ikIndex = 0; ikIndex < AnimatorIKIndex.Total; ikIndex++)
                    {
                        if (selectHumanoidIndexes.Contains(AnimatorIKIndex2HumanBodyBones[(int)ikIndex]) ||
                            selectAnimatorIKTargetsHumanoidIndexes.Contains(AnimatorIKIndex2HumanBodyBones[(int)ikIndex]))
                        {
                            ikIndices.Add(ikIndex, new PoseTemplate.IKData()
                            {
                                position = GetAnimationValueAnimatorIkT(ikIndex),
                                rotation = GetAnimationValueAnimatorIkQ(ikIndex),
                            });
                        }
                    }
                    poseTemplate.ikIndices = EditorCommon.CopyArrayOrNull(ikIndices.Keys);
                    poseTemplate.ikValues = EditorCommon.CopyArrayOrNull(ikIndices.Values);
                }
            }
            #endregion
            #region Generic
            if ((flags & PoseFlags.Generic) != 0)
            {
                var transformList = new Dictionary<string, PoseTemplate.TransformData>();
                for (int boneIndex = 0; boneIndex < Bones.Length; boneIndex++)
                {
                    if (SelectionBones.Contains(boneIndex) ||
                        selectOriginalIKTargetsBoneIndexes.Contains(boneIndex))
                    {
                        if (transformList.ContainsKey(BonePaths[boneIndex]))
                            continue;
                        if (IsConflictBone(boneIndex))
                        {
                            var t = Skeleton.Bones[boneIndex].transform;
                            transformList.Add(BonePaths[boneIndex], new PoseTemplate.TransformData()
                            {
                                position = t.localPosition,
                                rotation = t.localRotation,
                                scale = t.localScale,
                            });
                        }
                        else
                        {
                            transformList.Add(BonePaths[boneIndex], new PoseTemplate.TransformData()
                            {
                                position = GetAnimationValueTransformPosition(boneIndex),
                                rotation = GetAnimationValueTransformRotation(boneIndex),
                                scale = GetAnimationValueTransformScale(boneIndex),
                            });
                        }
                    }
                }
                poseTemplate.transformPaths = EditorCommon.CopyArrayOrNull(transformList.Keys);
                poseTemplate.transformValues = EditorCommon.CopyArrayOrNull(transformList.Values);
            }
            #endregion
            #region BlendShape
            if ((flags & PoseFlags.BlendShape) != 0)
            {
                var blendShapeList = new Dictionary<string, PoseTemplate.BlendShapeData>();
                foreach (var boneIndex in SelectionBones)
                {
                    var renderer = Bones[boneIndex].GetComponent<SkinnedMeshRenderer>();
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount <= 0) continue;
                    var path = BonePaths[boneIndex];
                    if (blendShapeList.ContainsKey(path))
                        continue;
                    var data = new PoseTemplate.BlendShapeData()
                    {
                        names = new string[renderer.sharedMesh.blendShapeCount],
                        weights = new float[renderer.sharedMesh.blendShapeCount],
                    };
                    for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    {
                        data.names[i] = renderer.sharedMesh.GetBlendShapeName(i);
                        data.weights[i] = GetAnimationValueBlendShape(renderer, data.names[i]);
                    }
                    blendShapeList.Add(path, data);
                }
                poseTemplate.blendShapePaths = EditorCommon.CopyArrayOrNull(blendShapeList.Keys);
                poseTemplate.blendShapeValues = EditorCommon.CopyArrayOrNull(blendShapeList.Values);
            }
            #endregion
        }
        public void LoadPoseTemplate(PoseTemplate poseTemplate, PoseFlags flags = PoseFlags.All, bool calcIK = false, bool blendShapeAdd = false)
        {
            if (!SetPoseBefore("Set Pose Template"))
                return;
            #region Humanoid
            if ((flags & PoseFlags.Humanoid) != 0)
            {
                if (IsHuman && poseTemplate.isHuman)
                {
                    if (poseTemplate.haveRootT)
                    {
                        SetAnimationValueAnimatorRootTIfNotOriginal(poseTemplate.rootT);
                    }
                    if (poseTemplate.haveRootQ)
                    {
                        SetAnimationValueAnimatorRootQIfNotOriginal(poseTemplate.rootQ);
                    }
                    if (poseTemplate.musclePropertyNames != null && poseTemplate.muscleValues != null)
                    {
                        Assert.IsTrue(poseTemplate.musclePropertyNames.Length == poseTemplate.muscleValues.Length);
                        for (int i = 0; i < poseTemplate.musclePropertyNames.Length; i++)
                        {
                            var muscleIndex = GetMuscleIndexFromPropertyName(poseTemplate.musclePropertyNames[i]);
                            if (muscleIndex < 0) continue;
                            SetAnimationValueAnimatorMuscleIfNotOriginal(muscleIndex, poseTemplate.muscleValues[i]);
                        }
                    }
                    if (poseTemplate.tdofIndices != null && poseTemplate.tdofValues != null)
                    {
                        Assert.IsTrue(poseTemplate.tdofIndices.Length == poseTemplate.tdofValues.Length);
                        for (int i = 0; i < poseTemplate.tdofIndices.Length; i++)
                        {
                            var tdofIndex = poseTemplate.tdofIndices[i];
                            var value = poseTemplate.tdofValues[i];
                            SetAnimationValueAnimatorTDOFIfNotOriginal(tdofIndex, value);
                        }
                    }
                    if (poseTemplate.ikIndices != null && poseTemplate.ikValues != null)
                    {
                        Assert.IsTrue(poseTemplate.ikIndices.Length == poseTemplate.ikValues.Length);
                        for (int i = 0; i < poseTemplate.ikIndices.Length; i++)
                        {
                            var ikIndex = poseTemplate.ikIndices[i];
                            var value = poseTemplate.ikValues[i];
                            SetAnimationValueAnimatorIkTIfNotOriginal(ikIndex, value.position);
                            SetAnimationValueAnimatorIkQIfNotOriginal(ikIndex, value.rotation);
                        }
                    }
                }
            }
            #endregion
            #region Generic
            if ((flags & PoseFlags.Generic) != 0)
            {
                if (poseTemplate.transformPaths != null && poseTemplate.transformValues != null)
                {
                    Assert.IsTrue(poseTemplate.transformPaths.Length == poseTemplate.transformValues.Length);
                    for (int i = 0; i < poseTemplate.transformPaths.Length; i++)
                    {
                        var boneIndex = GetBoneIndexFromPath(poseTemplate.transformPaths[i]);
                        if (boneIndex < 0 || IsConflictBone(boneIndex)) continue;
                        var position = poseTemplate.transformValues[i].position;
                        var rotation = poseTemplate.transformValues[i].rotation;
                        var scale = poseTemplate.transformValues[i].scale;
                        SetAnimationValueTransformPositionIfNotOriginal(boneIndex, position);
                        SetAnimationValueTransformRotationIfNotOriginal(boneIndex, rotation);
                        SetAnimationValueTransformScaleIfNotOriginal(boneIndex, scale);
                    }
                }
            }
            #endregion
            #region BlendShape
            if ((flags & PoseFlags.BlendShape) != 0)
            {
                if (poseTemplate.blendShapePaths != null && poseTemplate.blendShapeValues != null)
                {
                    Assert.IsTrue(poseTemplate.blendShapePaths.Length == poseTemplate.blendShapeValues.Length);
                    var blendShapePathCount = Mathf.Min(poseTemplate.blendShapePaths.Length, poseTemplate.blendShapeValues.Length);
                    var blendShapePathIndexTable = new Dictionary<string, int>(blendShapePathCount);
                    for (int i = 0; i < blendShapePathCount; i++)
                    {
                        blendShapePathIndexTable.TryAdd(poseTemplate.blendShapePaths[i], i);
                    }
                    foreach (var renderer in Renderers)
                    {
                        var smr = renderer as SkinnedMeshRenderer;
                        if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount <= 0) continue;
                        var path = GetGameObjectPath(smr.gameObject);
                        if (!blendShapePathIndexTable.TryGetValue(path, out int index)) continue;
                        var names = poseTemplate.blendShapeValues[index].names;
                        var weights = poseTemplate.blendShapeValues[index].weights;
                        if (names == null || weights == null) continue;
                        Assert.IsTrue(names.Length == weights.Length);
                        var count = Mathf.Min(names.Length, weights.Length);
                        for (int i = 0; i < count; i++)
                        {
                            if (blendShapeAdd)
                                SetAnimationValueBlendShapeIfNotOriginal(smr, names[i], GetAnimationValueBlendShape(smr, names[i]) + weights[i]);
                            else
                                SetAnimationValueBlendShapeIfNotOriginal(smr, names[i], weights[i]);
                        }
                    }
                }
            }
            #endregion
            SetPoseAfter(calcIK);
        }
        public void LoadSelectionPoseTemplate(PoseTemplate poseTemplate, PoseFlags flags = PoseFlags.All)
        {
            if (!SetPoseBefore("Set Selection Template"))
                return;
            bool selectRoot = SelectionGameObjectsIndexOf(VAW.GameObject) >= 0;
            var selectHumanoidIndexes = SelectionGameObjectsHumanoidIndex();
            var selectMuscleIndexes = SelectionGameObjectsMuscleIndex();
            var selectAnimatorIKTargetsHumanoidIndexes = animatorIK.SelectionAnimatorIKTargetsHumanoidIndexes();
            var selectOriginalIKTargetsBoneIndexes = originalIK.SelectionOriginalIKTargetsBoneIndexes();
            //
            #region Humanoid
            if ((flags & PoseFlags.Humanoid) != 0)
            {
                if (IsHuman && poseTemplate.isHuman)
                {
                    if (selectRoot)
                    {
                        if (poseTemplate.haveRootT)
                        {
                            SetAnimationValueAnimatorRootTIfNotOriginal(poseTemplate.rootT);
                        }
                        if (poseTemplate.haveRootQ)
                        {
                            SetAnimationValueAnimatorRootQIfNotOriginal(poseTemplate.rootQ);
                        }
                    }
                    if (poseTemplate.musclePropertyNames != null && poseTemplate.muscleValues != null)
                    {
                        Assert.IsTrue(poseTemplate.musclePropertyNames.Length == poseTemplate.muscleValues.Length);
                        for (int i = 0; i < poseTemplate.musclePropertyNames.Length; i++)
                        {
                            var muscleIndex = GetMuscleIndexFromPropertyName(poseTemplate.musclePropertyNames[i]);
                            if (muscleIndex < 0) continue;
                            if (selectMuscleIndexes.Contains(muscleIndex) ||
                                selectAnimatorIKTargetsHumanoidIndexes.Contains((HumanBodyBones)HumanTrait.BoneFromMuscle(muscleIndex)))
                            {
                                SetAnimationValueAnimatorMuscleIfNotOriginal(muscleIndex, poseTemplate.muscleValues[i]);
                            }
                        }
                    }
                    if (poseTemplate.tdofIndices != null && poseTemplate.tdofValues != null)
                    {
                        Assert.IsTrue(poseTemplate.tdofIndices.Length == poseTemplate.tdofValues.Length);
                        for (int i = 0; i < poseTemplate.tdofIndices.Length; i++)
                        {
                            var tdofIndex = poseTemplate.tdofIndices[i];
                            var value = poseTemplate.tdofValues[i];
                            if (selectHumanoidIndexes.Contains(AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex]) ||
                                selectAnimatorIKTargetsHumanoidIndexes.Contains(AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex]))
                            {
                                SetAnimationValueAnimatorTDOFIfNotOriginal(tdofIndex, value);
                            }
                        }
                    }
                    if (poseTemplate.ikIndices != null && poseTemplate.ikValues != null)
                    {
                        Assert.IsTrue(poseTemplate.ikIndices.Length == poseTemplate.ikValues.Length);
                        for (int i = 0; i < poseTemplate.ikIndices.Length; i++)
                        {
                            var ikIndex = poseTemplate.ikIndices[i];
                            var value = poseTemplate.ikValues[i];
                            if (selectHumanoidIndexes.Contains(AnimatorIKIndex2HumanBodyBones[(int)ikIndex]) ||
                                selectAnimatorIKTargetsHumanoidIndexes.Contains(AnimatorIKIndex2HumanBodyBones[(int)ikIndex]))
                            {
                                SetAnimationValueAnimatorIkTIfNotOriginal(ikIndex, value.position);
                                SetAnimationValueAnimatorIkQIfNotOriginal(ikIndex, value.rotation);
                            }
                        }
                    }
                }
            }
            #endregion
            #region Generic
            if ((flags & PoseFlags.Generic) != 0)
            {
                if (poseTemplate.transformPaths != null && poseTemplate.transformValues != null)
                {
                    Assert.IsTrue(poseTemplate.transformPaths.Length == poseTemplate.transformValues.Length);
                    for (int i = 0; i < poseTemplate.transformPaths.Length; i++)
                    {
                        var boneIndex = GetBoneIndexFromPath(poseTemplate.transformPaths[i]);
                        if (boneIndex < 0 || IsConflictBone(boneIndex)) continue;
                        if (SelectionBones.Contains(boneIndex) ||
                            selectOriginalIKTargetsBoneIndexes.Contains(boneIndex))
                        {
                            var position = poseTemplate.transformValues[i].position;
                            var rotation = poseTemplate.transformValues[i].rotation;
                            var scale = poseTemplate.transformValues[i].scale;
                            SetAnimationValueTransformPositionIfNotOriginal(boneIndex, position);
                            SetAnimationValueTransformRotationIfNotOriginal(boneIndex, rotation);
                            SetAnimationValueTransformScaleIfNotOriginal(boneIndex, scale);
                        }
                    }
                }
            }
            #endregion
            #region BlendShape
            if ((flags & PoseFlags.BlendShape) != 0)
            {
                if (poseTemplate.blendShapePaths != null && poseTemplate.blendShapeValues != null)
                {
                    Assert.IsTrue(poseTemplate.blendShapePaths.Length == poseTemplate.blendShapeValues.Length);
                    var blendShapePathCount = Mathf.Min(poseTemplate.blendShapePaths.Length, poseTemplate.blendShapeValues.Length);
                    var blendShapePathIndexTable = new Dictionary<string, int>(blendShapePathCount);
                    for (int i = 0; i < blendShapePathCount; i++)
                    {
                        blendShapePathIndexTable.TryAdd(poseTemplate.blendShapePaths[i], i);
                    }
                    foreach (var renderer in Renderers)
                    {
                        var smr = renderer as SkinnedMeshRenderer;
                        if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount <= 0) continue;
                        var path = GetGameObjectPath(smr.gameObject);
                        if (!blendShapePathIndexTable.TryGetValue(path, out int index)) continue;
                        var boneIndex = GetBoneIndexFromPath(path);
                        if (boneIndex < 0) continue;
                        if (SelectionBones.Contains(boneIndex))
                        {
                            var names = poseTemplate.blendShapeValues[index].names;
                            var weights = poseTemplate.blendShapeValues[index].weights;
                            if (names == null || weights == null) continue;
                            Assert.IsTrue(names.Length == weights.Length);
                            var count = Mathf.Min(names.Length, weights.Length);
                            for (int i = 0; i < count; i++)
                            {
                                SetAnimationValueBlendShapeIfNotOriginal(smr, names[i], weights[i]);
                            }
                        }
                    }
                }
            }
            #endregion
            SetPoseAfter(true);
        }
        #endregion

        #region IK
        public void IKHandleGUI()
        {
            animatorIK.HandleGUI();
            originalIK.HandleGUI();
        }
        public void IKTargetGUI()
        {
            animatorIK.TargetGUI();
            originalIK.TargetGUI();
        }

        private void IKUpdateBones()
        {
            animatorIK.UpdateBones();
        }
        private void IKChangeSelection()
        {
            if (animatorIK.ChangeSelectionIK()) return;
            if (originalIK.ChangeSelectionIK()) return;
        }

        public void ClearIkTargetSelect()
        {
            animatorIK.ikTargetSelect = null;
            animatorIK.OnSelectionChange();
            originalIK.ikTargetSelect = null;
            originalIK.OnSelectionChange();
        }

        public bool IsIKBone(HumanBodyBones humanoidIndex)
        {
            return animatorIK.IsIKBone(humanoidIndex) != AnimatorIKCore.IKTarget.None ||
                    originalIK.IsIKBone(humanoidIndex) >= 0;
        }
        public bool IsIKBone(int boneIndex)
        {
            return animatorIK.IsIKBone(BoneIndex2humanoidIndex[boneIndex]) != AnimatorIKCore.IKTarget.None ||
                    originalIK.IsIKBone(boneIndex) >= 0;
        }

        public bool IsConflictBone(int boneIndex)
        {
            if (IsHuman && HumanoidConflict[boneIndex])
                return true;
            else if (RootMotionBoneIndex >= 0 && boneIndex == 0)
                return true;
            return false;
        }

        public void SetUpdateIKtargetBone(int boneIndex)
        {
            if (boneIndex < 0)
                return;

            if (IsHuman)
            {
                bool updateFull = false;
                if (IsAnimatorRootCorrectionBone(BoneIndex2humanoidIndex[boneIndex]))
                {
                    if (rootCorrectionMode == VeryAnimation.RootCorrectionMode.Disable)
                    {
                        updateFull = true;
                    }
                }
                if (updateFull)
                {
                    SetUpdateIKtargetAll();
                }
                else
                {
                    animatorIK.SetUpdateIKtargetBone(boneIndex);
                }
            }
            originalIK.SetUpdateIKtargetBone(boneIndex);
        }
        public void SetUpdateIKtargetMuscle(int muscleIndex)
        {
            if (muscleIndex < 0) return;
            if (IsHuman)
            {
                SetUpdateIKtargetHumanoidIndex((HumanBodyBones)HumanTrait.BoneFromMuscle(muscleIndex));
            }
        }
        public void SetUpdateIKtargetHumanoidIndex(HumanBodyBones humanoidIndex)
        {
            if (humanoidIndex < 0) return;
            if (IsHuman)
            {
                var boneIndex = HumanoidIndex2boneIndex[(int)humanoidIndex];
                if (boneIndex < 0)
                {
                    var virtualIndex = GetHumanVirtualBoneParentBone(humanoidIndex);
                    if (virtualIndex >= 0)
                        boneIndex = HumanoidIndex2boneIndex[(int)virtualIndex];
                }
                SetUpdateIKtargetBone(boneIndex);
            }
        }
        public void SetUpdateIKtargetTdofIndex(AnimatorTDOFIndex tdofIndex)
        {
            if (tdofIndex < 0) return;
            if (IsHuman)
            {
                var humanoidIndex = VeryAnimation.AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex];
                if (humanoidIndex < 0) return;
                SetUpdateIKtargetHumanoidIndex(humanoidIndex);
            }
        }
        public void SetUpdateIKtargetAnimatorIK(AnimatorIKCore.IKTarget ikTarget)
        {
            if (ikTarget < 0) return;
            animatorIK.SetUpdateIKtargetAnimatorIK(ikTarget, true);
            for (int humanoidIndex = 0; humanoidIndex < AnimatorIKCore.HumanBonesUpdateAnimatorIK.Length; humanoidIndex++)
            {
                if (AnimatorIKCore.HumanBonesUpdateAnimatorIK[humanoidIndex] == ikTarget)
                {
                    var boneIndex = HumanoidIndex2boneIndex[humanoidIndex];
                    SetUpdateIKtargetBone(boneIndex);
                }
            }
        }
        public void SetUpdateIKtargetOriginalIK(int ikTarget)
        {
            if (ikTarget < 0 || ikTarget >= originalIK.ikData.Count) return;
            originalIK.SetUpdateIKtargetOriginalIK(ikTarget, true);
            var data = originalIK.ikData[ikTarget];
            var count = Mathf.Min(data.level, data.joints.Count);
            for (int i = 0; i < count; i++)
            {
                if (data.joints[i] == null) continue;
                var boneIndex = BonesIndexOf(data.joints[i].bone);
                SetUpdateIKtargetBone(boneIndex);
            }
        }
        public void ResetUpdateIKtargetAll()
        {
            animatorIK.ResetUpdateIKtargetAll();
            originalIK.ResetUpdateIKtargetAll();
        }
        public void SetUpdateIKtargetAll()
        {
            animatorIK.SetUpdateIKtargetAll();
            originalIK.SetUpdateIKtargetAll();
        }
        public bool GetUpdateIKtargetAll()
        {
            return animatorIK.GetUpdateIKtargetAll() || originalIK.GetUpdateIKtargetAll();
        }

        public void SetSynchroIKtargetBone(int boneIndex)
        {
            if (boneIndex < 0) return;
            if (IsHuman)
            {
                animatorIK.SetSynchroIKtargetBone(boneIndex);
            }
            originalIK.SetSynchroIKtargetBone(boneIndex);
        }
        public void SetSynchroIKtargetMuscle(int muscleIndex)
        {
            if (muscleIndex < 0) return;
            if (IsHuman)
            {
                SetSynchroIKtargetHumanoidIndex((HumanBodyBones)HumanTrait.BoneFromMuscle(muscleIndex));
            }
        }
        public void SetSynchroIKtargetHumanoidIndex(HumanBodyBones humanoidIndex)
        {
            if (humanoidIndex < 0) return;
            if (IsHuman)
            {
                var boneIndex = HumanoidIndex2boneIndex[(int)humanoidIndex];
                if (boneIndex < 0)
                {
                    var virtualIndex = GetHumanVirtualBoneParentBone(humanoidIndex);
                    if (virtualIndex >= 0)
                        boneIndex = HumanoidIndex2boneIndex[(int)virtualIndex];
                }
                SetSynchroIKtargetBone(boneIndex);
            }
        }
        public void SetSynchroIKtargetTdofIndex(AnimatorTDOFIndex tdofIndex)
        {
            if (tdofIndex < 0) return;
            if (IsHuman)
            {
                var humanoidIndex = VeryAnimation.AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex];
                if (humanoidIndex < 0) return;
                SetSynchroIKtargetHumanoidIndex(humanoidIndex);
            }
        }
        public void SetSynchroIKtargetAnimatorIK(AnimatorIKCore.IKTarget ikTarget)
        {
            if (ikTarget < 0) return;
            if (IsHuman)
            {
                animatorIK.SetSynchroIKtargetAnimatorIK(ikTarget);
            }
            for (int humanoidIndex = 0; humanoidIndex < AnimatorIKCore.HumanBonesUpdateAnimatorIK.Length; humanoidIndex++)
            {
                if (AnimatorIKCore.HumanBonesUpdateAnimatorIK[humanoidIndex] == ikTarget)
                {
                    SetSynchroIKtargetBone(HumanoidIndex2boneIndex[humanoidIndex]);
                }
            }
        }
        public void SetSynchroIKtargetOriginalIK(int ikTarget)
        {
            if (ikTarget < 0 || ikTarget >= originalIK.ikData.Count) return;
            if (IsHuman)
            {
                var data = originalIK.ikData[ikTarget];
                var count = Mathf.Min(data.level, data.joints.Count);
                for (int i = 0; i < count; i++)
                {
                    if (data.joints[i] == null) continue;
                    SetSynchroIKtargetBone(BonesIndexOf(data.joints[i].bone));
                }
            }
            originalIK.SetSynchroIKtargetOriginalIK(ikTarget);
        }
        public void ResetSynchroIKtargetAll()
        {
            animatorIK.ResetSynchroIKtargetAll();
            originalIK.ResetSynchroIKtargetAll();
        }
        public void SetSynchroIKtargetAll()
        {
            animatorIK.SetSynchroIKtargetAll();
            originalIK.SetSynchroIKtargetAll();
        }
        public bool GetSynchroIKtargetAll()
        {
            return animatorIK.GetSynchroIKtargetAll() || originalIK.GetSynchroIKtargetAll();
        }
        public void UpdateSynchroIKSet()
        {
            animatorIK.UpdateSynchroIKSet();
            originalIK.UpdateSynchroIKSet();
        }
        #endregion

        #region AnimationCurve
        private class TmpCurves
        {
            public EditorCurveBinding[] bindings = new EditorCurveBinding[4];
            public AnimationCurve[] curves = new AnimationCurve[4];

            public EditorCurveBinding[] subBindings = new EditorCurveBinding[4];
            public AnimationCurve[] subCurves = new AnimationCurve[4];

            public void Clear()
            {
                for (int i = 0; i < 4; i++)
                {
                    curves[i] = subCurves[i] = null;
                }
            }

            public Vector3 EvaluateVector3(float time) => AnimationCommon.EvaluateVector3(curves, time);
            public Quaternion EvaluateQuaternionNormalized(float time) => AnimationCommon.EvaluateQuaternionNormalized(curves, time);
        }
        private readonly TmpCurves tmpCurves = new();
        void LoadTmpCurves(EditorCurveBinding[] bindings)
        {
            tmpCurves.Clear();
            for (int i = 0; i < bindings.Length; i++)
            {
                tmpCurves.bindings[i] = bindings[i];
                tmpCurves.curves[i] = GetEditorCurveCache(tmpCurves.bindings[i]);
            }
        }
        private void LoadTmpCurvesFullDof(EditorCurveBinding binding, int count, EditorCurveBinding? subBinding = null)
        {
            tmpCurves.Clear();
            for (int i = 0; i < count; i++)
            {
                tmpCurves.bindings[i] = binding;
                tmpCurves.bindings[i].propertyName = tmpCurves.bindings[i].propertyName[..^AnimationCommon.PropertyName.DotDof[i].Length] + AnimationCommon.PropertyName.DotDof[i];
                tmpCurves.curves[i] = GetEditorCurveCache(tmpCurves.bindings[i]);
                if (subBinding.HasValue)
                {
                    tmpCurves.subBindings[i] = subBinding.Value;
                    tmpCurves.subBindings[i].propertyName = tmpCurves.subBindings[i].propertyName[..^AnimationCommon.PropertyName.DotDof[i].Length] + AnimationCommon.PropertyName.DotDof[i];
                    tmpCurves.subCurves[i] = GetEditorCurveCache(tmpCurves.subBindings[i]);
                }
            }
        }

        private bool beginChangeAnimationCurve;
        private bool BeginChangeAnimationCurve(AnimationClip clip, string undoName)
        {
            SetUpdateSampleAnimation();
            if (!beginChangeAnimationCurve)
            {
                if (clip == null) return false;
                if ((clip.hideFlags & HideFlags.NotEditable) != HideFlags.None)
                {
                    EditorCommon.ShowNotification("Read-Only");
                    Debug.LogErrorFormat(Language.GetText(Language.Help.LogAnimationClipReadOnlyError), clip.name);
                    return false;
                }

                Undo.RegisterCompleteObjectUndo(clip, undoName);

                beginChangeAnimationCurve = true;
            }
            return true;
        }
        private void EndChangeAnimationCurve()
        {
            if (!beginChangeAnimationCurve) return;
            beginChangeAnimationCurve = false;
        }

        public void SetPoseHumanoidDefault()
        {
            if (!SetPoseBefore("Set Pose HumanoidDefault"))
                return;
            ResetAllHaveAnimationCurve(PoseFlags.Humanoid);
            SetPoseAfter();
        }
        public void SetPoseHumanoidAvatarConfiguration()
        {
            if (!SetPoseBefore("Set Pose HumanoidAvatarConfiguration"))
                return;
            ResetAllHaveAnimationCurve(PoseFlags.Humanoid);
            TransformPoseSave.ResetHumanDescriptionTransforms();
            BlendShapeWeightSave.ResetDefaultWeight();
            SetAllChangedAnimationCurve(PoseFlags.Humanoid);
            SetPoseAfter();
        }
        public void SetPoseHumanoidTPose()
        {
            if (!SetPoseBefore("Set Pose HumanoidTPose"))
                return;
            ResetAllHaveAnimationCurve(PoseFlags.Humanoid);
            TransformPoseSave.ResetTPoseTransform();
            BlendShapeWeightSave.ResetDefaultWeight();
            SetAllChangedAnimationCurve(PoseFlags.Humanoid);
            SetPoseAfter();
        }
        public void SetPoseEditStart()
        {
            if (!SetPoseBefore("Set Pose EditStart"))
                return;
            ResetAllHaveAnimationCurve();
            SetAllChangedAnimationCurve();
            SetPoseAfter();
        }
        public void SetPoseBind()
        {
            if (!SetPoseBefore("Set Pose Bind"))
                return;
            ResetAllHaveAnimationCurve();
            TransformPoseSave.ResetBindTransform();
            BlendShapeWeightSave.ResetDefaultWeight();
            SetAllChangedAnimationCurve();
            SetPoseAfter();
        }
        public void SetPosePrefab()
        {
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(VAW.GameObject) as GameObject;
            if (prefab == null) return;

            if (!SetPoseBefore("Set Pose Prefab"))
                return;
            ResetAllHaveAnimationCurve();
            TransformPoseSave.ResetPrefabTransform();
            BlendShapeWeightSave.ResetPrefabWeight();
            SetAllChangedAnimationCurve();
            SetPoseAfter();
        }
        public void SetPoseMirror()
        {
            if (!SetPoseBefore("Set Pose Mirror"))
                return;
            #region Humanoid
            if (IsHuman)
            {
                {
                    var rootT = GetAnimationValueAnimatorRootT();
                    SetAnimationValueAnimatorRootTIfNotOriginal(new Vector3(-rootT.x, rootT.y, rootT.z));
                    var rootQ = GetAnimationValueAnimatorRootQ();
                    SetAnimationValueAnimatorRootQIfNotOriginal(new Quaternion(rootQ.x, -rootQ.y, -rootQ.z, rootQ.w));
                }
                {
                    var values = new float[HumanTrait.MuscleCount];
                    for (int i = 0; i < values.Length; i++)
                    {
                        var mmi = GetMirrorMuscleIndex(i);
                        if (mmi < 0)
                            values[i] = float.MaxValue;
                        else
                            values[i] = GetAnimationValueAnimatorMuscle(mmi);
                    }
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (values[i] == float.MaxValue)
                        {
                            var hi = HumanTrait.BoneFromMuscle(i);
                            if (i == HumanTrait.MuscleFromBone(hi, 0) || i == HumanTrait.MuscleFromBone(hi, 1))
                            {
                                SetAnimationValueAnimatorMuscleIfNotOriginal(i, -GetAnimationValueAnimatorMuscle(i));
                            }
                        }
                        else
                        {
                            SetAnimationValueAnimatorMuscleIfNotOriginal(i, values[i]);
                        }
                    }
                }
                {
                    Vector3[] saves = new Vector3[(int)AnimatorTDOFIndex.Total];
                    for (var tdof = (AnimatorTDOFIndex)0; tdof < AnimatorTDOFIndex.Total; tdof++)
                    {
                        saves[(int)tdof] = GetAnimationValueAnimatorTDOF(tdof);
                    }
                    for (var tdof = (AnimatorTDOFIndex)0; tdof < AnimatorTDOFIndex.Total; tdof++)
                    {
                        var mmi = AnimatorTDOFMirrorIndexes[(int)tdof];
                        Vector3 vec;
                        if (mmi != AnimatorTDOFIndex.None)
                        {
                            vec = Vector3.Scale(saves[(int)mmi], HumanBonesAnimatorTDOFIndex[(int)AnimatorTDOFIndex2HumanBodyBones[(int)mmi]].mirror);
                        }
                        else
                        {
                            vec = saves[(int)tdof];
                            vec.z = -vec.z;
                        }
                        SetAnimationValueAnimatorTDOFIfNotOriginal(tdof, vec);
                    }
                }
            }
            #endregion
            var bindings = AnimationUtility.GetCurveBindings(CurrentClip);
            var bindingSet = new HashSet<EditorCurveBinding>(bindings);
            #region Generic
            {
                var values = new Dictionary<int, TransformPoseSave.SaveData>();
                for (int boneIndex = 0; boneIndex < Bones.Length; boneIndex++)
                {
                    if (values.ContainsKey(boneIndex)) continue;
                    var saveData = new TransformPoseSave.SaveData();
                    values.Add(boneIndex, saveData);
                    var mbi = MirrorBoneIndexes[boneIndex];
                    if (mbi >= 0)
                    {
                        saveData.localPosition = GetMirrorBoneLocalPosition(mbi, GetAnimationValueTransformPosition(mbi));
                        saveData.localRotation = GetMirrorBoneLocalRotation(mbi, GetAnimationValueTransformRotation(mbi));
                        saveData.localScale = GetMirrorBoneLocalScale(mbi, GetAnimationValueTransformScale(mbi));
                        if (!values.ContainsKey(mbi))
                        {
                            var mbiData = new TransformPoseSave.SaveData();
                            values.Add(mbi, mbiData);
                            mbiData.localPosition = GetMirrorBoneLocalPosition(boneIndex, GetAnimationValueTransformPosition(boneIndex));
                            mbiData.localRotation = GetMirrorBoneLocalRotation(boneIndex, GetAnimationValueTransformRotation(boneIndex));
                            mbiData.localScale = GetMirrorBoneLocalScale(boneIndex, GetAnimationValueTransformScale(boneIndex));
                        }
                    }
                    else
                    {
                        saveData.localPosition = GetMirrorBoneLocalPosition(boneIndex, GetAnimationValueTransformPosition(boneIndex));
                        saveData.localRotation = GetMirrorBoneLocalRotation(boneIndex, GetAnimationValueTransformRotation(boneIndex));
                        saveData.localScale = GetMirrorBoneLocalScale(boneIndex, GetAnimationValueTransformScale(boneIndex));
                    }
                }
                foreach (var pair in values)
                {
                    var bi = pair.Key;
                    if (IsConflictBone(bi)) continue;
                    SetAnimationValueTransformPositionIfNotOriginal(bi, pair.Value.localPosition);
                    SetAnimationValueTransformRotationIfNotOriginal(bi, pair.Value.localRotation);
                    if (VAW.EditorSettings.SettingGenericMirrorScale)
                        SetAnimationValueTransformScaleIfNotOriginal(bi, pair.Value.localScale);
                }
            }
            #endregion
            #region BlendShape
            {
                var values = new Dictionary<SkinnedMeshRenderer, Dictionary<string, float>>();
                foreach (var binding in bindings)
                {
                    if (!IsSkinnedMeshRendererBlendShapeCurveBinding(binding)) continue;
                    var boneIndex = GetBoneIndexFromPath(binding.path);
                    if (boneIndex < 0) continue;
                    if (!Bones[boneIndex].TryGetComponent<SkinnedMeshRenderer>(out var renderer)) continue;
                    var name = AnimationCommon.PropertyName2BlendShapeName(binding.propertyName);
                    if (MirrorBlendShape.TryGetValue(renderer, out Dictionary<string, string> nameTable))
                    {
                        if (nameTable.TryGetValue(name, out string mirrorName))
                        {
                            if (!values.TryGetValue(renderer, out var blendValues))
                            {
                                blendValues = new Dictionary<string, float>();
                                values.Add(renderer, blendValues);
                            }
                            blendValues.Add(mirrorName, GetAnimationValueBlendShape(renderer, name));
                            #region NotHaveMirrorCurve
                            {
                                var mbinding = AnimationCurveBindingBlendShape(renderer, mirrorName);
                                if (!bindingSet.Contains(mbinding))
                                {
                                    blendValues.Add(name, BlendShapeWeightSave.GetOriginalWeight(renderer, mirrorName));
                                }
                            }
                            #endregion
                        }
                    }
                }
                foreach (var list in values)
                {
                    foreach (var pair in list.Value)
                    {
                        SetAnimationValueBlendShapeIfNotOriginal(list.Key, pair.Key, pair.Value);
                    }
                }
            }
            #endregion

            SetPoseAfter();
        }

        public void SetSelectionHumanoidDefault(bool position, bool rotation)
        {
            if (!SetPoseBefore("Set Selection HumanoidDefault"))
                return;

            var selectHumanoidIndexes = SelectionGameObjectsHumanoidIndex();
            var selectMuscleIndexes = SelectionGameObjectsMuscleIndex();
            {
                if (rotation)
                {
                    foreach (var muscle in selectMuscleIndexes)
                        SetAnimationValueAnimatorMuscleIfNotOriginal(muscle, 0f);
                }
                if (position)
                {
                    foreach (var hi in selectHumanoidIndexes)
                    {
                        if (HumanBonesAnimatorTDOFIndex[(int)hi] == null) continue;
                        SetAnimationValueAnimatorTDOFIfNotOriginal(HumanBonesAnimatorTDOFIndex[(int)hi].index, Vector3.zero);
                    }
                }
            }
            if (SelectionGameObjectsIndexOf(VAW.GameObject) >= 0)
            {
                if (position)
                    SetAnimationValueAnimatorRootTIfNotOriginal(new Vector3(0, 1, 0));
                if (rotation)
                    SetAnimationValueAnimatorRootQIfNotOriginal(Quaternion.identity);
            }

            SetSelectionCommonOriginal();

            SetPoseAfter(true);
        }
        public void SetSelectionHumanoidAvatarConfiguration(bool position, bool rotation)
        {
            if (!SetPoseBefore("Set Selection HumanoidAvatarConfiguration"))
                return;

            TransformPoseSave.ResetHumanDescriptionTransforms();
            BlendShapeWeightSave.ResetDefaultWeight();

            SetSelectionHumanoidPose(position, rotation);

            SetSelectionCommonOriginal();

            SetPoseAfter(true);
        }
        public void SetSelectionHumanoidTPose(bool position, bool rotation)
        {
            if (!SetPoseBefore("Set Selection HumanoidTPose"))
                return;

            TransformPoseSave.ResetTPoseTransform();
            BlendShapeWeightSave.ResetDefaultWeight();

            SetSelectionHumanoidPose(position, rotation);

            SetSelectionCommonOriginal();

            SetPoseAfter(true);
        }
        public void SetSelectionBindPose(bool position, bool rotation, bool scale)
        {
            if (!SetPoseBefore("Set Selection BindPose"))
                return;

            TransformPoseSave.ResetBindTransform();
            BlendShapeWeightSave.ResetDefaultWeight();

            SetSelectionHumanoidPose(position, rotation);

            SetSelectionGenericPose(position, rotation, scale);

            SetSelectionCommonOriginal();

            SetPoseAfter(true);
        }
        public void SetSelectionPrefabPose(bool position, bool rotation, bool scale)
        {
            if (!SetPoseBefore("Set Selection PrefabPose"))
                return;

            TransformPoseSave.ResetPrefabTransform();
            BlendShapeWeightSave.ResetPrefabWeight();

            SetSelectionHumanoidPose(position, rotation);

            SetSelectionGenericPose(position, rotation, scale);

            #region Motion
            if (SelectionMotionTool)
            {
                SetAnimationValueAnimatorMotionTIfNotOriginal(Vector3.zero);
                SetAnimationValueAnimatorMotionQIfNotOriginal(Quaternion.identity);
            }
            #endregion

            #region BlendShape
            {
                foreach (var boneIndex in SelectionBones)
                {
                    var renderer = Bones[boneIndex].GetComponent<SkinnedMeshRenderer>();
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0) continue;
                    BlendShapeWeightSave.ActionOriginalWeights(renderer, (name, value) =>
                    {
                        if (BlendShapeWeightSave.IsHavePrefabWeight(renderer, name))
                            SetAnimationValueBlendShape(renderer, name, BlendShapeWeightSave.GetPrefabWeight(renderer, name));
                        else
                            SetAnimationValueBlendShapeIfNotOriginal(renderer, name, BlendShapeWeightSave.GetDefaultWeight(renderer, name));
                    });
                }
            }
            #endregion

            SetPoseAfter(true);
        }
        public void SetSelectionEditStart(bool position, bool rotation, bool scale)
        {
            if (!SetPoseBefore("Set Selection EditStart"))
                return;

            TransformPoseSave.ResetOriginalTransform();
            BlendShapeWeightSave.ResetOriginalWeight();

            SetSelectionHumanoidPose(position, rotation);

            SetSelectionGenericPose(position, rotation, scale);

            SetSelectionCommonOriginal();

            SetPoseAfter(true);
        }
        public void SetSelectionMirror()
        {
            if (!SetPoseBefore("Set Selection Mirror"))
                return;

            #region Humanoid
            if (IsHuman)
            {
                var selectAnimatorIKTargetsHumanoidIndexes = animatorIK.SelectionAnimatorIKTargetsHumanoidIndexes();
                var selectMuscleIndexes = SelectionGameObjectsMuscleIndex();
                foreach (var humanoidIndex in selectAnimatorIKTargetsHumanoidIndexes)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        var muscleIndex = HumanTrait.MuscleFromBone((int)humanoidIndex, j);
                        if (muscleIndex < 0) continue;
                        selectMuscleIndexes.Add(muscleIndex);
                    }
                }
                int[] mirrorMuscles = new int[selectMuscleIndexes.Count];
                var values = new float[selectMuscleIndexes.Count];
                for (int i = 0; i < selectMuscleIndexes.Count; i++)
                {
                    mirrorMuscles[i] = GetMirrorMuscleIndex(selectMuscleIndexes[i]);
                    if (mirrorMuscles[i] >= 0)
                        values[i] = GetAnimationValueAnimatorMuscle(mirrorMuscles[i]);
                }
                for (int i = 0; i < selectMuscleIndexes.Count; i++)
                {
                    if (mirrorMuscles[i] < 0)
                    {
                        var hi = HumanTrait.BoneFromMuscle(selectMuscleIndexes[i]);
                        if (selectMuscleIndexes[i] == HumanTrait.MuscleFromBone(hi, 0) || selectMuscleIndexes[i] == HumanTrait.MuscleFromBone(hi, 1))
                        {
                            var value = -GetAnimationValueAnimatorMuscle(selectMuscleIndexes[i]);
                            SetAnimationValueAnimatorMuscleIfNotOriginal(selectMuscleIndexes[i], value);
                        }
                    }
                    else
                    {
                        SetAnimationValueAnimatorMuscleIfNotOriginal(selectMuscleIndexes[i], values[i]);
                    }
                }
                if (HumanoidHasTDoF)
                {
                    var his = SelectionGameObjectsHumanoidIndex();
                    his.AddRange(selectAnimatorIKTargetsHumanoidIndexes);
                    Vector3[] saves = new Vector3[(int)AnimatorTDOFIndex.Total];
                    foreach (var hi in his)
                    {
                        if (HumanBonesAnimatorTDOFIndex[(int)hi] == null) continue;
                        var tdof = HumanBonesAnimatorTDOFIndex[(int)hi].index;
                        var mmi = AnimatorTDOFMirrorIndexes[(int)tdof];
                        if (mmi != AnimatorTDOFIndex.None)
                            saves[(int)mmi] = GetAnimationValueAnimatorTDOF(mmi);
                        else
                            saves[(int)tdof] = GetAnimationValueAnimatorTDOF(tdof);
                    }
                    foreach (var hi in his)
                    {
                        if (HumanBonesAnimatorTDOFIndex[(int)hi] == null) continue;
                        var tdof = HumanBonesAnimatorTDOFIndex[(int)hi].index;
                        var mmi = AnimatorTDOFMirrorIndexes[(int)tdof];
                        var vec = Vector3.zero;
                        if (mmi != AnimatorTDOFIndex.None)
                        {
                            vec = Vector3.Scale(saves[(int)mmi], HumanBonesAnimatorTDOFIndex[(int)AnimatorTDOFIndex2HumanBodyBones[(int)mmi]].mirror);
                        }
                        else
                        {
                            vec = saves[(int)tdof];
                            vec.z = -vec.z;
                        }
                        SetAnimationValueAnimatorTDOFIfNotOriginal(tdof, vec);
                    }
                }

                if (SelectionBones.Contains(RootMotionBoneIndex))
                {
                    var rootT = GetAnimationValueAnimatorRootT();
                    SetAnimationValueAnimatorRootTIfNotOriginal(new Vector3(-rootT.x, rootT.y, rootT.z));
                    var rootQ = GetAnimationValueAnimatorRootQ();
                    SetAnimationValueAnimatorRootQIfNotOriginal(new Quaternion(rootQ.x, -rootQ.y, -rootQ.z, rootQ.w));
                }
            }
            #endregion

            #region Generic
            {
                var selectOriginalIKTargetsBoneIndexes = originalIK.SelectionOriginalIKTargetsBoneIndexes();
                var bones = new List<int>(SelectionBones);
                bones.AddRange(selectOriginalIKTargetsBoneIndexes);
                var values = new TransformPoseSave.SaveData[bones.Count];
                for (int i = 0; i < bones.Count; i++)
                {
                    var mbi = MirrorBoneIndexes[bones[i]];
                    if (mbi >= 0)
                    {
                        var mt = Skeleton.Bones[mbi].transform;
                        values[i] = new TransformPoseSave.SaveData()
                        {
                            localPosition = GetMirrorBoneLocalPosition(mbi, mt.localPosition),
                            localRotation = GetMirrorBoneLocalRotation(mbi, mt.localRotation),
                            localScale = GetMirrorBoneLocalScale(mbi, mt.localScale),
                        };
                    }
                    else
                    {
                        var bi = bones[i];
                        var t = Skeleton.Bones[bi].transform;
                        values[i] = new TransformPoseSave.SaveData()
                        {
                            localPosition = GetMirrorBoneLocalPosition(bi, t.localPosition),
                            localRotation = GetMirrorBoneLocalRotation(bi, t.localRotation),
                            localScale = GetMirrorBoneLocalScale(bi, t.localScale),
                        };
                    }
                }
                for (int i = 0; i < bones.Count; i++)
                {
                    var bi = bones[i];
                    if (IsConflictBone(bi)) continue;
                    SetAnimationValueTransformPositionIfNotOriginal(bi, values[i].localPosition);
                    SetAnimationValueTransformRotationIfNotOriginal(bi, values[i].localRotation);
                    if (VAW.EditorSettings.SettingGenericMirrorScale)
                        SetAnimationValueTransformScaleIfNotOriginal(bi, values[i].localScale);
                }
            }
            #endregion

            #region Motion
            if (SelectionMotionTool)
            {
                var motionT = GetAnimationValueAnimatorMotionT();
                SetAnimationValueAnimatorMotionTIfNotOriginal(new Vector3(-motionT.x, motionT.y, motionT.z));
                var motionQ = GetAnimationValueAnimatorMotionQ();
                SetAnimationValueAnimatorMotionQIfNotOriginal(new Quaternion(motionQ.x, -motionQ.y, -motionQ.z, motionQ.w));
            }
            #endregion

            #region BlendShape
            {
                var values = new Dictionary<SkinnedMeshRenderer, Dictionary<string, float>>();
                foreach (var boneIndex in SelectionBones)
                {
                    var renderer = Bones[boneIndex].GetComponent<SkinnedMeshRenderer>();
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0) continue;
                    BlendShapeWeightSave.ActionOriginalWeights(renderer, (name, value) =>
                    {
                        if (MirrorBlendShape.TryGetValue(renderer, out Dictionary<string, string> nameTable))
                        {
                            if (nameTable.TryGetValue(name, out string mirrorName))
                            {
                                if (!values.TryGetValue(renderer, out var blendValues))
                                {
                                    blendValues = new Dictionary<string, float>();
                                    values.Add(renderer, blendValues);
                                }
                                blendValues.Add(mirrorName, GetAnimationValueBlendShape(renderer, name));
                            }
                        }
                    });
                }
                foreach (var list in values)
                {
                    foreach (var pair in list.Value)
                    {
                        SetAnimationValueBlendShapeIfNotOriginal(list.Key, pair.Key, pair.Value);
                    }
                }
            }
            #endregion

            SetPoseAfter(true);
        }
        private void SetSelectionHumanoidPose(bool position, bool rotation)
        {
            var selectHumanoidIndexes = SelectionGameObjectsHumanoidIndex();
            var selectMuscleIndexes = SelectionGameObjectsMuscleIndex();
            if (position)
            {
                foreach (var hi in selectHumanoidIndexes)
                {
                    if (HumanBonesAnimatorTDOFIndex[(int)hi] != null)
                        SetAnimationValueAnimatorTDOFIfNotOriginal(HumanBonesAnimatorTDOFIndex[(int)hi].index, Vector3.zero);
                }
            }

            {
                var hp = new HumanPose();
                GetSceneObjectHumanPose(ref hp);
                if (SelectionGameObjectsIndexOf(VAW.GameObject) >= 0)
                {
                    if (position)
                        SetAnimationValueAnimatorRootTIfNotOriginal(hp.bodyPosition);
                    if (rotation)
                        SetAnimationValueAnimatorRootQIfNotOriginal(hp.bodyRotation);
                }
                if (rotation)
                {
                    foreach (var muscle in selectMuscleIndexes)
                        SetAnimationValueAnimatorMuscleIfNotOriginal(muscle, hp.muscles[muscle]);
                }
            }
        }
        private void SetSelectionGenericPose(bool position, bool rotation, bool scale)
        {
            var selectOriginalIKTargetsBoneIndexes = originalIK.SelectionOriginalIKTargetsBoneIndexes();
            var boneIndexes = new List<int>(SelectionBones);
            boneIndexes.AddRange(selectOriginalIKTargetsBoneIndexes);
            foreach (var bi in boneIndexes)
            {
                if (IsConflictBone(bi)) continue;
                if (position)
                    SetAnimationValueTransformPositionIfNotOriginal(bi, Bones[bi].transform.localPosition);
                if (rotation)
                    SetAnimationValueTransformRotationIfNotOriginal(bi, Bones[bi].transform.localRotation);
                if (scale)
                    SetAnimationValueTransformScaleIfNotOriginal(bi, Bones[bi].transform.localScale);
            }
        }
        private void SetSelectionCommonOriginal()
        {
            #region Motion
            if (SelectionMotionTool)
            {
                SetAnimationValueAnimatorMotionTIfNotOriginal(Vector3.zero);
                SetAnimationValueAnimatorMotionQIfNotOriginal(Quaternion.identity);
            }
            #endregion

            #region BlendShape
            {
                foreach (var boneIndex in SelectionBones)
                {
                    var renderer = Bones[boneIndex].GetComponent<SkinnedMeshRenderer>();
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount == 0) continue;
                    BlendShapeWeightSave.ActionOriginalWeights(renderer, (name, value) =>
                    {
                        SetAnimationValueBlendShapeIfNotOriginal(renderer, name, BlendShapeWeightSave.GetDefaultWeight(renderer, name));
                    });
                }
            }
            #endregion
        }

        public bool SetPoseBefore(string undoName)
        {
            if (!BeginChangeAnimationCurve(CurrentClip, undoName))
                return false;
            return true;
        }
        public void SetPoseAfter(bool calcIK = false)
        {
            SetUpdateSampleAnimation(true);
            if (!calcIK)
            {
                SetSynchroIKtargetAll();
                updatePoseFixAnimation = true;
            }
            else
            {
                ResetSynchroIKtargetAll();
                updatePoseFixAnimation = false;
            }
        }

        private void ResetAllHaveAnimationCurve(PoseFlags flags = PoseFlags.All)
        {
            TransformPoseSave.ResetOriginalTransform();
            BlendShapeWeightSave.ResetOriginalWeight();

            #region Humanoid
            if (IsHuman && (flags & PoseFlags.Humanoid) != 0)
            {
                SetAnimationValueAnimatorRootT(new Vector3(0, 1, 0));   //Always create
                SetAnimationValueAnimatorRootQ(Quaternion.identity);    //Always create
                for (int mi = 0; mi < HumanTrait.MuscleCount; mi++)
                {
                    SetAnimationValueAnimatorMuscleIfNotOriginal(mi, 0f);
                }
                for (var tdof = (AnimatorTDOFIndex)0; tdof < AnimatorTDOFIndex.Total; tdof++)
                {
                    SetAnimationValueAnimatorTDOFIfNotOriginal(tdof, Vector3.zero);
                }
            }
            #endregion

            #region Generic
            if ((flags & PoseFlags.Generic) != 0)
            {
                for (int bi = 0; bi < Skeleton.Bones.Length; bi++)
                {
                    if (IsConflictBone(bi)) continue;
                    SetAnimationValueTransformPositionIfNotOriginal(bi, BoneSaveTransforms[bi].localPosition);
                    SetAnimationValueTransformRotationIfNotOriginal(bi, BoneSaveTransforms[bi].localRotation);
                    SetAnimationValueTransformScaleIfNotOriginal(bi, BoneSaveTransforms[bi].localScale);
                }
            }
            #endregion

            #region BlendShape
            if ((flags & PoseFlags.BlendShape) != 0)
            {
                foreach (var renderer in Renderers)
                {
                    var smr = renderer as SkinnedMeshRenderer;
                    if (smr == null || smr.sharedMesh == null || smr.sharedMesh.blendShapeCount <= 0) continue;
                    for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    {
                        var name = smr.sharedMesh.GetBlendShapeName(i);
                        SetAnimationValueBlendShapeIfNotOriginal(smr, name, BlendShapeWeightSave.GetDefaultWeight(smr, name));
                    }
                }
            }
            #endregion
        }
        private void SetAllChangedAnimationCurve(PoseFlags flags = PoseFlags.All)
        {
            #region Humanoid
            if (IsHuman && (flags & PoseFlags.Humanoid) != 0)
            {
                var hp = new HumanPose();
                GetSceneObjectHumanPose(ref hp);
                SetAnimationValueAnimatorRootT(hp.bodyPosition);    //Always create
                SetAnimationValueAnimatorRootQIfNotOriginal(hp.bodyRotation);
                for (int i = 0; i < hp.muscles.Length; i++)
                {
                    SetAnimationValueAnimatorMuscleIfNotOriginal(i, hp.muscles[i]);
                }
            }
            #endregion

            #region Generic
            if ((flags & PoseFlags.Generic) != 0)
            {
                for (int i = 0; i < Bones.Length; i++)
                {
                    if (IsConflictBone(i)) continue;
                    var t = Bones[i].transform;
                    SetAnimationValueTransformPositionIfNotOriginal(i, t.localPosition);
                    SetAnimationValueTransformRotationIfNotOriginal(i, t.localRotation);
                    SetAnimationValueTransformScaleIfNotOriginal(i, t.localScale);
                }
            }
            #endregion

            #region BlendShape
            if ((flags & PoseFlags.BlendShape) != 0)
            {
                foreach (var r in Renderers)
                {
                    var renderer = r as SkinnedMeshRenderer;
                    if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount <= 0) continue;
                    for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    {
                        var name = renderer.sharedMesh.GetBlendShapeName(i);
                        var weight = renderer.GetBlendShapeWeight(i);
                        SetAnimationValueBlendShapeIfNotOriginal(renderer, name, weight);
                    }
                }
            }
            #endregion
        }

        private bool AddRemoveInbetween(int plusFrame)
        {
            var bindings = AnimationUtility.GetCurveBindings(CurrentClip);
            var rbindings = AnimationUtility.GetObjectReferenceCurveBindings(CurrentClip);
            var events = AnimationUtility.GetAnimationEvents(CurrentClip);

            #region HasCheck
            if (plusFrame < 0)
            {
                bool hasCurrent = false;
                {
                    foreach (var binding in bindings)
                    {
                        var curve = GetEditorCurveCache(binding);
                        if (curve == null)
                            continue;
                        if (AnimationCommon.FindKeyframeAtTime(curve, CurrentTime) >= 0)
                        {
                            hasCurrent |= true;
                            break;
                        }
                    }
                    foreach (var rbinding in rbindings)
                    {
                        var keys = AnimationUtility.GetObjectReferenceCurve(CurrentClip, rbinding);
                        if (keys == null)
                            continue;
                        if (AnimationCommon.FindKeyframeAtTime(keys, CurrentTime) >= 0)
                        {
                            hasCurrent |= true;
                            break;
                        }
                    }
                    {
                        if (AnimationCommon.FindKeyframeAtTime(events, CurrentTime) >= 0)
                        {
                            hasCurrent |= true;
                        }
                    }
                }
                if (hasCurrent)
                {
                    var nextTime = EditorCommon.SnapToFrame(CurrentTime + GetFrameTime(1), CurrentClip.frameRate);
                    foreach (var binding in bindings)
                    {
                        var curve = GetEditorCurveCache(binding);
                        if (curve == null)
                            continue;
                        if (AnimationCommon.FindKeyframeAtTime(curve, nextTime) >= 0)
                            return false;
                    }
                    foreach (var rbinding in rbindings)
                    {
                        var keys = AnimationUtility.GetObjectReferenceCurve(CurrentClip, rbinding);
                        if (keys == null)
                            continue;
                        if (AnimationCommon.FindKeyframeAtTime(keys, nextTime) >= 0)
                            return false;
                    }
                    if (events.Length > 0)
                    {
                        if (AnimationCommon.FindKeyframeAtTime(events, nextTime) >= 0)
                        {
                            return false;
                        }
                    }
                }
            }
            #endregion

            if (!BeginChangeAnimationCurve(CurrentClip, plusFrame > 0 ? "Add In between" : "Remove In between"))
                return false;

            #region MoveKeys
            var plusTime = GetFrameTime(plusFrame);
            {
                foreach (var binding in bindings)
                {
                    var curve = GetEditorCurveCache(binding);
                    if (curve == null)
                        continue;
                    var index = FindAfterNearKeyframeAtTime(curve, CurrentTime);
                    if (index < 0)
                        continue;
                    if (plusTime > 0f)
                    {
                        for (int i = curve.length - 1; i >= index; i--)
                        {
                            var key = curve[i];
                            key.time = EditorCommon.SnapToFrame(key.time + plusTime, CurrentClip.frameRate);
                            curve.MoveKey(i, key);
                        }
                    }
                    else
                    {
                        for (int i = index; i < curve.length; i++)
                        {
                            var key = curve[i];
                            key.time = EditorCommon.SnapToFrame(key.time + plusTime, CurrentClip.frameRate);
                            curve.MoveKey(i, key);
                        }
                    }
                    SetEditorCurveCache(binding, curve);
                }
                UpdateSyncEditorCurveClip();
            }
            {
                foreach (var rbinding in rbindings)
                {
                    var keys = AnimationUtility.GetObjectReferenceCurve(CurrentClip, rbinding);
                    if (keys == null)
                        continue;
                    var index = FindAfterNearKeyframeAtTime(keys, CurrentTime);
                    if (index < 0)
                        continue;
                    if (plusTime > 0f)
                    {
                        for (int i = keys.Length - 1; i >= index; i--)
                        {
                            var key = keys[i];
                            key.time = EditorCommon.SnapToFrame(key.time + plusTime, CurrentClip.frameRate);
                            keys[i] = key;
                        }
                    }
                    else
                    {
                        for (int i = index; i < keys.Length; i++)
                        {
                            var key = keys[i];
                            key.time = EditorCommon.SnapToFrame(key.time + plusTime, CurrentClip.frameRate);
                            keys[i] = key;
                        }
                    }
                    AnimationUtility.SetObjectReferenceCurve(CurrentClip, rbinding, keys);
                }
            }
            if (events.Length > 0)
            {
                var index = FindAfterNearKeyframeAtTime(events, CurrentTime);
                if (index >= 0)
                {
                    if (plusTime > 0f)
                    {
                        for (int i = events.Length - 1; i >= index; i--)
                        {
                            var ev = events[i];
                            ev.time = EditorCommon.SnapToFrame(ev.time + plusTime, CurrentClip.frameRate);
                            events[i] = ev;
                        }
                    }
                    else
                    {
                        for (int i = index; i < events.Length; i++)
                        {
                            var ev = events[i];
                            ev.time = EditorCommon.SnapToFrame(ev.time + plusTime, CurrentClip.frameRate);
                            events[i] = ev;
                        }
                    }
                    AnimationUtility.SetAnimationEvents(CurrentClip, events);
                }
            }
            #endregion

            SetUpdateSampleAnimation();
            UAw.ClearKeySelections();
            UAw.ForceRefresh();

            return true;
        }

        private void BakeBetweenHumanoidBasicCurve(int beginFrame, int endFrame)
        {
            for (int i = 0; i < 3; i++)
            {
                var curve = GetAnimationCurveAnimatorRootT(i, false);
                if (curve == null) continue;
                AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                SetAnimationCurveAnimatorRootT(i, curve);
            }
            for (int i = 0; i < 4; i++)
            {
                var curve = GetAnimationCurveAnimatorRootQ(i, false);
                if (curve == null) continue;
                AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                SetAnimationCurveAnimatorRootQ(i, curve);
            }
            for (int i = 0; i < 3; i++)
            {
                var curve = GetAnimationCurveAnimatorMotionT(i, false);
                if (curve == null) continue;
                AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                SetAnimationCurveAnimatorMotionT(i, curve);
            }
            for (int i = 0; i < 4; i++)
            {
                var curve = GetAnimationCurveAnimatorMotionQ(i, false);
                if (curve == null) continue;
                AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                SetAnimationCurveAnimatorMotionQ(i, curve);
            }
            for (var hi = HumanBodyBones.Hips; hi < HumanBodyBones.LastBone; hi++)
            {
                if (hi <= HumanBodyBones.Jaw || hi == HumanBodyBones.UpperChest)
                {
                    for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                    {
                        var mi = HumanTrait.MuscleFromBone((int)hi, dofIndex);
                        if (mi < 0) continue;

                        var curve = GetAnimationCurveAnimatorMuscle(mi, false);
                        if (curve == null) continue;
                        AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                        SetAnimationCurveAnimatorMuscle(mi, curve);
                    }
                }
            }
            for (var tdofIndex = (AnimatorTDOFIndex)0; tdofIndex < AnimatorTDOFIndex.Total; tdofIndex++)
            {
                for (int dof = 0; dof < 3; dof++)
                {
                    var curve = GetAnimationCurveAnimatorTDOF(tdofIndex, dof, false);
                    if (curve == null) continue;
                    AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                    SetAnimationCurveAnimatorTDOF(tdofIndex, dof, curve);
                }
            }
        }
        private void BakeBetweenGenericAncestorCurve(int boneIndex, int beginFrame, int endFrame)
        {
            while (boneIndex > 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    var curve = GetAnimationCurveTransformPosition(boneIndex, i, false);
                    if (curve == null) continue;
                    AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                    SetAnimationCurveTransformPosition(boneIndex, i, curve);
                }
                {
                    var mode = GetHaveAnimationCurveTransformRotationMode(boneIndex);
                    switch (mode)
                    {
                        case URotationCurveInterpolation.Mode.RawQuaternions:
                            {
                                for (int i = 0; i < 4; i++)
                                {
                                    var curve = GetAnimationCurveTransformRotation(boneIndex, i, URotationCurveInterpolation.Mode.RawQuaternions, false);
                                    if (curve == null) continue;
                                    AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                                    SetAnimationCurveTransformRotation(boneIndex, i, URotationCurveInterpolation.Mode.RawQuaternions, curve);
                                }
                            }
                            break;
                        case URotationCurveInterpolation.Mode.RawEuler:
                        case URotationCurveInterpolation.Mode.Baked:
                        case URotationCurveInterpolation.Mode.NonBaked:
                            {
                                for (int i = 0; i < 3; i++)
                                {
                                    var curve = GetAnimationCurveTransformRotation(boneIndex, i, mode, false);
                                    if (curve == null) continue;
                                    AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                                    SetAnimationCurveTransformRotation(boneIndex, i, mode, curve);
                                }
                            }
                            break;
                    }
                }
                for (int i = 0; i < 3; i++)
                {
                    var curve = GetAnimationCurveTransformScale(boneIndex, i, false);
                    if (curve == null) continue;
                    AnimationCommon.BakeBetweenKeyframe(curve, beginFrame, endFrame, CurrentClip.frameRate);
                    SetAnimationCurveTransformScale(boneIndex, i, curve);
                }

                boneIndex = ParentBoneIndexes[boneIndex];
            }
        }

        public List<float> GetHumanoidKeyframeTimeList(AnimationClip clip, HumanBodyBones humanoidIndex)
        {
            var keyTimes = new HashSet<float>();
            #region KeyTimes
            {
                void AddKeyTimes(HumanBodyBones hi)
                {
                    for (int dofIndex = 0; dofIndex < 3; dofIndex++)
                    {
                        var mi = HumanTrait.MuscleFromBone((int)hi, dofIndex);
                        if (mi < 0) continue;
                        var curve = AnimationUtility.GetEditorCurve(clip, AnimatorMuscleBindings[mi]);
                        if (curve == null) continue;
                        for (int i = 0; i < curve.length; i++)
                            keyTimes.Add(curve[i].time);
                    }
                    if (HumanoidHasTDoF && HumanBonesAnimatorTDOFIndex[(int)hi] != null)
                    {
                        var tdofIndex = HumanBonesAnimatorTDOFIndex[(int)hi].index;
                        for (int dof = 0; dof < 3; dof++)
                        {
                            var curve = AnimationUtility.GetEditorCurve(clip, AnimatorTDOFBindings[(int)tdofIndex][dof]);
                            if (curve == null)
                                continue;
                            for (int i = 0; i < curve.length; i++)
                                keyTimes.Add(curve[i].time);
                        }
                    }
                    var phi = (HumanBodyBones)HumanTrait.GetParentBone((int)hi);
                    if (phi >= 0)
                    {
                        AddKeyTimes(phi);
                    }
                }

                keyTimes.Add(0f);
                keyTimes.Add(clip.length);
                AddKeyTimes(humanoidIndex);
            }
            #endregion
            var list = keyTimes.ToList();
            list.Sort();
            return list;
        }

        public bool IsHaveAnimationCurveAnimatorRootT()
        {
            if (CurrentClip == null)
                return false;
            for (int i = 0; i < 3; i++)
            {
                var curve = GetEditorCurveCache(AnimationCommon.Binding.RootT[i]);
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Vector3 GetAnimationValueAnimatorRootT(float time = -1f)
        {
            if (CurrentClip == null)
                return Vector3.zero;
            time = GetFrameSnapTime(time);
            LoadTmpCurves(AnimationCommon.Binding.RootT);
            return tmpCurves.EvaluateVector3(time);
        }
        public void SetAnimationValueAnimatorRootTIfNotOriginal(Vector3 value3, float time = -1f)
        {
            if (IsHaveAnimationCurveAnimatorRootT() ||
                !Mathf.Approximately(value3.x, 0f) ||
                !Mathf.Approximately(value3.y, 0f) ||
                !Mathf.Approximately(value3.z, 0f))
            {
                SetAnimationValueAnimatorRootT(value3, time);
            }
        }
        public void SetAnimationValueAnimatorRootT(Vector3 value3, float time = -1f)
        {
            if (IsWriteLockBone(RootMotionBoneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change RootT"))
                return;
            time = GetFrameSnapTime(time);
            for (int i = 0; i < 3; i++)
            {
                var curve = GetAnimationCurveAnimatorRootT(i);
                AnimationCommon.SetKeyframe(curve, time, value3[i]);
                SetAnimationCurveAnimatorRootT(i, curve);
            }
        }
        public AnimationCurve GetAnimationCurveAnimatorRootT(int dof, bool notNull = true)
        {
            var defaultValue = IsHuman ? new Vector3(0f, 1f, 0f) : Vector3.zero;
            return GetOrCreateEditorCurveCache(AnimationCommon.Binding.RootT[dof], defaultValue[dof], notNull);
        }
        public void SetAnimationCurveAnimatorRootT(int dof, AnimationCurve curve)
        {
            if (IsWriteLockBone(RootMotionBoneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change RootT"))
            {
                SetEditorCurveCache(AnimationCommon.Binding.RootT[dof], curve);
            }
        }

        public bool IsHaveAnimationCurveAnimatorRootQ()
        {
            if (CurrentClip == null)
                return false;
            for (int i = 0; i < 4; i++)
            {
                var curve = GetEditorCurveCache(AnimationCommon.Binding.RootQ[i]);
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Quaternion GetAnimationValueAnimatorRootQ(float time = -1f)
        {
            if (CurrentClip == null)
                return Quaternion.identity;
            time = GetFrameSnapTime(time);
            LoadTmpCurves(AnimationCommon.Binding.RootQ);
            return tmpCurves.EvaluateQuaternionNormalized(time);
        }
        public void SetAnimationValueAnimatorRootQIfNotOriginal(Quaternion rotation, float time = -1f)
        {
            if (IsHaveAnimationCurveAnimatorRootQ() ||
                !Mathf.Approximately(rotation.x, 0f) ||
                !Mathf.Approximately(rotation.y, 0f) ||
                !Mathf.Approximately(rotation.z, 0f) ||
                !Mathf.Approximately(rotation.w, 1f))
            {
                SetAnimationValueAnimatorRootQ(rotation, time);
            }
        }
        public void SetAnimationValueAnimatorRootQ(Quaternion rotation, float time = -1f)
        {
            if (IsWriteLockBone(RootMotionBoneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change RootQ"))
                return;
            time = GetFrameSnapTime(time);
            tmpCurves.Clear();
            for (int i = 0; i < 4; i++)
            {
                tmpCurves.curves[i] = GetAnimationCurveAnimatorRootQ(i);
            }
            rotation = FixReverseRotationQuaternion(tmpCurves.curves, time, rotation);
            for (int i = 0; i < 4; i++)
            {
                AnimationCommon.SetKeyframe(tmpCurves.curves[i], time, rotation[i]);
                SetAnimationCurveAnimatorRootQ(i, tmpCurves.curves[i]);
            }
        }
        public AnimationCurve GetAnimationCurveAnimatorRootQ(int dof, bool notNull = true)
        {
            return GetOrCreateEditorCurveCache(AnimationCommon.Binding.RootQ[dof], Quaternion.identity[dof], notNull);
        }
        public void SetAnimationCurveAnimatorRootQ(int dof, AnimationCurve curve)
        {
            if (IsWriteLockBone(RootMotionBoneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change RootQ"))
            {
                SetEditorCurveCache(AnimationCommon.Binding.RootQ[dof], curve);
            }
        }

        public bool IsHaveAnimationCurveAnimatorMotionT()
        {
            if (CurrentClip == null)
                return false;
            for (int i = 0; i < 3; i++)
            {
                var curve = GetEditorCurveCache(AnimationCommon.Binding.MotionT[i]);
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Vector3 GetAnimationValueAnimatorMotionT(float time = -1f)
        {
            if (CurrentClip == null)
                return Vector3.zero;
            time = GetFrameSnapTime(time);
            LoadTmpCurves(AnimationCommon.Binding.MotionT);
            return tmpCurves.EvaluateVector3(time);
        }
        public void SetAnimationValueAnimatorMotionTIfNotOriginal(Vector3 value3, float time = -1f)
        {
            if (IsHaveAnimationCurveAnimatorMotionT() ||
                !Mathf.Approximately(value3.x, 0f) ||
                !Mathf.Approximately(value3.y, 0f) ||
                !Mathf.Approximately(value3.z, 0f))
            {
                SetAnimationValueAnimatorMotionT(value3, time);
            }
        }
        public void SetAnimationValueAnimatorMotionT(Vector3 value3, float time = -1f)
        {
            if (IsWriteLockBone(RootMotionBoneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change MotionT"))
                return;
            time = GetFrameSnapTime(time);
            for (int i = 0; i < 3; i++)
            {
                var curve = GetAnimationCurveAnimatorMotionT(i);
                AnimationCommon.SetKeyframe(curve, time, value3[i]);
                SetAnimationCurveAnimatorMotionT(i, curve);
            }
        }
        public AnimationCurve GetAnimationCurveAnimatorMotionT(int dof, bool notNull = true)
        {
            return GetOrCreateEditorCurveCache(AnimationCommon.Binding.MotionT[dof], 0f, notNull);
        }
        public void SetAnimationCurveAnimatorMotionT(int dof, AnimationCurve curve)
        {
            if (IsWriteLockBone(RootMotionBoneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change MotionT"))
            {
                SetEditorCurveCache(AnimationCommon.Binding.MotionT[dof], curve);
            }
        }

        public bool IsHaveAnimationCurveAnimatorMotionQ()
        {
            if (CurrentClip == null)
                return false;
            for (int i = 0; i < 4; i++)
            {
                var curve = GetEditorCurveCache(AnimationCommon.Binding.MotionQ[i]);
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Quaternion GetAnimationValueAnimatorMotionQ(float time = -1f)
        {
            if (CurrentClip == null)
                return Quaternion.identity;
            time = GetFrameSnapTime(time);
            LoadTmpCurves(AnimationCommon.Binding.MotionQ);
            return tmpCurves.EvaluateQuaternionNormalized(time);
        }
        public void SetAnimationValueAnimatorMotionQIfNotOriginal(Quaternion rotation, float time = -1f)
        {
            if (IsHaveAnimationCurveAnimatorMotionQ() ||
                !Mathf.Approximately(rotation.x, 0f) ||
                !Mathf.Approximately(rotation.y, 0f) ||
                !Mathf.Approximately(rotation.z, 0f) ||
                !Mathf.Approximately(rotation.w, 1f))
            {
                SetAnimationValueAnimatorMotionQ(rotation, time);
            }
        }
        public void SetAnimationValueAnimatorMotionQ(Quaternion rotation, float time = -1f)
        {
            if (IsWriteLockBone(RootMotionBoneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change MotionQ"))
                return;
            time = GetFrameSnapTime(time);
            tmpCurves.Clear();
            for (int i = 0; i < 4; i++)
            {
                tmpCurves.curves[i] = GetAnimationCurveAnimatorMotionQ(i);
            }
            rotation = FixReverseRotationQuaternion(tmpCurves.curves, time, rotation);
            for (int i = 0; i < 4; i++)
            {
                AnimationCommon.SetKeyframe(tmpCurves.curves[i], time, rotation[i]);
                SetAnimationCurveAnimatorMotionQ(i, tmpCurves.curves[i]);
            }
        }
        public AnimationCurve GetAnimationCurveAnimatorMotionQ(int dof, bool notNull = true)
        {
            return GetOrCreateEditorCurveCache(AnimationCommon.Binding.MotionQ[dof], Quaternion.identity[dof], notNull);
        }
        public void SetAnimationCurveAnimatorMotionQ(int dof, AnimationCurve curve)
        {
            if (IsWriteLockBone(RootMotionBoneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change MotionQ"))
            {
                SetEditorCurveCache(AnimationCommon.Binding.MotionQ[dof], curve);
            }
        }

        public bool IsHaveAnimationCurveAnimatorIkT(AnimatorIKIndex ikIndex)
        {
            if (CurrentClip == null)
                return false;
            for (int i = 0; i < 3; i++)
            {
                var curve = GetEditorCurveCache(AnimatorIkTBindings[(int)ikIndex][i]);
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Vector3 GetAnimationValueAnimatorIkT(AnimatorIKIndex ikIndex, float time = -1f)
        {
            if (CurrentClip == null)
                return Vector3.zero;
            time = GetFrameSnapTime(time);
            LoadTmpCurves(AnimatorIkTBindings[(int)ikIndex]);
            return tmpCurves.EvaluateVector3(time);
        }
        public void SetAnimationValueAnimatorIkTIfNotOriginal(AnimatorIKIndex ikIndex, Vector3 value3, float time = -1f)
        {
            if (IsHaveAnimationCurveAnimatorIkT(ikIndex) ||
                !Mathf.Approximately(value3.x, 0f) ||
                !Mathf.Approximately(value3.y, 0f) ||
                !Mathf.Approximately(value3.z, 0f))
            {
                SetAnimationValueAnimatorIkT(ikIndex, value3, time);
            }
        }
        public void SetAnimationValueAnimatorIkT(AnimatorIKIndex ikIndex, Vector3 value3, float time = -1f)
        {
            if (ikIndex < 0 || ikIndex >= AnimatorIKIndex.Total)
                return;
            if (IsWriteLockBone(AnimatorIKIndex2HumanBodyBones[(int)ikIndex]))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change IK T"))
                return;
            time = GetFrameSnapTime(time);
            for (int i = 0; i < 3; i++)
            {
                var curve = GetAnimationCurveAnimatorIkT(ikIndex, i);
                AnimationCommon.SetKeyframe(curve, time, value3[i]);
                SetAnimationCurveAnimatorIkT(ikIndex, i, curve);
            }
        }
        public AnimationCurve GetAnimationCurveAnimatorIkT(AnimatorIKIndex ikIndex, int dof, bool notNull = true)
        {
            if (ikIndex < 0 || ikIndex >= AnimatorIKIndex.Total)
                return null;
            return GetOrCreateEditorCurveCache(AnimatorIkTBindings[(int)ikIndex][dof], 0f, notNull);
        }
        public void SetAnimationCurveAnimatorIkT(AnimatorIKIndex ikIndex, int dof, AnimationCurve curve)
        {
            if (ikIndex < 0 || ikIndex >= AnimatorIKIndex.Total)
                return;
            if (IsWriteLockBone(AnimatorIKIndex2HumanBodyBones[(int)ikIndex]))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change IK T"))
            {
                SetEditorCurveCache(AnimatorIkTBindings[(int)ikIndex][dof], curve);
            }
        }

        public bool IsHaveAnimationCurveAnimatorIkQ(AnimatorIKIndex ikIndex)
        {
            if (CurrentClip == null)
                return false;
            for (int i = 0; i < 4; i++)
            {
                var curve = GetEditorCurveCache(AnimatorIkQBindings[(int)ikIndex][i]);
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Quaternion GetAnimationValueAnimatorIkQ(AnimatorIKIndex ikIndex, float time = -1f)
        {
            if (CurrentClip == null)
                return Quaternion.identity;
            time = GetFrameSnapTime(time);
            LoadTmpCurves(AnimatorIkQBindings[(int)ikIndex]);
            return tmpCurves.EvaluateQuaternionNormalized(time);
        }
        public void SetAnimationValueAnimatorIkQIfNotOriginal(AnimatorIKIndex ikIndex, Quaternion rotation, float time = -1f)
        {
            if (IsHaveAnimationCurveAnimatorIkQ(ikIndex) ||
                !Mathf.Approximately(rotation.x, 0f) ||
                !Mathf.Approximately(rotation.y, 0f) ||
                !Mathf.Approximately(rotation.z, 0f) ||
                !Mathf.Approximately(rotation.w, 1f))
            {
                SetAnimationValueAnimatorIkQ(ikIndex, rotation, time);
            }
        }
        public void SetAnimationValueAnimatorIkQ(AnimatorIKIndex ikIndex, Quaternion rotation, float time = -1f)
        {
            if (ikIndex < 0 || ikIndex >= AnimatorIKIndex.Total)
                return;
            if (IsWriteLockBone(AnimatorIKIndex2HumanBodyBones[(int)ikIndex]))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change IK Q"))
                return;
            time = GetFrameSnapTime(time);
            tmpCurves.Clear();
            for (int i = 0; i < 4; i++)
            {
                tmpCurves.curves[i] = GetAnimationCurveAnimatorIkQ(ikIndex, i);
            }
            rotation = FixReverseRotationQuaternion(tmpCurves.curves, time, rotation);
            for (int i = 0; i < 4; i++)
            {
                AnimationCommon.SetKeyframe(tmpCurves.curves[i], time, rotation[i]);
                SetAnimationCurveAnimatorIkQ(ikIndex, i, tmpCurves.curves[i]);
            }
        }
        public AnimationCurve GetAnimationCurveAnimatorIkQ(AnimatorIKIndex ikIndex, int dof, bool notNull = true)
        {
            if (ikIndex < 0 || ikIndex >= AnimatorIKIndex.Total)
                return null;
            return GetOrCreateEditorCurveCache(AnimatorIkQBindings[(int)ikIndex][dof], Quaternion.identity[dof], notNull);
        }
        public void SetAnimationCurveAnimatorIkQ(AnimatorIKIndex ikIndex, int dof, AnimationCurve curve)
        {
            if (ikIndex < 0 || ikIndex >= AnimatorIKIndex.Total)
                return;
            if (IsWriteLockBone(AnimatorIKIndex2HumanBodyBones[(int)ikIndex]))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change IK Q"))
            {
                SetEditorCurveCache(AnimatorIkQBindings[(int)ikIndex][dof], curve);
            }
        }

        public bool IsHaveAnimationCurveAnimatorTDOF(AnimatorTDOFIndex tdofIndex)
        {
            if (CurrentClip == null)
                return false;
            for (int i = 0; i < 3; i++)
            {
                var curve = GetEditorCurveCache(AnimatorTDOFBindings[(int)tdofIndex][i]);
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Vector3 GetAnimationValueAnimatorTDOF(AnimatorTDOFIndex tdofIndex, float time = -1f)
        {
            if (CurrentClip == null)
                return Vector3.zero;
            time = GetFrameSnapTime(time);
            LoadTmpCurves(AnimatorTDOFBindings[(int)tdofIndex]);
            return tmpCurves.EvaluateVector3(time);
        }
        public void SetAnimationValueAnimatorTDOFIfNotOriginal(AnimatorTDOFIndex tdofIndex, Vector3 value3, float time = -1f)
        {
            if (IsHaveAnimationCurveAnimatorTDOF(tdofIndex) ||
                !Mathf.Approximately(value3.x, 0f) ||
                !Mathf.Approximately(value3.y, 0f) ||
                !Mathf.Approximately(value3.z, 0f))
            {
                SetAnimationValueAnimatorTDOF(tdofIndex, value3, time);
            }
        }
        public void SetAnimationValueAnimatorTDOF(AnimatorTDOFIndex tdofIndex, Vector3 value3, float time = -1f)
        {
            if (tdofIndex < 0 || tdofIndex >= AnimatorTDOFIndex.Total)
                return;
            if (IsWriteLockBone(AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex]))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change TDOF"))
                return;
            time = GetFrameSnapTime(time);
            for (int dof = 0; dof < 3; dof++)
            {
                var curve = GetAnimationCurveAnimatorTDOF(tdofIndex, dof);
                AnimationCommon.SetKeyframe(curve, time, value3[dof]);
                SetAnimationCurveAnimatorTDOF(tdofIndex, dof, curve);
            }
        }
        public AnimationCurve GetAnimationCurveAnimatorTDOF(AnimatorTDOFIndex tdofIndex, int dof, bool notNull = true)
        {
            if (tdofIndex < 0 || tdofIndex >= AnimatorTDOFIndex.Total)
                return null;
            return GetOrCreateEditorCurveCache(AnimatorTDOFBindings[(int)tdofIndex][dof], 0f, notNull);
        }
        public void SetAnimationCurveAnimatorTDOF(AnimatorTDOFIndex tdofIndex, int dof, AnimationCurve curve)
        {
            if (tdofIndex < 0 || tdofIndex >= AnimatorTDOFIndex.Total)
                return;
            if (IsWriteLockBone(AnimatorTDOFIndex2HumanBodyBones[(int)tdofIndex]))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change TDOF"))
            {
                SetEditorCurveCache(AnimatorTDOFBindings[(int)tdofIndex][dof], curve);
            }
        }

        public bool IsHaveAnimationCurveAnimatorMuscle(int muscleIndex)
        {
            if (muscleIndex < 0 || muscleIndex >= HumanTrait.MuscleCount)
                return false;
            if (CurrentClip == null)
                return false;
            return GetEditorCurveCache(AnimatorMuscleBindings[muscleIndex]) != null;
        }
        public float GetAnimationValueAnimatorMuscle(int muscleIndex, float time = -1f)
        {
            if (muscleIndex < 0 || muscleIndex >= HumanTrait.MuscleCount)
                return 0f;
            if (CurrentClip == null)
                return 0f;
            time = GetFrameSnapTime(time);
            var curve = GetEditorCurveCache(AnimatorMuscleBindings[muscleIndex]);
            if (curve == null) return 0f;
            return curve.Evaluate(time);
        }
        public void SetAnimationValueAnimatorMuscleIfNotOriginal(int muscleIndex, float value, float time = -1f)
        {
            if (IsHaveAnimationCurveAnimatorMuscle(muscleIndex) ||
                !Mathf.Approximately(value, 0f))
            {
                SetAnimationValueAnimatorMuscle(muscleIndex, value, time);
            }
        }
        public void SetAnimationValueAnimatorMuscle(int muscleIndex, float value, float time = -1f)
        {
            if (muscleIndex < 0 || muscleIndex >= HumanTrait.MuscleCount)
                return;
            if (IsWriteLockBone((HumanBodyBones)HumanTrait.BoneFromMuscle(muscleIndex)))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change Muscle"))
                return;
            time = GetFrameSnapTime(time);
            {
                var curve = GetAnimationCurveAnimatorMuscle(muscleIndex);
                AnimationCommon.SetKeyframe(curve, time, value);
                SetAnimationCurveAnimatorMuscle(muscleIndex, curve);
            }
        }
        public AnimationCurve GetAnimationCurveAnimatorMuscle(int muscleIndex, bool notNull = true)
        {
            if (muscleIndex < 0 || muscleIndex >= HumanTrait.MuscleCount)
                return null;
            return GetOrCreateEditorCurveCache(AnimatorMuscleBindings[muscleIndex], 0f, notNull);
        }
        public void SetAnimationCurveAnimatorMuscle(int muscleIndex, AnimationCurve curve)
        {
            if (muscleIndex < 0 || muscleIndex >= HumanTrait.MuscleCount)
                return;
            if (IsWriteLockBone((HumanBodyBones)HumanTrait.BoneFromMuscle(muscleIndex)))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change Muscle"))
            {
                SetEditorCurveCache(AnimatorMuscleBindings[muscleIndex], curve);
            }
        }

        public bool IsHaveAnimationCurveTransformPosition(int boneIndex)
        {
            if (CurrentClip == null || boneIndex < 0 || boneIndex >= Skeleton.Bones.Length)
                return false;
            {
                var curve = GetEditorCurveCache(AnimationCurveBindingTransformPosition(boneIndex, 0));
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Vector3 GetAnimationValueTransformPosition(int boneIndex, float time = -1f)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return Vector3.zero;
            if (CurrentClip == null)
                return BoneSaveOriginalTransforms[boneIndex].localPosition;
            time = GetFrameSnapTime(time);
            Vector3 result = BoneSaveOriginalTransforms[boneIndex].localPosition;
            for (int i = 0; i < 3; i++)
            {
                var curve = GetEditorCurveCache(AnimationCurveBindingTransformPosition(boneIndex, i));
                if (curve != null)
                {
                    result[i] = curve.Evaluate(time);
                }
            }
            return result;
        }
        public void SetAnimationValueTransformPositionIfNotOriginal(int boneIndex, Vector3 position, float time = -1f)
        {
            if (IsHaveAnimationCurveTransformPosition(boneIndex) ||
                IsAnimationValueTransformPositionNotOriginal(boneIndex, position))
            {
                SetAnimationValueTransformPosition(boneIndex, position, time);
            }
        }
        public bool IsAnimationValueTransformPositionNotOriginal(int boneIndex, Vector3 position)
        {
            return Mathf.Abs(position.x - BoneSaveOriginalTransforms[boneIndex].localPosition.x) >= TransformPositionApproximatelyThreshold ||
                    Mathf.Abs(position.y - BoneSaveOriginalTransforms[boneIndex].localPosition.y) >= TransformPositionApproximatelyThreshold ||
                    Mathf.Abs(position.z - BoneSaveOriginalTransforms[boneIndex].localPosition.z) >= TransformPositionApproximatelyThreshold;
        }
        public void SetAnimationValueTransformPosition(int boneIndex, Vector3 position, float time = -1f)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change Transform Position"))
                return;
            time = GetFrameSnapTime(time);
            bool removeCurve = false;
            if (IsHuman && HumanoidConflict[boneIndex])
            {
                EditorCommon.ShowNotification("Conflict");
                Debug.LogErrorFormat(Language.GetText(Language.Help.LogGenericCurveHumanoidConflictError), Skeleton.Bones[boneIndex].name);
                removeCurve = true;
            }
            else if (RootMotionBoneIndex >= 0 && boneIndex == 0)
            {
                EditorCommon.ShowNotification("Conflict");
                Debug.LogErrorFormat(Language.GetText(Language.Help.LogGenericCurveRootConflictError), Skeleton.Bones[boneIndex].name);
                removeCurve = true;
            }
            if (removeCurve)
            {
                for (int i = 0; i < 3; i++)
                {
                    SetEditorCurveCache(AnimationCurveBindingTransformPosition(boneIndex, i), null);
                }
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    var curve = GetAnimationCurveTransformPosition(boneIndex, i);
                    AnimationCommon.SetKeyframe(curve, time, position[i]);
                    SetAnimationCurveTransformPosition(boneIndex, i, curve);
                }
            }
        }
        public AnimationCurve GetAnimationCurveTransformPosition(int boneIndex, int dof, bool notNull = true)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return null;
            return GetOrCreateEditorCurveCache(AnimationCurveBindingTransformPosition(boneIndex, dof), BoneSaveOriginalTransforms[boneIndex].localPosition[dof], notNull);
        }
        public void SetAnimationCurveTransformPosition(int boneIndex, int dof, AnimationCurve curve)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change Transform Position"))
            {
                SetEditorCurveCache(AnimationCurveBindingTransformPosition(boneIndex, dof), curve);
            }
        }

        public URotationCurveInterpolation.Mode GetHaveAnimationCurveTransformRotationMode(int boneIndex)
        {
            if (CurrentClip == null || boneIndex < 0 || boneIndex >= Skeleton.Bones.Length)
                return URotationCurveInterpolation.Mode.Undefined;

            if (GetEditorCurveCache(AnimationCurveBindingTransformRotation(boneIndex, 0, URotationCurveInterpolation.Mode.RawQuaternions)) != null)
                return URotationCurveInterpolation.Mode.RawQuaternions;

            if (GetEditorCurveCache(AnimationCurveBindingTransformRotation(boneIndex, 0, URotationCurveInterpolation.Mode.RawEuler)) != null)
                return URotationCurveInterpolation.Mode.RawEuler;

            if (GetEditorCurveCache(AnimationCurveBindingTransformRotation(boneIndex, 0, URotationCurveInterpolation.Mode.Baked)) != null)
                return URotationCurveInterpolation.Mode.Baked;

            if (GetEditorCurveCache(AnimationCurveBindingTransformRotation(boneIndex, 0, URotationCurveInterpolation.Mode.NonBaked)) != null)
                return URotationCurveInterpolation.Mode.NonBaked;

            return URotationCurveInterpolation.Mode.Undefined;
        }
        public Quaternion GetAnimationValueTransformRotation(int boneIndex, float time = -1f)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return Quaternion.identity;
            if (CurrentClip == null)
                return BoneSaveOriginalTransforms[boneIndex].localRotation;
            time = GetFrameSnapTime(time);

            var mode = GetHaveAnimationCurveTransformRotationMode(boneIndex);
            switch (mode)
            {
                case URotationCurveInterpolation.Mode.RawQuaternions:
                    {
                        Vector4 result = Vector4.zero;
                        for (int i = 0; i < 4; i++)
                        {
                            var binding = AnimationCurveBindingTransformRotation(boneIndex, i, URotationCurveInterpolation.Mode.RawQuaternions);
                            var curve = GetEditorCurveCache(binding);
                            if (curve != null)
                                result[i] = curve.Evaluate(time);
                        }
                        result.Normalize();
                        if (result.sqrMagnitude <= 0f)
                            return BoneSaveOriginalTransforms[boneIndex].localRotation;
                        Quaternion resultQ = Quaternion.identity;
                        for (int i = 0; i < 4; i++)
                            resultQ[i] = result[i];
                        return resultQ;
                    }
                case URotationCurveInterpolation.Mode.RawEuler:
                case URotationCurveInterpolation.Mode.Baked:
                case URotationCurveInterpolation.Mode.NonBaked:
                    {
                        Vector3 result = Vector3.zero;
                        for (int i = 0; i < 3; i++)
                        {
                            var binding = AnimationCurveBindingTransformRotation(boneIndex, i, mode);
                            var curve = GetEditorCurveCache(binding);
                            if (curve != null)
                                result[i] = curve.Evaluate(time);
                        }
                        return Quaternion.Euler(result);
                    }
                default:
                    return BoneSaveOriginalTransforms[boneIndex].localRotation;
            }
        }
        public void SetAnimationValueTransformRotationIfNotOriginal(int boneIndex, Quaternion rotation, float time = -1f)
        {
            if (GetHaveAnimationCurveTransformRotationMode(boneIndex) != URotationCurveInterpolation.Mode.Undefined ||
                IsAnimationValueTransformRotationNotOriginal(boneIndex, rotation))
            {
                SetAnimationValueTransformRotation(boneIndex, rotation, time);
            }
        }
        public bool IsAnimationValueTransformRotationNotOriginal(int boneIndex, Quaternion rotation)
        {
            var eulerAngles = rotation.eulerAngles;
            var originalEulerAngles = BoneSaveOriginalTransforms[boneIndex].localRotation.eulerAngles;
            return Mathf.Abs(eulerAngles.x - originalEulerAngles.x) >= TransformRotationApproximatelyThreshold ||
                    Mathf.Abs(eulerAngles.y - originalEulerAngles.y) >= TransformRotationApproximatelyThreshold ||
                    Mathf.Abs(eulerAngles.z - originalEulerAngles.z) >= TransformRotationApproximatelyThreshold;
        }
        public void SetAnimationValueTransformRotation(int boneIndex, Quaternion rotation, float time = -1f, URotationCurveInterpolation.Mode newCreateMode = URotationCurveInterpolation.Mode.RawQuaternions)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change Transform Rotation"))
                return;
            time = GetFrameSnapTime(time);
            bool removeCurve = false;
            if (IsHuman && HumanoidConflict[boneIndex])
            {
                EditorCommon.ShowNotification("Conflict");
                Debug.LogErrorFormat(Language.GetText(Language.Help.LogGenericCurveHumanoidConflictError), Skeleton.Bones[boneIndex].name);
                removeCurve = true;
            }
            else if (RootMotionBoneIndex >= 0 && boneIndex == 0)
            {
                EditorCommon.ShowNotification("Conflict");
                Debug.LogErrorFormat(Language.GetText(Language.Help.LogGenericCurveRootConflictError), Skeleton.Bones[boneIndex].name);
                removeCurve = true;
            }
            var mode = GetHaveAnimationCurveTransformRotationMode(boneIndex);
            if (removeCurve)
            {
                switch (mode)
                {
                    case URotationCurveInterpolation.Mode.RawQuaternions:
                        for (int i = 0; i < 4; i++)
                        {
                            SetEditorCurveCache(AnimationCurveBindingTransformRotation(boneIndex, i, URotationCurveInterpolation.Mode.RawQuaternions), null);
                        }
                        break;
                    case URotationCurveInterpolation.Mode.RawEuler:
                    case URotationCurveInterpolation.Mode.Baked:
                    case URotationCurveInterpolation.Mode.NonBaked:
                        for (int i = 0; i < 3; i++)
                        {
                            SetEditorCurveCache(AnimationCurveBindingTransformRotation(boneIndex, i, mode), null);
                        }
                        break;
                }
            }
            else
            {
                if (mode == URotationCurveInterpolation.Mode.Undefined)
                    mode = newCreateMode;
                tmpCurves.Clear();
                switch (mode)
                {
                    case URotationCurveInterpolation.Mode.RawQuaternions:
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                tmpCurves.curves[i] = GetAnimationCurveTransformRotation(boneIndex, i, mode);
                            }
                            rotation = FixReverseRotationQuaternion(tmpCurves.curves, time, rotation);
                            for (int i = 0; i < 4; i++)
                            {
                                float value = rotation[i];
                                AnimationCommon.SetKeyframe(tmpCurves.curves[i], time, value);
                                SetAnimationCurveTransformRotation(boneIndex, i, mode, tmpCurves.curves[i]);
                            }
                        }
                        break;
                    case URotationCurveInterpolation.Mode.RawEuler:
                    case URotationCurveInterpolation.Mode.Baked:
                    case URotationCurveInterpolation.Mode.NonBaked:
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                tmpCurves.curves[i] = GetAnimationCurveTransformRotation(boneIndex, i, mode);
                            }
                            var eulerAngles = FixReverseRotationEuler(tmpCurves.curves, time, rotation.eulerAngles);
                            for (int i = 0; i < 3; i++)
                            {
                                var value = eulerAngles[i];
                                AnimationCommon.SetKeyframe(tmpCurves.curves[i], time, value);
                                SetAnimationCurveTransformRotation(boneIndex, i, mode, tmpCurves.curves[i]);
                            }
                        }
                        break;
                }
            }
        }
        public AnimationCurve GetAnimationCurveTransformRotation(int boneIndex, int dof, URotationCurveInterpolation.Mode mode, bool notNull = true)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return null;
            float defaultValue = mode == URotationCurveInterpolation.Mode.RawQuaternions
                ? BoneSaveOriginalTransforms[boneIndex].localRotation[dof]
                : BoneSaveOriginalTransforms[boneIndex].localRotation.eulerAngles[dof];
            return GetOrCreateEditorCurveCache(AnimationCurveBindingTransformRotation(boneIndex, dof, mode), defaultValue, notNull);
        }
        public void SetAnimationCurveTransformRotation(int boneIndex, int dof, URotationCurveInterpolation.Mode mode, AnimationCurve curve)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change Transform Rotation"))
            {
                SetEditorCurveCache(AnimationCurveBindingTransformRotation(boneIndex, dof, mode), curve);
            }
        }

        public bool IsHaveAnimationCurveTransformScale(int boneIndex)
        {
            if (CurrentClip == null || boneIndex < 0 || boneIndex >= Skeleton.Bones.Length)
                return false;
            {
                var curve = GetEditorCurveCache(AnimationCurveBindingTransformScale(boneIndex, 0));
                if (curve != null)
                    return true;
            }
            return false;
        }
        public Vector3 GetAnimationValueTransformScale(int boneIndex, float time = -1f)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return Vector3.one;
            if (CurrentClip == null)
                return BoneSaveOriginalTransforms[boneIndex].localScale;
            time = GetFrameSnapTime(time);
            Vector3 result = BoneSaveOriginalTransforms[boneIndex].localScale;
            for (int i = 0; i < 3; i++)
            {
                var curve = GetEditorCurveCache(AnimationCurveBindingTransformScale(boneIndex, i));
                if (curve != null)
                {
                    result[i] = curve.Evaluate(time);
                }
            }
            return result;
        }
        public void SetAnimationValueTransformScaleIfNotOriginal(int boneIndex, Vector3 scale, float time = -1f)
        {
            if (IsHaveAnimationCurveTransformScale(boneIndex) ||
                IsAnimationValueTransformScaleNotOriginal(boneIndex, scale))
            {
                SetAnimationValueTransformScale(boneIndex, scale, time);
            }
        }
        public bool IsAnimationValueTransformScaleNotOriginal(int boneIndex, Vector3 scale)
        {
            return Mathf.Abs(scale.x - BoneSaveOriginalTransforms[boneIndex].localScale.x) >= TransformScaleApproximatelyThreshold ||
                    Mathf.Abs(scale.y - BoneSaveOriginalTransforms[boneIndex].localScale.y) >= TransformScaleApproximatelyThreshold ||
                    Mathf.Abs(scale.z - BoneSaveOriginalTransforms[boneIndex].localScale.z) >= TransformScaleApproximatelyThreshold;
        }
        public void SetAnimationValueTransformScale(int boneIndex, Vector3 scale, float time = -1f)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change Transform Scale"))
                return;
            time = GetFrameSnapTime(time);
            bool removeCurve = false;
            if (IsHuman && HumanoidConflict[boneIndex])
            {
                EditorCommon.ShowNotification("Conflict");
                Debug.LogErrorFormat(Language.GetText(Language.Help.LogGenericCurveHumanoidConflictError), Skeleton.Bones[boneIndex].name);
                removeCurve = true;
            }
            if (removeCurve)
            {
                for (int i = 0; i < 3; i++)
                {
                    SetEditorCurveCache(AnimationCurveBindingTransformScale(boneIndex, i), null);
                }
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    var curve = GetAnimationCurveTransformScale(boneIndex, i);
                    AnimationCommon.SetKeyframe(curve, time, scale[i]);
                    SetAnimationCurveTransformScale(boneIndex, i, curve);
                }
            }
        }
        public AnimationCurve GetAnimationCurveTransformScale(int boneIndex, int dof, bool notNull = true)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return null;
            return GetOrCreateEditorCurveCache(AnimationCurveBindingTransformScale(boneIndex, dof), BoneSaveOriginalTransforms[boneIndex].localScale[dof], notNull);
        }
        public void SetAnimationCurveTransformScale(int boneIndex, int dof, AnimationCurve curve)
        {
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change Transform Scale"))
            {
                SetEditorCurveCache(AnimationCurveBindingTransformScale(boneIndex, dof), curve);
            }
        }

        public bool IsHaveAnimationCurveBlendShape(SkinnedMeshRenderer renderer, string name)
        {
            if (CurrentClip == null || renderer == null || renderer.sharedMesh == null)
                return false;
            {
                var curve = GetEditorCurveCache(AnimationCurveBindingBlendShape(renderer, name));
                if (curve != null)
                    return true;
            }
            return false;
        }
        public float GetAnimationValueBlendShape(SkinnedMeshRenderer renderer, string name, float time = -1f)
        {
            if (CurrentClip == null || renderer == null || renderer.sharedMesh == null)
                return 0f;
            time = GetFrameSnapTime(time);
            var curve = GetEditorCurveCache(AnimationCurveBindingBlendShape(renderer, name));
            if (curve != null)
            {
                return curve.Evaluate(time);
            }
            else
            {
                return BlendShapeWeightSave.GetOriginalWeight(renderer, name);
            }
        }
        public void SetAnimationValueBlendShapeIfNotOriginal(SkinnedMeshRenderer renderer, string name, float value, float time = -1f)
        {
            if (IsHaveAnimationCurveBlendShape(renderer, name) ||
                IsAnimationValueBlendShapeNotOriginal(renderer, name, value))
            {
                SetAnimationValueBlendShape(renderer, name, value, time);
            }
        }
        public bool IsAnimationValueBlendShapeNotOriginal(SkinnedMeshRenderer renderer, string name, float value)
        {
            return !Mathf.Approximately(value, BlendShapeWeightSave.GetOriginalWeight(renderer, name));
        }
        public void SetAnimationValueBlendShape(SkinnedMeshRenderer renderer, string name, float value, float time = -1f)
        {
            if (renderer == null || renderer.sharedMesh == null)
                return;
            var boneIndex = BonesIndexOf(renderer.gameObject);
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change BlendShape"))
                return;
            time = GetFrameSnapTime(time);
            {
                var curve = GetAnimationCurveBlendShape(renderer, name);
                AnimationCommon.SetKeyframe(curve, time, value);
                SetAnimationCurveBlendShape(renderer, name, curve);
            }
        }
        public AnimationCurve GetAnimationCurveBlendShape(SkinnedMeshRenderer renderer, string name, bool notNull = true)
        {
            if (renderer == null || renderer.sharedMesh == null)
                return null;
            return GetOrCreateEditorCurveCache(AnimationCurveBindingBlendShape(renderer, name), BlendShapeWeightSave.GetOriginalWeight(renderer, name), notNull);
        }
        public void SetAnimationCurveBlendShape(SkinnedMeshRenderer renderer, string name, AnimationCurve curve)
        {
            if (renderer == null || renderer.sharedMesh == null)
                return;
            var boneIndex = BonesIndexOf(renderer.gameObject);
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change BlendShape"))
            {
                SetEditorCurveCache(AnimationCurveBindingBlendShape(renderer, name), curve);
            }
        }

        public bool IsHaveAnimationCurveCustomProperty(EditorCurveBinding binding)
        {
            if (CurrentClip == null)
                return false;
            return GetEditorCurveCache(binding) != null;
        }
        public float GetAnimationValueCustomProperty(EditorCurveBinding binding, float time = -1f)
        {
            if (CurrentClip == null)
                return 0f;
            time = GetFrameSnapTime(time);
            var curve = GetEditorCurveCache(binding);
            if (curve == null)
            {
                AnimationUtility.GetFloatValue(VAW.GameObject, binding, out float value);
                return value;
            }
            return curve.Evaluate(time);
        }
        public void SetAnimationValueCustomPropertyIfNotOriginal(EditorCurveBinding binding, float value, float time = -1f)
        {
            if (IsHaveAnimationCurveCustomProperty(binding) ||
                !Mathf.Approximately(value, 0f))
            {
                SetAnimationValueCustomProperty(binding, value, time);
            }
        }
        public void SetAnimationValueCustomProperty(EditorCurveBinding binding, float value, float time = -1f)
        {
            var boneIndex = GetBoneIndexFromCurveBinding(binding);
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (!BeginChangeAnimationCurve(CurrentClip, "Change Property"))
                return;
            time = GetFrameSnapTime(time);
            {
                var curve = GetAnimationCurveCustomProperty(binding);
                AnimationCommon.SetKeyframe(curve, time, value);
                SetAnimationCurveCustomProperty(binding, curve);
            }
        }
        public AnimationCurve GetAnimationCurveCustomProperty(EditorCurveBinding binding, bool notNull = true)
        {
            var curve = GetEditorCurveCache(binding);
            if (curve == null && notNull)
            {
                AnimationUtility.GetFloatValue(VAW.GameObject, binding, out float value);
                curve = GetOrCreateEditorCurveCache(binding, value, true);
            }
            return curve;
        }
        public void SetAnimationCurveCustomProperty(EditorCurveBinding binding, AnimationCurve curve)
        {
            var boneIndex = GetBoneIndexFromCurveBinding(binding);
            if (boneIndex < 0 || boneIndex >= Bones.Length)
                return;
            if (IsWriteLockBone(boneIndex))
                return;
            if (BeginChangeAnimationCurve(CurrentClip, "Change Property"))
            {
                SetEditorCurveCache(binding, curve);
            }
        }
        #endregion
    }
}
