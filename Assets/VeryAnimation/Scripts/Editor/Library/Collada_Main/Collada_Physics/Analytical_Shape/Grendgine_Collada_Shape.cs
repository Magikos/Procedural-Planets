using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "shape", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Shape
    {
        [XmlElement(ElementName = "hollow")]
        public Grendgine_Collada_SID_Bool Hollow { get; set; }

        [XmlElement(ElementName = "mass")]
        public Grendgine_Collada_SID_Float Mass { get; set; }

        [XmlElement(ElementName = "density")]
        public Grendgine_Collada_SID_Float Density { get; set; }


        [XmlElement(ElementName = "physics_material")]
        public Grendgine_Collada_Physics_Material Physics_Material { get; set; }

        [XmlElement(ElementName = "instance_physics_material")]
        public Grendgine_Collada_Instance_Physics_Material Instance_Physics_Material { get; set; }


        [XmlElement(ElementName = "instance_geometry")]
        public Grendgine_Collada_Instance_Geometry Instance_Geometry { get; set; }

        [XmlElement(ElementName = "plane")]
        public Grendgine_Collada_Plane Plane { get; set; }
        [XmlElement(ElementName = "box")]
        public Grendgine_Collada_Box Box { get; set; }
        [XmlElement(ElementName = "sphere")]
        public Grendgine_Collada_Sphere Sphere { get; set; }
        [XmlElement(ElementName = "cylinder")]
        public Grendgine_Collada_Cylinder Cylinder { get; set; }
        [XmlElement(ElementName = "capsule")]
        public Grendgine_Collada_Capsule Capsule { get; set; }



        [XmlElement(ElementName = "translate")]
        public Grendgine_Collada_Translate[] Translate { get; set; }

        [XmlElement(ElementName = "rotate")]
        public Grendgine_Collada_Rotate[] Rotate { get; set; }

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

