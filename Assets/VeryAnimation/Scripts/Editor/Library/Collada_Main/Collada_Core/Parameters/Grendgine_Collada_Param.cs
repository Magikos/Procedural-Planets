using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Param
    {
        [XmlAttribute("ref")]
        public string Ref { get; set; }

        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("semantic")]
        public string Semantic { get; set; }

        [XmlAttribute("type")]
        public string Type { get; set; }

        [XmlAnyElement]
        public XmlElement[] Data { get; set; }

        //TODO: this is used in a few contexts
    }
}

