using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    public partial class Grendgine_Collada_Sampler
    {
        [XmlAttribute("id")]
        public string ID { get; set; }

        /*1.5	
		[XmlAttribute("pre_behavior")]
		[System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_Sampler_Behavior.UNDEFINED)]
		public Grendgine_Collada_Sampler_Behavior Pre_Behavior { get; set; }

		[XmlAttribute("post_behavior")]
		[System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_Sampler_Behavior.UNDEFINED)]
		public Grendgine_Collada_Sampler_Behavior Post_Behavior { get; set; }
		*/

        [XmlElement(ElementName = "input")]
        public Grendgine_Collada_Input_Unshared[] Input { get; set; }
    }
}

//check done