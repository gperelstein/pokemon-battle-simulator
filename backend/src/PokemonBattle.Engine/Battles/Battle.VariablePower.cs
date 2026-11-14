using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Move-specific power and damage logic, keyed by move id like Showdown's basePowerCallback,
/// onBasePower, damageCallback and onModifyMove (data/moves.ts; Gen 6 inherits these). Moves that
/// depend on items, weather, allies or consecutive use are not supported yet.
/// </summary>
public sealed partial class Battle
{
    /// <summary>Moves without a fixed base power whose logic is implemented.</summary>
    private static readonly HashSet<string> SupportedVariableMoves =
    [
        "lowkick", "grassknot", "heavyslam", "heatcrash", "flail", "reversal", "wringout", "crushgrip",
        "gyroball", "electroball", "punishment", "return", "frustration", "magnitude", "present",
        "superfang", "endeavor", "finalgambit", "psywave", "counter", "mirrorcoat", "metalburst",
    ];

    private static bool IsSupported(MoveDefinition move) =>
        !move.HasVariablePower || move.Ohko || SupportedVariableMoves.Contains(move.Id);

    /// <summary>
    /// Random rolls made before the move is announced (Showdown's onModifyMove): Magnitude's
    /// strength and Present's power or heal.
    /// </summary>
    private (MoveDefinition Move, int? Magnitude, bool Heals) RollMoveVariant(MoveDefinition move)
    {
        switch (move.Id)
        {
            case "magnitude":
                var roll = _random.Next(0, 100);
                var (magnitude, power) = roll switch
                {
                    < 5 => (4, 10),
                    < 15 => (5, 30),
                    < 35 => (6, 50),
                    < 65 => (7, 70),
                    < 85 => (8, 90),
                    < 95 => (9, 110),
                    _ => (10, 150),
                };
                return (move with { BasePower = power }, magnitude, false);

            case "present":
                var present = _random.Next(0, 10);
                return present < 2
                    ? (move, null, true)
                    : (move with { BasePower = present < 6 ? 40 : present < 9 ? 80 : 120 }, null, false);

            default:
                return (move, null, false);
        }
    }

    /// <summary>
    /// The foe a retaliation move answers (Counter, Mirror Coat, Metal Burst) and the damage it
    /// returns, or null when the user was not hit the way the move needs this turn.
    /// </summary>
    private static (HitTaken Hit, int Damage)? Retaliation(MoveDefinition move, BattlePokemon user)
    {
        var category = move.Id switch
        {
            "counter" => MoveCategory.Physical,
            "mirrorcoat" => MoveCategory.Special,
            "metalburst" => (MoveCategory?)null,
            _ => throw new InvalidOperationException($"{move.Id} is not a retaliation move."),
        };

        var hit = user.HitsTakenThisTurn.LastOrDefault(h =>
            h.Side != user.Side && (category is null || h.Category == category));
        if (hit is null)
        {
            return null;
        }

        var damage = move.Id == "metalburst" ? hit.Damage * 3 / 2 : hit.Damage * 2;
        return (hit, Math.Max(1, damage));
    }

    private static bool IsRetaliation(MoveDefinition move) => move.Id is "counter" or "mirrorcoat" or "metalburst";

    /// <summary>
    /// Damage set by the move itself instead of the formula (Showdown's damageCallback), or null.
    /// </summary>
    private int? CallbackDamage(MoveDefinition move, BattlePokemon user, BattlePokemon target, int? retaliation) =>
        move.Id switch
        {
            "superfang" => Math.Max(1, target.CurrentHp / 2),
            "endeavor" => target.CurrentHp - user.CurrentHp,
            "finalgambit" => user.CurrentHp,
            "psywave" => Math.Max(1, _random.Next(50, 151) * user.Set.Level / 100),
            "counter" or "mirrorcoat" or "metalburst" => retaliation,
            _ => null,
        };

