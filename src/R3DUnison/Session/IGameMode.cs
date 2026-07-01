namespace R3DUnison.Session
{
    /// <summary>
    /// A multiplayer mode running inside a room. v1: GhostRaceMode (everyone plays the
    /// same level locally, peers see live stats + ghost planets). v2: CoopMode built on
    /// the game's official co-op plumbing. Session/room code stays mode-agnostic.
    /// </summary>
    public interface IGameMode
    {
        string Name { get; }
        void OnLevelStart();
        void OnPeerEvent(ulong peerId, Protocol.PlayerEvent evt);
        void OnLevelEnd();
    }
}
