using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_New_Param
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlElement("annotate")]
        public Grendgine_Collada_Annotate[] Annotate { get; set; }

        [XmlElement(ElementName = "semantic")]
        public string Semantic { get; set; }

        [XmlElement(ElementName = "modifier")]
        public string Modifier { get; set; }

        [XmlElement(ElementName = "sampler2D")]
        public Grendgine_Collada_Sampler2D Sampler2D { get; set; }

        [XmlElement(ElementName = "surface")]
        public Grendgine_Collada_Surface_1_4_1 Surface { get; set; }

        /// <summary>
        /// The element is the type and the element text is the value or space delimited list of values
        /// </summary>
        [XmlAnyElement]
        public XmlElement[] Data { get; set; }
    }
}

//check done