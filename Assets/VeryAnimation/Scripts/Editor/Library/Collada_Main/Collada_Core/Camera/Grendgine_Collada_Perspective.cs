using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Perspective
    {
        [XmlElement(ElementName = "xfov")]
        public Grendgine_Collada_SID_Float XFov { get; set; }

        [XmlElement(ElementName = "yfov")]
        public Grendgine_Collada_SID_Float YFov { get; set; }

        [XmlElement(ElementName = "aspect_ratio")]
        public Grendgine_Collada_SID_Float Aspect_Ratio { get; set; }

        [XmlElement(ElementName = "znear")]
        public Grendgine_Collada_SID_Float ZNear { get; set; }

        [XmlElement(ElementName = "zfar")]
        public Grendgine_Collada_SID_Float ZFar { get; set; }
    }
}

