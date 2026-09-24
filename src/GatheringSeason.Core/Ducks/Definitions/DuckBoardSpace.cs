using System;
using System.Globalization;

namespace GatheringSeason.Core.Ducks.Definitions
{
    /// <summary>One occupiable and scorable route reward. The separate nest at zero is not a row.</summary>
    public sealed class DuckBoardSpace
    {
        public DuckBoardSpace(
            int space,
            DuckBiome biome,
            int sleep,
            int twigs,
            int feathers,
            string? shelterName)
        {
            if (space < 1 || space > 43) throw new ArgumentOutOfRangeException(nameof(space));
            if (!Enum.IsDefined(typeof(DuckBiome), biome)) throw new ArgumentOutOfRangeException(nameof(biome));
            if (sleep < 0) throw new ArgumentOutOfRangeException(nameof(sleep));
            if (twigs < 0) throw new ArgumentOutOfRangeException(nameof(twigs));
            if (feathers < 0) throw new ArgumentOutOfRangeException(nameof(feathers));
            if (shelterName != null && (shelterName.Length == 0 || shelterName.Trim() != shelterName))
                throw new ArgumentException("A shelter name must be non-empty and have no surrounding whitespace.", nameof(shelterName));
            if (shelterName == null && feathers != 0)
                throw new ArgumentException("Only a shelter may award Feathers.", nameof(feathers));

            Space = space;
            Biome = biome;
            Sleep = sleep;
            Twigs = twigs;
            Feathers = feathers;
            ShelterName = shelterName;
        }

        public string DefinitionId => "space_" + Space.ToString("00", CultureInfo.InvariantCulture);
        public int Space { get; }
        public DuckBiome Biome { get; }
        public int Sleep { get; }
        public int Reward => Sleep;
        public int Stars => Sleep;
        public int Twigs { get; }
        public int Feathers { get; }
        public string? ShelterName { get; }
        public bool IsShelter => ShelterName != null;
    }
}
