public readonly struct ResourceRateSnapshot
{
    public readonly int woodPerMinute;
    public readonly int mineralsPerMinute;
    public readonly int foodPerMinute;
    public readonly float woodModifierPercent;
    public readonly float mineralsModifierPercent;
    public readonly float foodModifierPercent;

    public ResourceRateSnapshot(
        int woodPerMinute,
        int mineralsPerMinute,
        int foodPerMinute,
        float woodModifierPercent,
        float mineralsModifierPercent,
        float foodModifierPercent)
    {
        this.woodPerMinute = woodPerMinute;
        this.mineralsPerMinute = mineralsPerMinute;
        this.foodPerMinute = foodPerMinute;
        this.woodModifierPercent = woodModifierPercent;
        this.mineralsModifierPercent = mineralsModifierPercent;
        this.foodModifierPercent = foodModifierPercent;
    }
}
