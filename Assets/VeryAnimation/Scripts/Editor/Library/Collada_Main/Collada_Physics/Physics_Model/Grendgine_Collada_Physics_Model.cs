using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "physics_model", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Physics_Model
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "rigid_body")]
        public Grendgine_Collada_Rigid_Body[] Rigid_Body { get; set; }

        [XmlElement(ElementName = "rigid_constraint")]
        public Grendgine_Collada_Rigid_Constraint[] Rigid_Constraint { get; set; }

        [XmlElement(ElementName = "instance_physics_model")]
        public Grendgine_Collada_Instance_Physics_Model[] Instance_Physics_Model { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

