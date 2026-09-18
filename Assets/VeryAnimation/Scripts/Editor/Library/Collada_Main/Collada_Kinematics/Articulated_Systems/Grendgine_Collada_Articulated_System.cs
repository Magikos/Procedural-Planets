using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "articulated_system", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Articulated_System
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }


        [XmlElement(ElementName = "kinematics")]
        public Grendgine_Collada_Kinematics Kinematics { get; set; }

        [XmlElement(ElementName = "motion")]
        public Grendgine_Collada_Motion Motion { get; set; }


        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

