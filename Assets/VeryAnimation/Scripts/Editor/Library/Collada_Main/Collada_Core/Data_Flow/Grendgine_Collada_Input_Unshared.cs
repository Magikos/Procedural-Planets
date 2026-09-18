using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Input_Unshared
    {
        [XmlAttribute("semantic")]
        public Grendgine_Collada_Input_Semantic Semantic { get; set; }

        [XmlAttribute("source")]
        public string source { get; set; }

    }
}

//check done