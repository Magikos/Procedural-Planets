using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Morph
    {

        [XmlAttribute("source")]
        public string Source_Attribute { get; set; }

        [XmlAttribute("method")]
        public string Method { get; set; }

        [XmlArray("targets")]
        public Grendgine_Collada_Input_Shared[] Targets { get; set; }

        [XmlElement(ElementName = "source")]
        public Grendgine_Collada_Source[] Source { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

