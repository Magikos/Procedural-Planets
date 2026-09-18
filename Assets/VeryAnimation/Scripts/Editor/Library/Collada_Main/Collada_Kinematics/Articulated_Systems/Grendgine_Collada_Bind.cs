using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "bind", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Bind
    {
        [XmlAttribute("symbol")]
        public string Symbol { get; set; }

        [XmlElement(ElementName = "param")]
        public Grendgine_Collada_Param Param { get; set; }

        [XmlElement(ElementName = "float")]
        public float Float { get; set; }

        [XmlElement(ElementName = "int")]
        public int Int { get; set; }

        [XmlElement(ElementName = "bool")]
        public bool Bool { get; set; }

        [XmlElement(ElementName = "SIDREF")]
        public string SIDREF { get; set; }

    }
}

