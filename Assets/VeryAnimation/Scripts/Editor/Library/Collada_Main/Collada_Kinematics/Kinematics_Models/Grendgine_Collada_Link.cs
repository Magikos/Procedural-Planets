using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "link", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Link
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "translate")]
        public Grendgine_Collada_Translate[] Translate { get; set; }

        [XmlElement(ElementName = "rotate")]
        public Grendgine_Collada_Rotate[] Rotate { get; set; }

        [XmlElement(ElementName = "attachment_full")]
        public Grendgine_Collada_Attachment_Full Attachment_Full { get; set; }

        [XmlElement(ElementName = "attachment_end")]
        public Grendgine_Collada_Attachment_End Attachment_End { get; set; }

        [XmlElement(ElementName = "attachment_start")]
        public Grendgine_Collada_Attachment_Start Attachment_Start { get; set; }
    }
}

