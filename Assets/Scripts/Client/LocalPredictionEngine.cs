using System.Collections.Generic;

public sealed class LocalPredictionEngine
{
    private readonly Dictionary<string, ActionResolution> pendingByIntentId = new Dictionary<string, ActionResolution>();
    private readonly Queue<string> pendingOrder = new Queue<string>();

    public IReadOnlyDictionary<string, ActionResolution> Pending => pendingByIntentId;

    public ActionResolution Predict(IActionResolver resolver, ActionIntent intent, WorldState worldState, PlayerState playerState, int tick)
    {
        ActionResolution resolution = resolver.Resolve(intent, worldState, playerState, tick);

        if (resolution.accepted)
        {
            pendingByIntentId[intent.intentId] = resolution;
            pendingOrder.Enqueue(intent.intentId);
        }

        return resolution;
    }

    public bool TryAcknowledge(string intentId, ActionResolution authoritativeResolution, out ActionResolution predictedResolution)
    {
        if (!pendingByIntentId.TryGetValue(intentId, out predictedResolution))
        {
            return false;
        }

        pendingByIntentId.Remove(intentId);
        return true;
    }

    public void Clear()
    {
        pendingByIntentId.Clear();
        pendingOrder.Clear();
    }
}
