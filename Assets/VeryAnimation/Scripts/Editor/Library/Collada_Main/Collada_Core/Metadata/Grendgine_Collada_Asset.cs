using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Asset
    {
        [XmlElement(ElementName = "contributor")]
        public Grendgine_Collada_Asset_Contributor[] Contributor { get; set; }

        [XmlElement(ElementName = "coverage")]
        public Grendgine_Collada_Asset_Coverage Coverage { get; set; }

        [XmlElement(ElementName = "created")]
        public System.DateTime Created { get; set; }

        [XmlElement(ElementName = "keywords")]
        public string Keywords { get; set; }

        [XmlElement(ElementName = "modified")]
        public System.DateTime Modified { get; set; }

        [XmlElement(ElementName = "revision")]
        public string Revision { get; set; }

        [XmlElement(ElementName = "subject")]
        public string Subject { get; set; }

        [XmlElement(ElementName = "title")]
        public string Title { get; set; }

        [XmlElement(ElementName = "unit")]
        public Grendgine_Collada_Asset_Unit Unit { get; set; }

        [XmlElement(ElementName = "up_axis")]
        [System.ComponentModel.DefaultValueAttribute("Y_UP")]
        public string Up_Axis { get; set; } = "Y_UP";

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done