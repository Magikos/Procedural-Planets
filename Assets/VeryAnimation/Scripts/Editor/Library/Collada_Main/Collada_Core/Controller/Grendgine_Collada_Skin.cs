using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Skin
    {
        [XmlAttribute("source")]
        public string SourceAt { get; set; }

        [XmlElement(ElementName = "bind_shape_matrix")]
        public Grendgine_Collada_Float_Array_String Bind_Shape_Matrix { get; set; }

        [XmlElement(ElementName = "source")]
        public Grendgine_Collada_Source[] Source { get; set; }

        [XmlElement(ElementName = "joints")]
        public Grendgine_Collada_Joints Joints { get; set; }

        [XmlElement(ElementName = "vertex_weights")]
        public Grendgine_Collada_Vertex_Weights Vertex_Weights { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done