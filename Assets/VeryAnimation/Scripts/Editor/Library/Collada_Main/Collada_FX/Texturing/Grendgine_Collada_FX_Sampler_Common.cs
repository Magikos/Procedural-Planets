using System.Xml;
using System.Xml.Serialization;

namespace VeryAnimation.grendgine_collada
{
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(AnonymousType = true)]
    [System.Xml.Serialization.XmlRootAttribute(ElementName = "fx_sampler_common", Namespace = "http://www.collada.org/2005/11/COLLADASchema", IsNullable = true)]
    public partial class Grendgine_Collada_FX_Sampler_Common
    {
        [XmlElement(ElementName = "instance_image")]
        public Grendgine_Collada_Instance_Image Instance_Image { get; set; }

        [XmlElement(ElementName = "texcoord")]
        public Grendgine_Collada_TexCoord_Semantic TexCoord_Semantic { get; set; }

        [XmlElement(ElementName = "wrap_s")]
        [System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_FX_Sampler_Common_Wrap_Mode.WRAP)]
        public Grendgine_Collada_FX_Sampler_Common_Wrap_Mode Wrap_S { get; set; } = Grendgine_Collada_FX_Sampler_Common_Wrap_Mode.WRAP;

        [XmlElement(ElementName = "wrap_t")]
        [System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_FX_Sampler_Common_Wrap_Mode.WRAP)]
        public Grendgine_Collada_FX_Sampler_Common_Wrap_Mode Wrap_T { get; set; } = Grendgine_Collada_FX_Sampler_Common_Wrap_Mode.WRAP;

        [XmlElement(ElementName = "wrap_p")]
        [System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_FX_Sampler_Common_Wrap_Mode.WRAP)]
        public Grendgine_Collada_FX_Sampler_Common_Wrap_Mode Wrap_P { get; set; } = Grendgine_Collada_FX_Sampler_Common_Wrap_Mode.WRAP;

        [XmlElement(ElementName = "minfilter")]
        [System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_FX_Sampler_Common_Filter_Type.LINEAR)]
        public Grendgine_Collada_FX_Sampler_Common_Filter_Type MinFilter { get; set; } = Grendgine_Collada_FX_Sampler_Common_Filter_Type.LINEAR;

        [XmlElement(ElementName = "magfilter")]
        [System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_FX_Sampler_Common_Filter_Type.LINEAR)]
        public Grendgine_Collada_FX_Sampler_Common_Filter_Type MagFilter { get; set; } = Grendgine_Collada_FX_Sampler_Common_Filter_Type.LINEAR;

        [XmlElement(ElementName = "mipfilter")]
        [System.ComponentModel.DefaultValueAttribute(Grendgine_Collada_FX_Sampler_Common_Filter_Type.LINEAR)]
        public Grendgine_Collada_FX_Sampler_Common_Filter_Type MipFilter { get; set; } = Grendgine_Collada_FX_Sampler_Common_Filter_Type.LINEAR;

        [XmlElement(ElementName = "border_color")]
        public Grendgine_Collada_Float_Array_String Border_Color { get; set; }

        [XmlElement(ElementName = "mip_max_level")]
        [System.ComponentModel.DefaultValueAttribute((byte)0)]
        public byte Mip_Max_Level { get; set; }

        [XmlElement(ElementName = "mip_min_level")]
        [System.ComponentModel.DefaultValueAttribute((byte)0)]
        public byte Mip_Min_Level { get; set; }

        [XmlElement(ElementName = "mip_bias")]
        [System.ComponentModel.DefaultValueAttribute(0.0f)]
        public float Mip_Bias { get; set; }

        [XmlElement(ElementName = "max_anisotropy")]
        [System.ComponentModel.DefaultValueAttribute(1)]
        public int Max_Anisotropy { get; set; } = 1;

        [XmlElement(ElementName = "extra")]
        public Grendgine_Collada_Extra[] Extra { get; set; }
    }
}

//check done