using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GatheringSeason.Core.Ducks.Runtime
{
    /// <summary>Immutable authoritative ranking after Night 10 conversion.</summary>
    public sealed class DuckFinalResult
    {
        internal DuckFinalResult(IEnumerable<DuckFinalStanding> standings)
        {
            if (standings == null) throw new ArgumentNullException(nameof(standings));
            Standings = new ReadOnlyCollection<DuckFinalStanding>(standings.ToList());
            if (Standings.Count == 0) throw new ArgumentException("A final result needs at least one standing.", nameof(standings));
            WinnerIds = new ReadOnlyCollection<string>(Standings
                .Where(standing => standing.IsWinner)
                .Select(standing => standing.PlayerId)
                .ToList());
        }

        public IReadOnlyList<DuckFinalStanding> Standings { get; }
        public IReadOnlyList<string> WinnerIds { get; }

        internal static DuckFinalResult Create(IEnumerable<DuckPlayerState> players)
        {
            if (players == null) throw new ArgumentNullException(nameof(players));
            var ordered = players
                .OrderByDescending(player => player.TotalTwigs)
                .ThenByDescending(player => player.FrozenSleep)
                .ThenBy(player => player.Id, StringComparer.Ordinal)
                .ToArray();
            if (ordered.Length == 0) throw new InvalidOperationException("A final result needs at least one duck.");

            var winningTwigs = ordered[0].TotalTwigs;
            var winningSleep = ordered[0].FrozenSleep;
            var standings = new List<DuckFinalStanding>(ordered.Length);
            var previousTwigs = -1;
            var previousSleep = -1;
            var rank = 0;
            for (var index = 0; index < ordered.Length; index++)
            {
                var player = ordered[index];
                if (index == 0 || player.TotalTwigs != previousTwigs || player.FrozenSleep != previousSleep)
                    rank = index + 1;
                var dreamTwigs = player.LastNightOutcome?.Day == DuckMatchSettings.StandardDays
                    ? player.LastNightOutcome.DreamTwigs
                    : throw new InvalidOperationException("Every final standing needs a resolved Night 10 outcome.");
                standings.Add(new DuckFinalStanding(
                    player.Id,
                    player.Name,
                    rank,
                    player.TotalTwigs,
                    player.FrozenSleep,
                    dreamTwigs,
                    player.TotalTwigs == winningTwigs && player.FrozenSleep == winningSleep));
                previousTwigs = player.TotalTwigs;
                previousSleep = player.FrozenSleep;
            }
            return new DuckFinalResult(standings);
        }
    }

    /// <summary>One duck's final score and the two values used for ranking.</summary>
    public sealed class DuckFinalStanding
    {
        internal DuckFinalStanding(
            string playerId,
            string playerName,
            int rank,
            int totalTwigs,
            int frozenNightTenSleep,
            int dreamTwigs,
            bool isWinner)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("A standing needs a player ID.", nameof(playerId));
            if (string.IsNullOrWhiteSpace(playerName)) throw new ArgumentException("A standing needs a player name.", nameof(playerName));
            if (rank < 1) throw new ArgumentOutOfRangeException(nameof(rank));
            if (totalTwigs < 0) throw new ArgumentOutOfRangeException(nameof(totalTwigs));
            if (frozenNightTenSleep < 0) throw new ArgumentOutOfRangeException(nameof(frozenNightTenSleep));
            if (dreamTwigs < 0) throw new ArgumentOutOfRangeException(nameof(dreamTwigs));
            PlayerId = playerId;
            PlayerName = playerName;
            Rank = rank;
            TotalTwigs = totalTwigs;
            FrozenNightTenSleep = frozenNightTenSleep;
            DreamTwigs = dreamTwigs;
            IsWinner = isWinner;
        }

        public string PlayerId { get; }
        public string PlayerName { get; }
        public int Rank { get; }
        public int TotalTwigs { get; }
        public int FrozenNightTenSleep { get; }
        public int FrozenNightTenReward => FrozenNightTenSleep;
        public int FrozenNightTenStars => FrozenNightTenSleep;
        public int DreamTwigs { get; }
        public bool IsWinner { get; }
    }
}
