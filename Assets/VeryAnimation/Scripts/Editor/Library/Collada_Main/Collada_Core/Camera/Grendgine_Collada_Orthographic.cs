using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Orthographic
    {

        [XmlElement(ElementName = "xmag")]
        public Grendgine_Collada_SID_Float XMag { get; set; }

        [XmlElement(ElementName = "ymag")]
        public Grendgine_Collada_SID_Float YMag { get; set; }

        [XmlElement(ElementName = "aspect_ratio")]
        public Grendgine_Collada_SID_Float Aspect_Ratio { get; set; }

        [XmlElement(ElementName = "znear")]
        public Grendgine_Collada_SID_Float ZNear { get; set; }

        [XmlElement(ElementName = "zfar")]
        public Grendgine_Collada_SID_Float ZFar { get; set; }

    }
}

