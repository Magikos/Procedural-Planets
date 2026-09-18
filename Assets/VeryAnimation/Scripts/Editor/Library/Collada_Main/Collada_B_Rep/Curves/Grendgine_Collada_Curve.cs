using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Curve
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "line")]
        public Grendgine_Collada_Line Line { get; set; }

        [XmlElement(ElementName = "circle")]
        public Grendgine_Collada_Circle Circle { get; set; }

        [XmlElement(ElementName = "ellipse")]
        public Grendgine_Collada_Ellipse Ellipse { get; set; }

        [XmlElement(ElementName = "parabola")]
        public Grendgine_Collada_Parabola Parabola { get; set; }

        [XmlElement(ElementName = "hyperbola")]
        public Grendgine_Collada_Hyperbola Hyperbola { get; set; }

        [XmlElement(ElementName = "nurbs")]
        public Grendgine_Collada_Nurbs Nurbs { get; set; }


        [XmlElement(ElementName = "orient")]
        public Grendgine_Collada_Orient[] Orient { get; set; }

        [XmlElement(ElementName = "origin")]
        public Grendgine_Collada_Origin Origin { get; set; }
    }
}

