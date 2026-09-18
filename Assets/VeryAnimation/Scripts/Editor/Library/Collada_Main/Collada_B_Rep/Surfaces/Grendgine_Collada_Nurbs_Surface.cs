using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Nurbs_Surface
    {
        [XmlAttribute("degree_u")]
        public int Degree_U { get; set; }
        [XmlAttribute("closed_u")]
        public bool Closed_U { get; set; }
        [XmlAttribute("degree_v")]
        public int Degree_V { get; set; }
        [XmlAttribute("closed_v")]
        public bool Closed_V { get; set; }

        [XmlElement(ElementName = "source")]
        public Grendgine_Collada_Source[] Source { get; set; }

        [XmlElement(ElementName = "control_vertices")]
        public Grendgine_Collada_Control_Vertices Control_Vertices { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

