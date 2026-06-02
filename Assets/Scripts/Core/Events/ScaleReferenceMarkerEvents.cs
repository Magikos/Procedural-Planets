/// <summary>
/// Scale reference marker events — fired from <see cref="DebugCommandProvider"/> in response
/// to F-key / letter-key requests; consumed by <see cref="ScaleReferenceMarkers"/> running
/// next to the active planet. Three discrete events instead of one with an enum to keep the
/// subscriber side trivial.
/// </summary>
public readonly struct DebugDropScaleMarkersRequestedEvent : IGameEvent { }
public readonly struct DebugClearScaleMarkersRequestedEvent : IGameEvent { }
public readonly struct DebugTeleportToScaleMarkersRequestedEvent : IGameEvent { }
