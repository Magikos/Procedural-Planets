using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "image", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Image
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "asset")]
        public Grendgine_Collada_Asset Asset { get; set; }

        [XmlElement(ElementName = "renderable")]
        public Grendgine_Collada_Renderable_Share Renderable_Share { get; set; }

        [XmlElement(ElementName = "init_from")]
        //public Grendgine_Collada_Init_From Init_From;
        public string Init_From { get; set; }    //1.4.1

        [XmlElement(ElementName = "create_2d")]
        public Grendgine_Collada_Create_2D Create_2D { get; set; }

        [XmlElement(ElementName = "create_3d")]
        public Grendgine_Collada_Create_3D Create_3D { get; set; }

        [XmlElement(ElementName = "create_cube")]
        public Grendgine_Collada_Create_Cube Create_Cube { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done