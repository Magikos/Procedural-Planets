using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "physics_scene", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Physics_Scene
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }


        [XmlElement(ElementName = "instance_force_field")]
        public Grendgine_Collada_Instance_Force_Field[] Instance_Force_Field { get; set; }

        [XmlElement(ElementName = "instance_physics_model")]
        public Grendgine_Collada_Instance_Physics_Model[] Instance_Physics_Model { get; set; }

        [XmlElement(ElementName = "technique_common")]
        public Grendgine_Collada_Technique_Common_Physics_Scene Technique_Common { get; set; }

        [XmlElement(ElementName = "technique")]
        public Grendgine_Collada_Technique[] Technique { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

