using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "technique", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Effect_Technique_COMMON
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("id")]
        public string id { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "annotate")]
        public Grendgine_Collada_Annotate[] Annotate { get; set; }

        [XmlElement(ElementName = "blinn")]
        public Grendgine_Collada_Blinn Blinn { get; set; }

        [XmlElement(ElementName = "constant")]
        public Grendgine_Collada_Constant Constant { get; set; }

        [XmlElement(ElementName = "lambert")]
        public Grendgine_Collada_Lambert Lambert { get; set; }

        [XmlElement(ElementName = "phong")]
        public Grendgine_Collada_Phong Phong { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done