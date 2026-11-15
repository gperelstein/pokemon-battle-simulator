using PokemonBattle.Engine.Moves;
using PokemonBattle.Engine.Pokemon;

namespace PokemonBattle.Engine.Battles;

/// <summary>
/// Held items that act through the battle rather than the damage formula, following Showdown's
/// data/items.ts with the Gen 6 mods: Life Orb's recoil (gen6: none after OHKO moves), Normal Gem
/// (consumed by the first damaging Normal move, x1.3 from data/conditions.ts "gem"), the Metronome
/// item's consecutive-use count, Quick Claw / Lagging Tail / Full Incense turn order
/// (onFractionalPriority, sim/battle-queue.ts) and Iron Ball's grounding (sim/pokemon.ts isGrounded).
/// </summary>
public sealed partial class Battle
{
    /// <summary>The Pokémon whose Gem was used up by the move being executed (Showdown's "gem" volatile).</summary>
    private BattlePokemon? _gemUser;

    /// <summary>
    /// Normal Gem (onSourceTryPrimaryHit): a damaging Normal move about to hit another Pokémon uses
    /// up the gem, which boosts the move against every target it then hits. Fixed-damage and OHKO
    /// moves use it up too, without benefiting from it.
    /// </summary>
    private void TryUseGem(BattlePokemon user, BattlePokemon target, MoveDefinition move)
    {
        if (target == user || move.Category == MoveCategory.Status || move.Type != PokemonType.Normal ||
            user.Item != HeldItems.NormalGem)
        {
            return;
        }

        ItemConsumption(user, HeldItems.NormalGem);
        _gemUser = user;
    }

    /// <summary>
    /// The Metronome item's onTryMove (priority -2), run once the move is past its charging turn:
    /// the same move again after a successful use, or the attacking turn of a two-turn move, adds a
    /// consecutive use; anything else starts over. Holding another item forgets the count.
    /// </summary>
    private static void CountMetronomeUse(BattlePokemon user, MoveDefinition move, bool releasingCharge)
    {
        if (user.Item != HeldItems.Metronome)
        {
            user.MetronomeMove = null;
            user.MetronomeCount = 0;
            return;
        }

        if (user.MetronomeMove == move.Id && user.MoveSucceededLastTurn)
        {
            user.MetronomeCount++;
        }
        else if (releasingCharge)
        {
            user.MetronomeCount = user.MetronomeMove == move.Id ? user.MetronomeCount + 1 : 1;
        }
        else
        {
            user.MetronomeCount = 0;
        }

        user.MetronomeMove = move.Id;
    }

    /// <summary>
    /// Life Orb (onAfterMoveSecondarySelf, gen6): after a damaging move hit something, the holder
    /// loses floor(max HP / 10), at least 1. Not after OHKO moves, not when Sheer Force removed the
    /// move's secondary effects, and never with Magic Guard.
    /// </summary>
    private void ApplyLifeOrbRecoil(BattlePokemon user, MoveDefinition move, bool hitSomething, bool sheerForce)
    {
        if (!hitSomething || user.Item != HeldItems.LifeOrb || move.Category == MoveCategory.Status || move.Ohko ||
            sheerForce || user.IsFainted || IgnoresIndirectDamage(user))
        {
            return;
        }

        HurtByItem(user, HeldItems.LifeOrb, user.Set.Stats.Hp / 10);
    }

    /// <summary>
    /// Fractional priority of a move action inside its priority bracket: Quick Claw rolls 1 in 5 to
    /// move first (announced when it works), Custap Berry is eaten at the pinch threshold to move first,
    /// Lagging Tail and Full Incense move last.
    /// </summary>
    private int FractionalPriority(Side side, int slot, BattlePokemon actor)
    {
        if (CustapActivates(actor))
        {
            return 1;
        }

        if (actor.Item != HeldItems.QuickClaw)
        {
            return HeldItems.FractionalPriority(actor.Item);
        }

        if (_random.Next(0, 5) != 0)
        {
            return 0;
        }

        _events.Add(new ItemActivated(side.Id, slot, actor.Set.Species, HeldItems.QuickClaw));
        return 1;
    }

    /// <summary>The accuracy modifiers of the user's and the target's items (priority -2), faster holder first.</summary>
    private static ModifierChain ChainItemAccuracy(ModifierChain chain, BattlePokemon user, BattlePokemon target, bool targetMoved)
    {
        var userModifier = HeldItems.UserAccuracyModifier(user.Item, targetMoved);
        var targetModifier = HeldItems.TargetAccuracyModifier(target.Item);
        int?[] modifiers = user.Speed > target.Speed ? [userModifier, targetModifier] : [targetModifier, userModifier];
        foreach (var modifier in modifiers.OfType<int>())
        {
            chain = chain.Chain(modifier, 4096);
        }

        return chain;
    }
}
