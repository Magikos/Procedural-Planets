using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "texenv", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_TexEnv
    {
        [XmlAttribute("operator")]
        public Grendgine_Collada_TexEnv_Operator Operator { get; set; }

        [XmlAttribute("sampler")]
        public string Sampler { get; set; }

        [XmlElement(ElementName = "constant")]
        public Grendgine_Collada_Constant_Attribute Constant { get; set; }
    }
}

