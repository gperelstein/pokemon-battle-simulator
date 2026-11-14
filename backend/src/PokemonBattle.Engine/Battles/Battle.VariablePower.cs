using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Move-specific power and damage logic, keyed by move id like Showdown's basePowerCallback,
/// onBasePower, damageCallback, onModifyMove and heal effects (data/moves.ts; Gen 6 inherits these). Moves that
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

    /// <summary>Moves whose type Aerilate leaves alone (Showdown's noModifyType list, Gen 6 entries).</summary>
    private static readonly HashSet<string> TypeLockedMoves = ["judgment", "naturalgift", "technoblast", "weatherball"];

    /// <summary>
    /// Changes made to the move before it is announced: the move's own (Magnitude, Present, weather
    /// moves), then the user's ability (Aerilate turns Normal moves into Flying ones and marks them for
    /// its power boost; onModifyTypePriority -1 runs after the move's own type change).
    /// </summary>
    private (MoveDefinition Move, int? Magnitude, bool Heals, bool TypeBoosted) ModifyMove(
        MoveDefinition move, BattlePokemon user)
    {
        var (modified, magnitude, heals) = ModifyMoveItself(move);
        if (user.Ability == "aerilate" && modified.Type == PokemonType.Normal && !TypeLockedMoves.Contains(move.Id))
        {
            return (modified with { Type = PokemonType.Flying }, magnitude, heals, true);
        }

        return (modified, magnitude, heals, false);
    }

    /// <summary>
    /// The move's own changes (Showdown's onModifyType/onModifyMove): Magnitude's strength, Present's
    /// power or heal, and the weather-dependent moves.
    /// </summary>
    private (MoveDefinition Move, int? Magnitude, bool Heals) ModifyMoveItself(MoveDefinition move)
    {
        switch (move.Id)
        {
            case "weatherball" when EffectiveWeather is { } weather:
                var type = weather switch
                {
                    Battles.Weather.Sun => PokemonType.Fire,
                    Battles.Weather.Rain => PokemonType.Water,
                    Battles.Weather.Sandstorm => PokemonType.Rock,
                    _ => PokemonType.Ice,
                };
                return (move with { Type = type, BasePower = move.BasePower * 2 }, null, false);

            // Null accuracy never misses.
            case "thunder" or "hurricane" when EffectiveWeather == Battles.Weather.Rain:
            case "blizzard" when EffectiveWeather == Battles.Weather.Hail:
                return (move with { Accuracy = null }, null, false);

            case "thunder" or "hurricane" when EffectiveWeather == Battles.Weather.Sun:
                return (move with { Accuracy = 50 }, null, false);

            case "growth" when EffectiveWeather == Battles.Weather.Sun:
                return (move with { Boosts = [new(BoostStat.Attack, 2), new(BoostStat.SpecialAttack, 2)] }, null, false);

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
    /// Base power as Showdown computes it: the move's callback (floored, at least 1), then one
    /// BasePower chain in handler priority order: the user's ability (Aerilate, Iron Fist, Reckless
    /// 23; Analytic, Tough Claws, Sand Force 21; Technician, Mega Launcher 19), then the move's own
    /// modifiers (Facade, Brine, Venoshock, Solar Beam) and Bounce's condition (0).
    /// </summary>
    /// <param name="typeBoosted">Aerilate changed the move's type, so it gets its power boost.</param>
    private int BasePower(MoveDefinition move, BattlePokemon user, BattlePokemon target, bool typeBoosted = false)
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

        // BasePower event, highest priority first, as one chain: the user's ability (23, 21, 19)...
        var chain = new ModifierChain();
        var boostedByAbility = user.Ability switch
        {
            "aerilate" => typeBoosted,
            "ironfist" => move.Flags.HasFlag(MoveFlags.Punch),
            "reckless" => move.HasRecoil,
            "analytic" => IsLastToMove(user),
            "toughclaws" => move.Flags.HasFlag(MoveFlags.Contact),
            "sheerforce" => move.HasAnySecondary,
            "sandforce" => EffectiveWeather == Battles.Weather.Sandstorm &&
                move.Type is PokemonType.Rock or PokemonType.Ground or PokemonType.Steel,
            _ => false,
        };
        if (boostedByAbility)
        {
            chain = user.Ability is "ironfist" or "reckless" ? chain.Chain(4915, 4096) : chain.Chain(5325, 4096);
        }

        // Technician (19) checks the power with the modifiers chained so far (Gen 6 priority).
        if ((user.Ability == "technician" && chain.Apply(power) <= 60) ||
            (user.Ability == "megalauncher" && move.Flags.HasFlag(MoveFlags.Pulse)))
        {
            chain = chain.Chain(3, 2);
        }

        // The target's Dry Skin takes more from Fire moves (onSourceBasePower, priority 17).
        if (AbilityOf(target) == "dryskin" && move.Type == PokemonType.Fire)
        {
            chain = chain.Chain(5120, 4096);
        }

        // ...then the move's own modifiers and the target's conditions (priority 0).
        var moveModifier = move.Id switch
        {
            "facade" when user.Status is not null and not MajorStatus.Sleep => (2, 1),
            "brine" when target.CurrentHp * 2 <= target.Set.Stats.Hp => (2, 1),
            "venoshock" when target.Status is MajorStatus.Poison or MajorStatus.Toxic => (2, 1),
            // Solar Beam is weakened by any weather but sun.
            "solarbeam" when EffectiveWeather is { } weather && weather != Battles.Weather.Sun => (1, 2),
            // Bounce doubles the base power of Gust and Twister (onSourceBasePower).
            "gust" or "twister" when target.ChargingMoveId == "bounce" => (2, 1),
            _ => (1, 1),
        };
        chain = chain.Chain(moveModifier.Item1, moveModifier.Item2);
        return Math.Max(1, chain.Apply(power));
    }

    /// <summary>No other active Pokémon still has an action queued this turn (Analytic).</summary>
    private bool IsLastToMove(BattlePokemon user) =>
        !_sides.Values
            .SelectMany(side => side.LiveSlots.Select(slot => side.Active[slot]!))
            .Any(pokemon => pokemon != user && WillMove(pokemon));

    /// <summary>
    /// Final damage modifiers from the battle rather than the two Pokémon's abilities: moves that
    /// reach a semi-invulnerable target and an ally's Friend Guard (x0.75).
    /// </summary>
    private ModifierChain BattleDamageModifiers(BattlePokemon target, MoveDefinition move)
    {
        var chain = new ModifierChain().Chain(SemiInvulnerableDamageMultiplier(target, move));
        var side = _sides[target.Side];
        var friendGuard = side.LiveSlots
            .Select(slot => side.Active[slot]!)
            .Any(ally => ally != target && AbilityOf(ally) == "friendguard");
        return friendGuard ? chain.Chain(3, 4) : chain;
    }

    /// <summary>
    /// HP restored by a healing move: a rounded fraction of max HP, or for Moonlight, Synthesis and
    /// Morning Sun Showdown's modify(maxHp, 0.5 | 0.667 in sun | 0.25 in other weather).
    /// </summary>
    private int? HealAmount(MoveDefinition move, BattlePokemon target)
    {
        var maxHp = target.Set.Stats.Hp;
        if (move.Id is "moonlight" or "synthesis" or "morningsun")
        {
            var modifier = EffectiveWeather switch
            {
                null => 2048,
                Battles.Weather.Sun => 2732,
                _ => 1024,
            };
            return (int)(((long)maxHp * modifier + 2047) / 4096);
        }

        return move.Heal is { } heal
            ? (2 * maxHp * heal.Numerator + heal.Denominator) / (2 * heal.Denominator)
            : null;
    }

    /// <summary>Showdown's 4096-based Wring Out / Crush Grip formula: up to 120, at least 1.</summary>
    private static int WringOutPower(BattlePokemon target)
    {
        var ratio = (long)target.CurrentHp * 4096 / target.Set.Stats.Hp;
        return (int)Math.Max(1, (120 * (100 * ratio) + 2047) / 4096 / 100);
    }

    private bool WillMove(BattlePokemon pokemon) => _queue.Any(a => a is MoveAction or RechargeAction && a.Actor == pokemon);

    /// <summary>Status cured by the move after it hits (Smelling Salts, Wake-Up Slap).</summary>
    private static MajorStatus? CuredOnHit(MoveDefinition move) => move.Id switch
    {
        "smellingsalts" => MajorStatus.Paralysis,
        "wakeupslap" => MajorStatus.Sleep,
        _ => null,
    };
}
