namespace DeckLauncher.Core;

/// <summary>
/// A source of games (Steam, Epic, GOG and games added by hand; Xbox/Game Pass... later).
/// Adding a store means implementing this interface and registering it in the app.
/// </summary>
public interface IGameProvider
{
    string Name { get; }

    /// <summary>True when the store client/library is present on this machine.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken = default);

    void Launch(Game game);
}
