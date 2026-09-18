using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Mesh
    {


        [XmlElement(ElementName = "source")]
        public Grendgine_Collada_Source[] Source { get; set; }

        [XmlElement(ElementName = "vertices")]
        public Grendgine_Collada_Vertices Vertices { get; set; }

        [XmlElement(ElementName = "lines")]
        public Grendgine_Collada_Lines[] Lines { get; set; }

        [XmlElement(ElementName = "linestrips")]
        public Grendgine_Collada_Linestrips[] Linestrips { get; set; }

        [XmlElement(ElementName = "polygons")]
        public Grendgine_Collada_Polygons[] Polygons { get; set; }

        [XmlElement(ElementName = "polylist")]
        public Grendgine_Collada_Polylist[] Polylist { get; set; }

        [XmlElement(ElementName = "triangles")]
        public Grendgine_Collada_Triangles[] Triangles { get; set; }

        [XmlElement(ElementName = "trifans")]
        public Grendgine_Collada_Trifans[] Trifans { get; set; }

        [XmlElement(ElementName = "tristrips")]
        public Grendgine_Collada_Tristrips[] Tristrips { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done