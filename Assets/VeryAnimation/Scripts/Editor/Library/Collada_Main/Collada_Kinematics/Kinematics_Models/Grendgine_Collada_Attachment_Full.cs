using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "attachment_full", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Attachment_Full
    {
        [XmlAttribute("joint")]
        public string Joint { get; set; }

        [XmlElement(ElementName = "translate")]
        public Grendgine_Collada_Translate[] Translate { get; set; }

        [XmlElement(ElementName = "rotate")]
        public Grendgine_Collada_Rotate[] Rotate { get; set; }

        [XmlElement(ElementName = "link")]
        public Grendgine_Collada_Link Link { get; set; }

    }
}

