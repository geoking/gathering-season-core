using System;

namespace GatheringSeason.Core.Ducks.Definitions
{
    internal static class DefinitionIdentity
    {
        internal static string Require(string definitionId, string parameterName)
        {
            if (definitionId == null) throw new ArgumentNullException(parameterName);
            if (definitionId.Length == 0 || definitionId.Trim() != definitionId)
                throw new ArgumentException("A definition ID must be non-empty and have no surrounding whitespace.", parameterName);

            return definitionId;
        }

        internal static string RequireName(string name, string parameterName)
        {
            if (name == null) throw new ArgumentNullException(parameterName);
            if (name.Length == 0 || name.Trim() != name)
                throw new ArgumentException("A name must be non-empty and have no surrounding whitespace.", parameterName);

            return name;
        }
    }
}
