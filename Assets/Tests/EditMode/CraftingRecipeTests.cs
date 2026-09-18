using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class CraftingRecipeTests
    {
        [Test]
        public void ExchangeConsumesAllCostsAndGrantsResultsAtomically()
        {
            var inventory = new InventoryService();
            inventory.Add("Herb", 3);
            var costs = new[] { new ItemQuantity("Herb", 1), new ItemQuantity("Herb", 2) };
            var results = new[] { new ItemQuantity("Powder", 2) };
            Assert.IsTrue(inventory.TryExchange(costs, results, false));
            Assert.AreEqual(3, inventory.Count("Herb"));
            Assert.IsTrue(inventory.TryExchange(costs, results));
            Assert.AreEqual(0, inventory.Count("Herb"));
            Assert.AreEqual(2, inventory.Count("Powder"));
            Assert.AreEqual(1, inventory.DistinctItems);
            Assert.IsFalse(inventory.TryExchange(costs, results));
            Assert.AreEqual(2, inventory.Count("Powder"));
        }

        [Test]
        public void MissingSecondIngredientDoesNotConsumeFirst()
        {
            var inventory = new InventoryService();
            inventory.Add("Herb", 3);
            Assert.IsFalse(inventory.TryExchange(new[] { new ItemQuantity("Herb", 2), new ItemQuantity("Water", 1) },
                new[] { new ItemQuantity("Soup", 1) }));
            Assert.AreEqual(3, inventory.Count("Herb"));
            Assert.AreEqual(0, inventory.Count("Soup"));
        }

        [Test]
        public void InvalidOrOverflowingResultLeavesInventoryUnchanged()
        {
            var inventory = new InventoryService();
            inventory.Add("Herb", 1);
            inventory.Add("Powder", int.MaxValue);
            var costs = new[] { new ItemQuantity("Herb", 1) };
            foreach (var result in new[] { new ItemQuantity("Powder", 1), new ItemQuantity("", 1), new ItemQuantity("Powder", -1) })
            {
                Assert.IsFalse(inventory.TryExchange(costs, new[] { result }));
                Assert.AreEqual(1, inventory.Count("Herb"));
                Assert.AreEqual(int.MaxValue, inventory.Count("Powder"));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CancellationBeforeCommitDoesNotExchange(bool cancel)
        {
            var definition = ScriptableObject.CreateInstance<ActorInteractionDefinition>();
            var recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
            var clip = new AnimationClip();
            try
            {
                recipe.Costs = new[] { new ItemQuantity("Raw meat", 1) };
                recipe.Results = new[] { new ItemQuantity("Cooked meat", 1) };
                var inventory = new InventoryService(); inventory.Add("Raw meat", 1);
                definition.Action = "Cook";
                definition.Phases = new[] { new ActorInteractionDefinition.Phase { Seconds = 1, Marker = "CraftComplete", MarkerProgress = 1 } };
                definition.Phases[0].Animation.Name = "Cook";
                definition.Phases[0].Animation.Clip = clip;
                var session = new ActorInteractionSession();
                session.Marker += _ => Assert.IsTrue(recipe.TryMake(inventory));
                session.Begin(definition.Snapshot());
                session.Advance(.5f);
                Assert.AreEqual(1, inventory.Count("Raw meat"));
                if (cancel) session.Cancel();
                session.Advance(10);
                Assert.AreEqual(cancel ? 1 : 0, inventory.Count("Raw meat"));
                Assert.AreEqual(cancel ? 0 : 1, inventory.Count("Cooked meat"));
            }
            finally { Object.DestroyImmediate(definition); Object.DestroyImmediate(recipe); Object.DestroyImmediate(clip); }
        }
    }
}


