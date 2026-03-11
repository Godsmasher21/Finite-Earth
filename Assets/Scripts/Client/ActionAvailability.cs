public readonly struct ActionAvailability
{
    public readonly FiniteEarthActionType actionType;
    public readonly bool hasSelection;
    public readonly bool isApplicable;
    public readonly bool isAffordable;
    public readonly bool isInteractable;
    public readonly int applicableSelectionCount;
    public readonly int affordableSelectionCount;
    public readonly FiniteEarthResourcePool cost;
    public readonly string reason;

    public ActionAvailability(
        FiniteEarthActionType actionType,
        bool hasSelection,
        bool isApplicable,
        bool isAffordable,
        bool isInteractable,
        int applicableSelectionCount,
        int affordableSelectionCount,
        FiniteEarthResourcePool cost,
        string reason)
    {
        this.actionType = actionType;
        this.hasSelection = hasSelection;
        this.isApplicable = isApplicable;
        this.isAffordable = isAffordable;
        this.isInteractable = isInteractable;
        this.applicableSelectionCount = applicableSelectionCount;
        this.affordableSelectionCount = affordableSelectionCount;
        this.cost = cost;
        this.reason = reason;
    }
}
