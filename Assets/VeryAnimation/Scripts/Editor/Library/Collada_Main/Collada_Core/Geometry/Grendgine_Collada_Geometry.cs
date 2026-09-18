using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Geometry
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "convex_mesh")]
        public Grendgine_Collada_Convex_Mesh Convex_Mesh { get; set; }

        [XmlElement(ElementName = "mesh")]
        public Grendgine_Collada_Mesh Mesh { get; set; }

        [XmlElement(ElementName = "spline")]
        public Grendgine_Collada_Spline Spline { get; set; }

        [XmlElement(ElementName = "brep")]
        public Grendgine_Collada_B_Rep B_Rep { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done