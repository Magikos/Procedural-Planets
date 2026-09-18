using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_B_Rep
    {


        [XmlElement(ElementName = "curves")]
        public Grendgine_Collada_Curves Curves { get; set; }

        [XmlElement(ElementName = "surface_curves")]
        public Grendgine_Collada_Surface_Curves Surface_Curves { get; set; }

        [XmlElement(ElementName = "surfaces")]
        public Grendgine_Collada_Surfaces Surfaces { get; set; }

        [XmlElement(ElementName = "source")]
        public Grendgine_Collada_Source[] Source { get; set; }

        [XmlElement(ElementName = "vertices")]
        public Grendgine_Collada_Vertices Vertices { get; set; }


        [XmlElement(ElementName = "edges")]
        public Grendgine_Collada_Edges Edges { get; set; }

        [XmlElement(ElementName = "wires")]
        public Grendgine_Collada_Wires Wires { get; set; }

        [XmlElement(ElementName = "faces")]
        public Grendgine_Collada_Faces Faces { get; set; }

        [XmlElement(ElementName = "pcurves")]
        public Grendgine_Collada_PCurves PCurves { get; set; }

        [XmlElement(ElementName = "shells")]
        public Grendgine_Collada_Shells Shells { get; set; }

        [XmlElement(ElementName = "solids")]
        public Grendgine_Collada_Solids Solids { get; set; }




        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }

    }
}

