using System;

namespace GatheringSeason.Core.Ducks.Runtime
{
    internal static class DuckPlayerSeats
    {
        private static readonly string[] Ids = { "human", "ai", "ai-2", "ai-3" };
        private static readonly string[] Names = { "Human", "AI", "AI 2", "AI 3" };

        internal static string Id(int seat) => seat >= 0 && seat < Ids.Length
            ? Ids[seat] : throw new ArgumentOutOfRangeException(nameof(seat));

        internal static string Name(int seat) => seat >= 0 && seat < Names.Length
            ? Names[seat] : throw new ArgumentOutOfRangeException(nameof(seat));
    }
}
