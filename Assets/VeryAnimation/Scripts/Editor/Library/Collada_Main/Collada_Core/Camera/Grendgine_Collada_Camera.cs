using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Camera
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "optics")]
        public Grendgine_Collada_Optics Optics { get; set; }

        [XmlElement(ElementName = "imager")]
        public Grendgine_Collada_Imager Imager { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