    /// <summary>
    /// Base power after the move's own logic: its callback (floored, at least 1), then the
    /// doublings applied as base power modifiers (Facade, Brine, Venoshock).
    /// </summary>
    private int BasePower(MoveDefinition move, BattlePokemon user, BattlePokemon target)
    {
        var power = move.Id switch
        {
            "lowkick" or "grassknot" => target.WeightHg switch
            {
                >= 2000 => 120,
                >= 1000 => 100,
                >= 500 => 80,
                >= 250 => 60,
                >= 100 => 40,
                _ => 20,
            },
            "heavyslam" or "heatcrash" => (user.WeightHg, target.WeightHg) switch
            {
                var (u, t) when u >= t * 5 => 120,
                var (u, t) when u >= t * 4 => 100,
                var (u, t) when u >= t * 3 => 80,
                var (u, t) when u >= t * 2 => 60,
                _ => 40,
            },
            "flail" or "reversal" => Math.Max(user.CurrentHp * 48 / user.Set.Stats.Hp, 1) switch
            {
                < 2 => 200,
                < 5 => 150,
                < 10 => 100,
                < 17 => 80,
                < 33 => 40,
                _ => 20,
            },
            "eruption" or "waterspout" => move.BasePower * user.CurrentHp / user.Set.Stats.Hp,
            "wringout" or "crushgrip" => WringOutPower(target),
            "gyroball" => user.Speed == 0 ? 1 : Math.Min(150, 25 * target.Speed / user.Speed + 1),
            "electroball" => new[] { 40, 60, 80, 120, 150 }[
                target.Speed == 0 ? 0 : Math.Min(user.Speed / target.Speed, 4)],
            "storedpower" => move.BasePower + 20 * user.PositiveStages,
            "punishment" => Math.Min(200, 60 + 20 * target.PositiveStages),
            "return" => Math.Max(1, user.Set.Happiness * 10 / 25),
            "frustration" => Math.Max(1, (255 - user.Set.Happiness) * 10 / 25),
            "hex" when target.Status is not null => move.BasePower * 2,
            "smellingsalts" when target.Status == MajorStatus.Paralysis => move.BasePower * 2,
            "wakeupslap" when target.Status == MajorStatus.Sleep => move.BasePower * 2,
            "revenge" or "avalanche" when user.HitsTakenThisTurn.Any(h => h.Source == target && h.Damage > 0) =>
                move.BasePower * 2,
            "payback" when !target.NewlySwitched && !WillMove(target) => move.BasePower * 2,
            "assurance" when target.HurtThisTurn => move.BasePower * 2,
            _ => move.BasePower,
        };
        power = Math.Max(1, power);

        var doubled = move.Id switch
        {
            "facade" => user.Status is not null and not MajorStatus.Sleep,
            "brine" => target.CurrentHp * 2 <= target.Set.Stats.Hp,
            "venoshock" => target.Status is MajorStatus.Poison or MajorStatus.Toxic,
            _ => false,
        };
        return doubled ? power * 2 : power;
    }

    /// <summary>Showdown's 4096-based Wring Out / Crush Grip formula: up to 120, at least 1.</summary>
    private static int WringOutPower(BattlePokemon target)
    {
        var ratio = (long)target.CurrentHp * 4096 / target.Set.Stats.Hp;
        return (int)Math.Max(1, (120 * (100 * ratio) + 2047) / 4096 / 100);
    }

    private bool WillMove(BattlePokemon pokemon) => _queue.Any(a => a is MoveAction && a.Actor == pokemon);

    /// <summary>Status cured by the move after it hits (Smelling Salts, Wake-Up Slap).</summary>
    private static MajorStatus? CuredOnHit(MoveDefinition move) => move.Id switch
    {
        "smellingsalts" => MajorStatus.Paralysis,
        "wakeupslap" => MajorStatus.Sleep,
        _ => null,
    };
}
