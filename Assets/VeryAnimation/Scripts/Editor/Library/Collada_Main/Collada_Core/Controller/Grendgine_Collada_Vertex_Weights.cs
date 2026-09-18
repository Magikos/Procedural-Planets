using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Vertex_Weights
    {
        [XmlAttribute("count")]
        public uint Count { get; set; }

        [XmlElement(ElementName = "input")]
        public Grendgine_Collada_Input_Shared[] Input { get; set; }

        [XmlElement(ElementName = "vcount")]
        public Grendgine_Collada_Int_Array_String VCount { get; set; }

        [XmlElement(ElementName = "v")]
        public Grendgine_Collada_Int_Array_String V { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done