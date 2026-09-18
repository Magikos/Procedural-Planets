using UnityEngine;

namespace VeryAnimation
{
    public class Human : MonoBehaviour
    {
        private static readonly int OpenHash = Animator.StringToHash("Open");

        public GameObject door;

        private Vector3 savePosition;
        private Quaternion saveRotation;

        private void Awake()
        {
            savePosition = transform.localPosition;
            saveRotation = transform.localRotation;
        }

        public void Restart()
        {
            if (!TryGetComponent<Animator>(out _)) return;

            transform.SetLocalPositionAndRotation(savePosition, saveRotation);
            gameObject.SetActive(false);
            gameObject.SetActive(true);
        }

        public void OpenDoor()
        {
            if (door == null) return;
            if (!door.TryGetComponent<Animator>(out var animator)) return;
            animator.SetTrigger(OpenHash);
        }
    }
}