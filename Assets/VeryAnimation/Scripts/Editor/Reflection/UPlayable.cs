using System.Reflection;
using UnityEngine.Assertions;
using UnityEngine.Playables;

namespace VeryAnimation
{
    internal sealed class UPlayable
    {
        private readonly FieldInfo fi_m_Handle;

        public UPlayable()
        {
            Assert.IsNotNull(fi_m_Handle = typeof(Playable).GetField("m_Handle", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        public Playable Create(PlayableHandle handle)
        {
            object obj = new Playable();
            fi_m_Handle.SetValue(obj, handle);
            return (Playable)obj;
        }
    }
}
