namespace PokemonBattle.Engine.Battles;

public enum BattleFormat
{
    Singles,
    Doubles,
}

public enum SideId
{
    P1,
    P2,
}

public static class BattleFormatExtensions
{
    public static int ActiveSlots(this BattleFormat format) => format switch
    {
        BattleFormat.Singles => 1,
        BattleFormat.Doubles => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static SideId Opponent(this SideId side) => side == SideId.P1 ? SideId.P2 : SideId.P1;
}
