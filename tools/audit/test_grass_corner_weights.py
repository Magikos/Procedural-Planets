"""Numerical contract for grass property interpolation; no Unity dependency."""
import math
import unittest


def blend(corners, spatial_weights):
    density = numerator = denominator = 0.0
    for slots, spatial_weight in zip(corners, spatial_weights):
        corner_density = 0.0
        for weight, grass_density, power, property_value in slots:
            corner_density += grass_density * weight ** power
            property_weight = weight * grass_density
            numerator += spatial_weight * property_weight * property_value
            denominator += spatial_weight * property_weight
        density += spatial_weight * min(1.0, corner_density)
    return density, numerator / denominator if denominator > 0.0 else 0.0


class GrassCornerWeightsTests(unittest.TestCase):
    def test_empty_corner_reduces_density_without_changing_properties(self):
        for value in (40.0, 5.0, 0.2, 0.8, 0.04):
            packed = blend([[(0.5, 1.0, 1.0, value)]], [1.0])
            spatial = blend([[(1.0, 1.0, 1.0, value)], []], [0.5, 0.5])
            self.assertEqual(packed, spatial)
            self.assertEqual(spatial, (0.5, value))

    def test_unequal_grass_weights_use_weighted_properties(self):
        result = blend([[(1.0, 1.0, 1.0, 40.0)], [(1.0, 0.25, 1.0, 20.0)]], [0.5, 0.5])
        self.assertEqual(result, (0.625, 36.0))

    def test_density_keeps_corner_power_and_saturation(self):
        corners = [[(0.5, 1.0, 2.0, 40.0)], [(0.8, 1.0, 1.0, 40.0)] * 2]
        density, value = blend(corners, [0.5, 0.5])
        self.assertEqual(density, 0.625)
        self.assertAlmostEqual(value, 40.0)

    def test_zero_and_small_spatial_weights_are_finite(self):
        self.assertEqual(blend([[], []], [0.5, 0.5]), (0.0, 0.0))
        density, value = blend([[(1.0, 1.0, 1.0, 40.0)], []], [1e-8, 1.0 - 1e-8])
        self.assertTrue(math.isfinite(value))
        self.assertAlmostEqual(value, 40.0)
        self.assertEqual(density, 1e-8)


if __name__ == "__main__":
    unittest.main()
