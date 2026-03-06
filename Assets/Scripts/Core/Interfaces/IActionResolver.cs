public interface IActionResolver
{
    ActionResolution Resolve(ActionIntent intent, WorldState state, PlayerState player, int tick);
}
