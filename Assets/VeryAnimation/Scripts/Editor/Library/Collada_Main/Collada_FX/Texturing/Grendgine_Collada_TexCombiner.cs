using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "texcombiner", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_TexCombiner
    {

        [XmlElement(ElementName = "constant")]
        public Grendgine_Collada_Constant_Attribute Constant { get; set; }

        [XmlElement(ElementName = "RGB")]
        public Grendgine_Collada_RGB RGB { get; set; }

        [XmlElement(ElementName = "alpha")]
        public Grendgine_Collada_Alpha Alpha { get; set; }
    }
}

