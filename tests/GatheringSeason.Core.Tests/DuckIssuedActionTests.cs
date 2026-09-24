using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;
using Xunit;

namespace GatheringSeason.Core.Tests;

public sealed class DuckIssuedActionTests
{
    [Fact]
    public void Issued_action_is_bound_to_its_player_and_match_without_consuming_valid_authorization()
    {
        var match = MatchSession.CreateDuck(seed: 42);
        var other = MatchSession.CreateDuck(seed: 42);
        var humanExplore = Explore(match, "human");

        Assert.Throws<InvalidOperationException>(() => match.Execute("ai", humanExplore));
        Assert.Throws<InvalidOperationException>(() => other.Execute("human", humanExplore));

        match.Execute("human", humanExplore);
        Assert.Single(Player(match, "human").PlacedChips);
        Assert.Empty(Player(match, "ai").PlacedChips);
    }

    [Fact]
    public void Opponent_action_keeps_an_independent_command_valid_but_replay_is_rejected()
    {
        var match = MatchSession.CreateDuck(seed: 42);
        var humanExplore = Explore(match, "human");
        var aiExplore = Explore(match, "ai");

        match.Execute("ai", aiExplore);
        match.Execute("human", humanExplore);

        Assert.Single(Player(match, "human").PlacedChips);
        Assert.Single(Player(match, "ai").PlacedChips);
        Assert.Throws<InvalidOperationException>(() => match.Execute("human", humanExplore));
        Assert.Contains(match.GetLegalActions("human"), action => action.Kind == GameActionKind.Explore);
    }

    private static GameAction Explore(MatchSession<DuckMatchView> match, string playerId) =>
        match.GetLegalActions(playerId).Single(action => action.Kind == GameActionKind.Explore);

    private static DuckPlayerView Player(MatchSession<DuckMatchView> match, string playerId) =>
        match.GetSnapshot(playerId).Players.Single(player => player.Id == playerId);
}
