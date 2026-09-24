using System;

namespace GatheringSeason.Core.Ducks.Definitions
{
    /// <summary>Immutable identity and display data for one World Event card.</summary>
    public sealed class DuckWorldEventDefinition
    {
        public DuckWorldEventDefinition(string definitionId, DuckWorldEventType eventType, string name)
        {
            if (!Enum.IsDefined(typeof(DuckWorldEventType), eventType))
                throw new ArgumentOutOfRangeException(nameof(eventType));

            DefinitionId = DefinitionIdentity.Require(definitionId, nameof(definitionId));
            EventType = eventType;
            Name = DefinitionIdentity.RequireName(name, nameof(name));
        }

        public string DefinitionId { get; }
        public DuckWorldEventType EventType { get; }
        public string Name { get; }
    }
}
