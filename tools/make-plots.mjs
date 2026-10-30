// Builds HousingStudio/Data/plots.json: every housing district's 60 plots (number, size, where the
// plot's centre is in the ward, and which way it faces), for turning a garden's plot-local
// coordinates (as MakePlace and Housing Studio write them) into the ward's own.
//
// The numbers come from Housing Studio's own extraction of the game's ward layouts
// (public/housing/exteriors.json in koko-puffs/kokos-tools, made by scripts/housing-extract).
// `turn` is the plot's yaw in radians about +y, the way the game turns things; MakePlace's own
// table (Plots.cs) holds the same plots with the opposite sign.
//
// Usage: node tools/make-plots.mjs <path to exteriors.json>
import { readFileSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const source = process.argv[2]
if (!source) {
  console.error('Usage: node tools/make-plots.mjs <path to exteriors.json>')
  process.exit(1)
}

// The ward's TerritoryType name (its Bg's last part), by district.
const WARD = {
  mist: 's1h1',
  'lavender-beds': 'f1h1',
  goblet: 'w1h1',
  shirogane: 'e1h1',
  empyreum: 'r1h1',
}

const data = JSON.parse(readFileSync(source, 'utf8'))
const out = { made: data.made, districts: {} }
for (const [key, district] of Object.entries(data.districts)) {
  if (!WARD[key]) throw new Error(`Unknown district ${key}`)
  out.districts[key] = {
    ward: WARD[key],
    plots: district.plots
      .map((p) => ({ n: p.n, size: p.size, x: p.at[0], y: p.at[1], z: p.at[2], turn: p.turn }))
      .sort((a, b) => a.n - b.n),
  }
}

const target = join(here, '..', 'HousingStudio', 'Data', 'plots.json')
writeFileSync(target, JSON.stringify(out, null, 1) + '\n')
console.log(`Wrote ${target}`)
