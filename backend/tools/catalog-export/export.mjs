// Exports the Generation 6 catalog from Pokémon Showdown data (via @pkmn, MIT licensed)
// into JSON files consumed by PokemonBattle.Catalog. Run with `npm run export`.
//
// Scope: species are limited to National Dex #1-151 (including their Gen 6 formes and Mega
// Evolutions); moves, abilities, items, natures and the type chart cover all of Generation 6.

import {mkdirSync, writeFileSync} from 'node:fs';
import {dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {Dex} from '@pkmn/dex';
import {Generations} from '@pkmn/data';

const GENERATION = 6;
const MAX_DEX_NUMBER = 151;
const here = dirname(fileURLToPath(import.meta.url));
const outputDir = join(here, '..', '..', 'src', 'PokemonBattle.Catalog', 'Data', `gen${GENERATION}`);

const gen = new Generations(Dex).get(GENERATION);

/** Copies the listed keys that are defined, dropping functions (Showdown event handlers). */
function pick(source, keys) {
  const result = {};
  for (const key of keys) {
    const value = source[key];
    if (value !== undefined && value !== null && typeof value !== 'function') {
      result[key] = value;
    }
  }
  return JSON.parse(JSON.stringify(result));
}

const byNumThenId = (a, b) => a.num - b.num || a.id.localeCompare(b.id);

function exportSpecies() {
  return [...gen.species]
    .filter(s => s.num >= 1 && s.num <= MAX_DEX_NUMBER)
    .sort(byNumThenId)
    .map(s => ({
      ...pick(s, ['id', 'num', 'name', 'baseSpecies', 'forme', 'types', 'baseStats', 'abilities',
        'weightkg', 'heightm', 'gender', 'genderRatio', 'prevo', 'evos', 'requiredItem', 'battleOnly']),
      isMega: Boolean(s.isMega),
    }));
}

/** Moves a species can legally know in Gen 6: any learn source from generation 6 or earlier. */
async function exportLearnsets(species) {
  const learnsets = {};
  for (const s of species.filter(s => !s.isMega)) {
    const learnable = await gen.learnsets.learnable(s.id);
    learnsets[s.id] = Object.entries(learnable ?? {})
      .filter(([, sources]) => sources.some(source => Number(source[0]) <= GENERATION))
      .map(([moveId]) => moveId)
      .filter(moveId => gen.moves.get(moveId)?.exists)
      .sort();
  }
  return learnsets;
}

const MOVE_KEYS = [
  'id', 'num', 'name', 'type', 'category', 'basePower', 'pp', 'priority', 'target', 'critRatio',
  'willCrit', 'selfSwitch', 'forceSwitch', 'drain', 'recoil', 'heal', 'multihit', 'ohko', 'status',
  'volatileStatus', 'sideCondition', 'slotCondition', 'pseudoWeather', 'weather', 'terrain', 'boosts',
  'self', 'selfBoost', 'secondary', 'secondaries', 'ignoreAbility', 'ignoreDefensive', 'ignoreEvasion',
  'ignoreImmunity', 'breaksProtect', 'stallingMove', 'thawsTarget', 'damage', 'defensiveCategory',
  'overrideOffensivePokemon', 'overrideOffensiveStat', 'overrideDefensiveStat', 'sleepUsable',
  'nonGhostTarget', 'hasCrashDamage', 'selfdestruct', 'isFutureMove', 'tracksTarget', 'smartTarget',
  'multiaccuracy', 'struggleRecoil', 'shortDesc',
];

function exportMoves() {
  return [...gen.moves].sort(byNumThenId).map(m => ({
    ...pick(m, MOVE_KEYS),
    // The 17 Hidden Power variants share the id "hiddenpower"; derive a unique id from the name.
    id: m.name.toLowerCase().replace(/[^a-z0-9]/g, ''),
    // Showdown uses `true` for moves that never miss.
    accuracy: m.accuracy === true ? null : m.accuracy,
    flags: Object.keys(m.flags ?? {}).sort(),
  }));
}

function exportAbilities() {
  return [...gen.abilities].sort(byNumThenId).map(a => ({
    ...pick(a, ['id', 'num', 'name', 'shortDesc']),
    flags: Object.keys(a.flags ?? {}).sort(),
  }));
}

function exportItems() {
  return [...gen.items].sort(byNumThenId).map(i => pick(i, [
    'id', 'num', 'name', 'shortDesc', 'megaStone', 'itemUser', 'fling', 'naturalGift', 'isBerry',
    'isGem', 'isChoice', 'isPokeball', 'onPlate', 'onDrive', 'ignoreKlutz',
  ]));
}

function exportNatures() {
  return [...gen.natures]
    .sort((a, b) => a.id.localeCompare(b.id))
    .map(n => pick(n, ['id', 'name', 'plus', 'minus']));
}

/** Converts Showdown's damageTaken codes (0 normal, 1 weak, 2 resist, 3 immune) to multipliers. */
function exportTypeChart() {
  const multiplier = {0: 1, 1: 2, 2: 0.5, 3: 0};
  const types = [...gen.types].map(t => t.name).sort();
  const chart = {};
  for (const defending of types) {
    const damageTaken = gen.types.get(defending).damageTaken;
    chart[defending] = Object.fromEntries(types.map(attacking => [attacking, multiplier[damageTaken[attacking]]]));
  }
  return {types, damageTaken: chart};
}

function write(name, data) {
  writeFileSync(join(outputDir, `${name}.json`), JSON.stringify(data, null, 2) + '\n');
  const count = Array.isArray(data) ? data.length : Object.keys(data).length;
  console.log(`${name}.json: ${count} entries`);
}

mkdirSync(outputDir, {recursive: true});
const species = exportSpecies();
write('species', species);
write('learnsets', await exportLearnsets(species));
write('moves', exportMoves());
write('abilities', exportAbilities());
write('items', exportItems());
write('natures', exportNatures());
write('typechart', exportTypeChart());
