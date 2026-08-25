/// <summary>
/// The single path actor input takes, whatever produced it — a local device, a deserialized network command,
/// an AI behaviour or a replay log. Everything above this seam consumes <see cref="ActorIntent"/> only, so
/// authority code never reaches for a device.
/// </summary>
// planned: network, AI and replay providers join the local one, docs/design/2026-08-20-magikos-game-architecture.md
public interface IInputProvider
{
    ActorIntent Sample(uint tick);
}
