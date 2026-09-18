using System;

/// <summary>Supply budgets expressed as nutrition per world day. The host supplies reachable, usable sources only.</summary>
public readonly struct ActorHabitatCapacity
{
    public readonly double FoodPerDay, WaterPerDay;
    public ActorHabitatCapacity(double foodPerDay, double waterPerDay)
    {
        if (!double.IsFinite(foodPerDay) || foodPerDay < 0 || !double.IsFinite(waterPerDay) || waterPerDay < 0)
            throw new ArgumentOutOfRangeException(nameof(foodPerDay));
        FoodPerDay = foodPerDay; WaterPerDay = waterPerDay;
    }
    public int SupportedPopulation(double hungerPerDay, double thirstPerDay, double reserve = .25)
    {
        if (!double.IsFinite(hungerPerDay) || hungerPerDay <= 0 || !double.IsFinite(thirstPerDay) || thirstPerDay <= 0 ||
            !double.IsFinite(reserve) || reserve < 0) throw new ArgumentOutOfRangeException(nameof(hungerPerDay));
        double result = Math.Min(FoodPerDay / hungerPerDay, WaterPerDay / thirstPerDay) / (1 + reserve);
        return (int)Math.Min(int.MaxValue, Math.Floor(result));
    }
}
