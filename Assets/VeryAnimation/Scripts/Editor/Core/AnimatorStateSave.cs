using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace VeryAnimation
{
    internal sealed class AnimatorStateSave
    {
        private TransformPoseSave.SaveData gameObjectTransform;

        #region Animator
        private AnimatorStateInfo[] currentAnimatorStateInfo;
        private AnimatorStateInfo[] nextAnimatorStateInfo;
        private class SaveParameter
        {
            public int nameHash;
            public UnityEngine.AnimatorControllerParameterType type;
            public object value;
        }
        private SaveParameter[] animatorParameters;
        #endregion

        public AnimatorStateSave(Animator animator)
        {
            Save(animator);
        }

        public void Save(Animator animator)
        {
            gameObjectTransform = new TransformPoseSave.SaveData(animator.gameObject.transform);

            currentAnimatorStateInfo = new AnimatorStateInfo[animator.layerCount];
            nextAnimatorStateInfo = new AnimatorStateInfo[animator.layerCount];
            for (int i = 0; i < animator.layerCount; i++)
            {
                currentAnimatorStateInfo[i] = animator.GetCurrentAnimatorStateInfo(i);
                nextAnimatorStateInfo[i] = animator.GetNextAnimatorStateInfo(i);
            }

            var parameters = animator.parameters;
            animatorParameters = new SaveParameter[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                animatorParameters[i] = new SaveParameter()
                {
                    nameHash = param.nameHash,
                    type = param.type,
                };
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float: animatorParameters[i].value = animator.GetFloat(param.nameHash); break;
                    case AnimatorControllerParameterType.Int: animatorParameters[i].value = animator.GetInteger(param.nameHash); break;
                    case AnimatorControllerParameterType.Bool: animatorParameters[i].value = animator.GetBool(param.nameHash); break;
                    case AnimatorControllerParameterType.Trigger: break;
                    default: Assert.IsTrue(false); break;
                }
            }
        }

        public void Load(Animator animator)
        {
            if (!animator.isInitialized)
                animator.Rebind();

            if (currentAnimatorStateInfo != null && currentAnimatorStateInfo.Length == animator.layerCount &&
                nextAnimatorStateInfo != null && nextAnimatorStateInfo.Length == animator.layerCount)
            {
                bool changed = false;
                for (int i = 0; i < animator.layerCount; i++)
                {
                    var info = animator.GetCurrentAnimatorStateInfo(i);
                    if (info.fullPathHash != currentAnimatorStateInfo[i].fullPathHash ||
                        info.shortNameHash != currentAnimatorStateInfo[i].shortNameHash ||
                        info.normalizedTime != currentAnimatorStateInfo[i].normalizedTime ||
                        info.length != currentAnimatorStateInfo[i].length)
                    {
                        changed = true;
                        break;
                    }
                    info = animator.GetNextAnimatorStateInfo(i);
                    if (info.fullPathHash != nextAnimatorStateInfo[i].fullPathHash ||
                        info.shortNameHash != nextAnimatorStateInfo[i].shortNameHash ||
                        info.normalizedTime != nextAnimatorStateInfo[i].normalizedTime ||
                        info.length != nextAnimatorStateInfo[i].length)
                    {
                        changed = true;
                        break;
                    }
                }
                if (changed)
                {
                    for (int i = 0; i < animator.layerCount; i++)
                    {
                        animator.Play(currentAnimatorStateInfo[i].fullPathHash, i, currentAnimatorStateInfo[i].normalizedTime);
                    }
                    animator.Update(0f);
                    for (int i = 0; i < animator.layerCount; i++)
                    {
                        if (nextAnimatorStateInfo[i].fullPathHash != 0)
                            animator.CrossFade(nextAnimatorStateInfo[i].fullPathHash, Mathf.Clamp01(1f - currentAnimatorStateInfo[i].normalizedTime), i, nextAnimatorStateInfo[i].normalizedTime);
                    }
                }
                animator.Update(0f);
                gameObjectTransform.LoadLocal(animator.gameObject.transform);
                #region RendererForceUpdate
                if (animator.gameObject != null) //Is there a bug that will not be updated while pausing? Therefore, it forcibly updates it.
                {
                    foreach (var renderer in animator.gameObject.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.enabled)
                            continue;
                        renderer.enabled = !renderer.enabled;
                        renderer.enabled = !renderer.enabled;
                    }
                }
                #endregion
            }

            if (animatorParameters != null)
            {
                var parameterTypeTable = new Dictionary<int, AnimatorControllerParameterType>();
                foreach (var param in animator.parameters)
                    parameterTypeTable.TryAdd(param.nameHash, param.type);
                foreach (var save in animatorParameters)
                {
                    if (save == null || save.value == null)
                        continue;
                    if (!parameterTypeTable.TryGetValue(save.nameHash, out var type) || type != save.type)
                        continue;
                    if (animator.IsParameterControlledByCurve(save.nameHash))
                        continue;
                    switch (save.type)
                    {
                        case AnimatorControllerParameterType.Float: animator.SetFloat(save.nameHash, (float)save.value); break;
                        case AnimatorControllerParameterType.Int: animator.SetInteger(save.nameHash, (int)save.value); break;
                        case AnimatorControllerParameterType.Bool: animator.SetBool(save.nameHash, (bool)save.value); break;
                    }
                }
                animator.Update(0f);
            }
        }
    }
}
