#if VERYANIMATION_TIMELINE
using UnityEngine;

namespace VeryAnimation
{
    [ExecuteAlways]
    internal class VAAnimationTrackObject : MonoBehaviour
    {
        public static VAAnimationTrackObject Instance { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            VAAnimationTrack.SetAllSettings();
        }

        void Update()
        {
            Instance = null;
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }
    }
}
#endif