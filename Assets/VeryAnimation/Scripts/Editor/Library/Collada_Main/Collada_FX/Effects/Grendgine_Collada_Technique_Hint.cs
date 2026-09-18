using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "technique_hint", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Technique_Hint
    {
        [XmlAttribute("platform")]
        public string Platform { get; set; }

        [XmlAttribute("ref")]
        public string Ref { get; set; }

        [XmlAttribute("profile")]
        public string Profile { get; set; }


    }
}

