using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "pass", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Pass
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlElement(ElementName = "annotate")]
        public Grendgine_Collada_Annotate[] Annotate { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }

        [XmlElement(ElementName = "states")]
        public Grendgine_Collada_States States { get; set; }

        [XmlElement(ElementName = "evaluate")]
        public Grendgine_Collada_Effect_Technique_Evaluate Evaluate { get; set; }
    }
}

