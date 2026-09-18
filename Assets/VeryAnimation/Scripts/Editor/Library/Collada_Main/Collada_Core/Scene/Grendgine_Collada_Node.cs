using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Node
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("type")]
        [System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_Node_Type.NODE)]
        public Grendgine_Collada_Node_Type Type { get; set; } = Grendgine_Collada_Node_Type.NODE;

        [XmlAttribute("layer")]
        public string Layer { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "lookat")]
        public Grendgine_Collada_Lookat[] Lookat { get; set; }

        [XmlElement(ElementName = "matrix")]
        public Grendgine_Collada_Matrix[] Matrix { get; set; }

        [XmlElement(ElementName = "rotate")]
        public Grendgine_Collada_Rotate[] Rotate { get; set; }

        [XmlElement(ElementName = "scale")]
        public Grendgine_Collada_Scale[] Scale { get; set; }

        [XmlElement(ElementName = "skew")]
        public Grendgine_Collada_Skew[] Skew { get; set; }

        [XmlElement(ElementName = "translate")]
        public Grendgine_Collada_Translate[] Translate { get; set; }

        [XmlElement(ElementName = "instance_camera")]
        public Grendgine_Collada_Instance_Camera[] Instance_Camera { get; set; }

        [XmlElement(ElementName = "instance_controller")]
        public Grendgine_Collada_Instance_Controller[] Instance_Controller { get; set; }

        [XmlElement(ElementName = "instance_geometry")]
        public Grendgine_Collada_Instance_Geometry[] Instance_Geometry { get; set; }

        [XmlElement(ElementName = "instance_light")]
        public Grendgine_Collada_Instance_Light[] Instance_Light { get; set; }

        [XmlElement(ElementName = "instance_node")]
        public Grendgine_Collada_Instance_Node[] Instance_Node { get; set; }

        [XmlElement(ElementName = "node")]
        public Grendgine_Collada_Node[] node { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }

    }
}

//check done