using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "render", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Render
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("sid")]
        public string sid { get; set; }

        [XmlAttribute("camera_node")]
        public string Camera_Node { get; set; }

        [XmlElement(ElementName = "layer")]
        public string[] Layer { get; set; }

        [XmlElement(ElementName = "instance_material")]
        public Grendgine_Collada_Instance_Material_Rendering Instance_Material { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

