using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Geometry_Common_Fields
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("count")]
        public int Count { get; set; }

        [XmlAttribute("material")]
        public string Material { get; set; }

        [XmlElement(ElementName = "input")]
        public Grendgine_Collada_Input_Shared[] Input { get; set; }

        [XmlElement(ElementName = "p")]
        public Grendgine_Collada_Int_Array_String P { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }

    }
}

//check done