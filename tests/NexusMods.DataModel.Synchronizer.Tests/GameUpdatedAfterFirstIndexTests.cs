using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.TestFramework;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;
using NexusMods.StandardGameLocators.TestHelpers;
using Xunit.Abstractions;

namespace NexusMods.DataModel.Synchronizer.Tests;

/// <summary>
/// The store updates the game after it was first indexed, to another version the file hashes database doesn't know
/// (it stopped being updated in September 2025, so that is every update from now on).
/// </summary>
public class GameUpdatedAfterFirstIndexTests : ACyberpunkIsolatedGameTest<GameUpdatedAfterFirstIndexTests>
{
    private static readonly string[] OriginalFiles =
    [
        "bin/x64/game.dll",
        "engine/config/base.ini",
        "archive/pc/content/basegame_1.archive",
        "r6/config/settings.json",
    ];

    private const string ChangedByUpdate = "bin/x64/game.dll";
    private const string RemovedByUpdate = "r6/config/settings.json";
    private const string AddedByUpdate = "archive/pc/content/basegame_2.archive";

    private readonly UniversalStubbedGameLocator<Cyberpunk2077Game> _locator;

    public GameUpdatedAfterFirstIndexTests(ITestOutputHelper helper) : base(helper)
    {
        _locator = ServiceProvider.GetServices<IGameLocator>().OfType<UniversalStubbedGameLocator<Cyberpunk2077Game>>().Single();
        // Set before the installation is located, so the loadout is created with a version the stubbed database doesn't know
        _locator.LocatorIds = [LocatorId.From("UnknownVersionA")];
    }

    private AbsolutePath PathOf(string relativePath) => GameInstallation.Locations[LocationId.Game].Path.Combine(relativePath);

    /// <summary>
    /// Creates a loadout on the game as first indexed, then updates the game on disk (a file changed, one removed, one
    /// added) and makes the store report another version.
    /// </summary>
    private async Task<Loadout.ReadOnly> CreateLoadoutThenUpdateTheGame()
    {
        foreach (var file in OriginalFiles)
        {
            PathOf(file).Parent.CreateDirectory();
            await PathOf(file).WriteAllTextAsync($"original {file}");
        }

        var loadout = await CreateLoadout();

        await PathOf(ChangedByUpdate).WriteAllTextAsync("updated game.dll");
        PathOf(RemovedByUpdate).Delete();
        await PathOf(AddedByUpdate).WriteAllTextAsync("added by the update");
        _locator.LocatorIds = [LocatorId.From("UnknownVersionB")];

        return loadout;
    }

    [Fact]
    public async Task SynchronizeKeepsTheUpdatedGame()
    {
        var loadout = await CreateLoadoutThenUpdateTheGame();

        await Synchronizer.Synchronize(loadout.Rebase());

        (await PathOf(ChangedByUpdate).ReadAllTextAsync()).Should().Be("updated game.dll");
        (await PathOf(AddedByUpdate).ReadAllTextAsync()).Should().Be("added by the update");
        PathOf(RemovedByUpdate).FileExists.Should().BeFalse("the update removed it");
    }

    [Fact]
    public async Task ResetToOriginalGameStateKeepsTheFilesTheUpdateAdded()
    {
        var loadout = await CreateLoadoutThenUpdateTheGame();
        await Synchronizer.Synchronize(loadout.Rebase());

        await Synchronizer.ResetToOriginalGameState(GameInstallation, GameInstallation.LocatorResult.LocatorIds.ToArray());

        (await PathOf(ChangedByUpdate).ReadAllTextAsync()).Should().Be("updated game.dll");
        PathOf(AddedByUpdate).FileExists.Should().BeTrue("it is a file of the updated game, not a mod file: deleting it breaks the game");
    }
}
