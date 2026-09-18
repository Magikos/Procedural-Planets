using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Spline
    {
        [XmlAttribute("closed")]
        public bool Closed { get; set; }


        [XmlElement(ElementName = "source")]
        public Grendgine_Collada_Source[] Source { get; set; }

        [XmlElement(ElementName = "control_vertices")]
        public Grendgine_Collada_Control_Vertices Control_Vertices { get; set; }


        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

