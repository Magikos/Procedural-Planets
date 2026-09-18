using UnityEngine;

namespace VeryAnimation
{
    public class AnimatorParameterToPositionY : MonoBehaviour
    {
        public GameObject sourceObject;
        public string parameterName;

        private void Update()
        {
            if (sourceObject == null) return;
            if (!sourceObject.TryGetComponent<Animator>(out var animator)) return;
            if (!animator.isInitialized) return;
            var value = animator.GetFloat(parameterName);
            var pos = transform.localPosition;
            transform.localPosition = new Vector3(pos.x, value, pos.z);
        }
    }
}
