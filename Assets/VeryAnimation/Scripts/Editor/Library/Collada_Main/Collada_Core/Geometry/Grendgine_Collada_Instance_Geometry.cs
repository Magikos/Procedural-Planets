using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Instance_Geometry
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("url")]
        public string URL { get; set; }


        [XmlElement(ElementName = "bind_material")]
        public Grendgine_Collada_Bind_Material[] Bind_Material { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done