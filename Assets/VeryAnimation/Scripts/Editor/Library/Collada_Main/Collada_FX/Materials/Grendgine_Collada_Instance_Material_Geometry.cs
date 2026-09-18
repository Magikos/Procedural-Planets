using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "instance_material", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Instance_Material_Geometry
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("target")]
        public string Target { get; set; }

        [XmlAttribute("symbol")]
        public string Symbol { get; set; }

        [XmlElement(ElementName = "bind")]
        public Grendgine_Collada_Bind_FX[] Bind { get; set; }

        [XmlElement(ElementName = "bind_vertex_input")]
        public Grendgine_Collada_Bind_Vertex_Input[] Bind_Vertex_Input { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done