# Moon textures

Source: [NASA Scientific Visualization Studio — CGI Moon Kit](https://svs.gsfc.nasa.gov/4720/).
Retrieved 2026-09-07. Credit: NASA's Scientific Visualization Studio.

The color data comes from LROC. The elevation data comes from LOLA.
These files use the kit's 2019 map version. Both maps cover the complete lunar surface.

| Local file | Source file | SHA-256 |
|---|---|---|
| Color.tif | [lroc_color_poles_4k.tif](https://svs.gsfc.nasa.gov/vis/a000000/a004700/a004720/lroc_color_poles_4k.tif) | 918649A7F8ED2F1329B2CD95BB0D25483BEFDCB60AE1A66DB681A637CC21344F |
| Height.tif | [ldem_4_uint.tif](https://svs.gsfc.nasa.gov/vis/a000000/a004700/a004720/ldem_4_uint.tif) | E6668BEC27FC9B8FBB02D198C7DDFB08EEDEEB790167B494F95E6B34201DA05E |

The source files remain unchanged. Unity imports color as sRGB at 4096 × 2048.
Unity converts the 1440 × 720 height map to a normal map with `convertToNormalmap = true` and `heightmapScale = 0.01`.
The height importer preserves non-power-of-two dimensions. Both textures use mipmaps, longitude repeat, and latitude clamp.
The normal conversion provides visual relief only. It does not displace geometry or simulate terrain.

`Moon.mat` uses `Planet/Moon`. Runtime consumers clone this material before changing its properties.
