using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Surface
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("sid")]
        public string sID { get; set; }


        [XmlElement(ElementName = "cone")]
        public Grendgine_Collada_Cone Cone { get; set; }

        [XmlElement(ElementName = "plane")]
        public Grendgine_Collada_Plane Plane { get; set; }

        [XmlElement(ElementName = "cylinder")]
        public Grendgine_Collada_Cylinder_B_Rep Cylinder { get; set; }

        [XmlElement(ElementName = "nurbs_surface")]
        public Grendgine_Collada_Nurbs_Surface Nurbs_Surface { get; set; }

        [XmlElement(ElementName = "sphere")]
        public Grendgine_Collada_Sphere Sphere { get; set; }

        [XmlElement(ElementName = "torus")]
        public Grendgine_Collada_Torus Torus { get; set; }

        [XmlElement(ElementName = "swept_surface")]
        public Grendgine_Collada_Swept_Surface Swept_Surface { get; set; }



        [XmlElement(ElementName = "orient")]
        public Grendgine_Collada_Orient[] Orient { get; set; }

        [XmlElement(ElementName = "origin")]
        public Grendgine_Collada_Origin Origin { get; set; }

    }
}

