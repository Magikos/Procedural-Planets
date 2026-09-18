using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "compiler", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Compiler
    {
        [XmlAttribute("platform")]
        public string Platform { get; set; }

        [XmlAttribute("target")]
        public string Target { get; set; }

        [XmlAttribute("options")]
        public string Options { get; set; }

        [XmlElement(ElementName = "binary")]
        public Grendgine_Collada_Binary Binary { get; set; }
    }
}

