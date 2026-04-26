public readonly struct ResourceRateSnapshot
{
    public readonly float woodPerMinute;
    public readonly float mineralsPerMinute;
    public readonly float foodPerMinute;
    public readonly float woodModifierPercent;
    public readonly float mineralsModifierPercent;
    public readonly float foodModifierPercent;

    public ResourceRateSnapshot(
        float woodPerMinute,
        float mineralsPerMinute,
        float foodPerMinute,
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
