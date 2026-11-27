/// <summary>
/// How much of the wind a body ignores: 0 = carried fully by it, 1 = immune.
/// Implemented by Creature, PlayerCreatureStats and the WindResistance component for loose props.
/// </summary>
public interface IWindResistant
{
    float WindResistance { get; }
}
