using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "effector_info", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_Effector_Info
    {
        [XmlAttribute("sid")]
        public string sID { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement(ElementName = "bind")]
        public Grendgine_Collada_Bind[] Bind { get; set; }

        [XmlElement(ElementName = "newparam")]
        public Grendgine_Collada_New_Param[] New_Param { get; set; }

        [XmlElement(ElementName = "setparam")]
        public Grendgine_Collada_Set_Param[] Set_Param { get; set; }

        [XmlElement(ElementName = "speed")]
        public Grendgine_Collada_Common_Float2_Or_Param_Type Speed { get; set; }

        [XmlElement(ElementName = "acceleration")]
        public Grendgine_Collada_Common_Float2_Or_Param_Type Acceleration { get; set; }

        [XmlElement(ElementName = "deceleration")]
        public Grendgine_Collada_Common_Float2_Or_Param_Type Deceleration { get; set; }

        [XmlElement(ElementName = "jerk")]
        public Grendgine_Collada_Common_Float2_Or_Param_Type Jerk { get; set; }

    }
}

