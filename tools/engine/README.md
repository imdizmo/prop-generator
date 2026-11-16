# tools/engine: easy-props as an engine

Builds easy-props' own TypeScript into one script, `engine.js`, that the app runs in process with ClearScript V8
(`src/PropGenerator.Engine`).

## Build

```sh
tools/engine/build.sh v0.4.2            # clone or fetch the tag, install, bundle, copy thumbnails, write the pin
tools/engine/build.sh v0.4.2 --smoke    # the same, then the smoke check
```

What it does:

1. Clones easy-props at the tag into `~/.cache/prop-generator/easy-props-<tag>`: from a local reference checkout
   (`EASY_PROPS_DIR`, default `~/.cache/easy-props`) when there is one with the tag (no network), else from GitHub
   (`EASY_PROPS_UPSTREAM`, default `https://github.com/AHS12/easy-props`); set `EASY_PROPS_REPO` to force a source.
   A second run fetches the tag into the existing clone instead. The reference checkout is only read: nothing runs,
   installs or fetches there.
2. Runs `pnpm install --frozen-lockfile` in the clone's `frontend/`.
3. Runs `build.mjs`, which bundles `entry.ts` plus their `frontend/src` with their own esbuild (the one their vite
   resolves): iife, global `EasyProps`, platform neutral, target es2022, unminified so script errors stay readable.
   A plugin loads `?raw` imports (their engine kits' shader files) as text; `easy-props/<path>` imports in
   `entry.ts` resolve under their `frontend/src`.
4. Writes to `src/PropGenerator.Engine/Resources/`: `engine.js`, `shims.js` (copied), `thumbs/` (their 101
   preset PNGs from `public/preset-thumbs/` and `preset-thumbs.json`, the fingerprints) and `engine-pin.json` (tag,
   commit, SHA-256 and size of engine.js, SHA-256 of shims.js, esbuild and three versions, thumbnail count).
   The bundle names its modules by paths relative to the clone (`frontend/src/...`, `raw:frontend/...`, and
   `ours:tools/engine/entry.ts` for ours), so the same tag and lockfile give the same bytes and SHA-256 whoever
   builds it and from whatever folder.

Node and pnpm run uncapped (the `systemd-run` memory cap was dropped in phase 3: the full smoke peaks at about
510 MiB). Their sources are never edited; everything ours goes in through `entry.ts` and `shims.js`.

## Upgrading

`tools/engine/build.sh v0.4.5 --smoke`, then the engine tests, then commit `Resources/`. If upstream renames a
function `entry.ts` imports, the bundle fails to build and names it; fix `entry.ts`, not their code.

The tag comes from GitHub when the reference checkout lacks it (see Build, step 1); the clone needs the network once.

What the engine tests check after an upgrade: `EngineSmokeTests` (every preset at three levels and an export, each
under the time limit), `EmbeddedBundleTests` (the bundle loads, its info names the pinned tag, a real build, cancel and
export), and the host and thread tests (`EngineHostTests`, `EngineThreadTests`, `RulesEngineTests`, on the stand-in).
Then run Desktop.Tests: the app's kinds, part edits and old work files (`Fixtures/port-0.4.2`) run on the new bundle.
The cross-check against the 0.4.2 goldens and the C# port went with the port in phase 4 (last at 56239dc).

## Files

| File | What it is |
|---|---|
| `build.sh` | Clone or fetch, install, run build.mjs, optionally the smoke. |
| `build.mjs` | The esbuild run, the resource copies and the pin. Prints the bundle's size by source. |
| `entry.ts` | The API, global `EasyProps` (below). Calls only easy-props' exported functions. |
| `shims.js` | Stand-ins for the browser things their code reaches. The host loads it before `engine.js`. |
| `smoke.mjs` | The smoke check, in `node:vm` with only the shims as globals. |
| `build.test.sh` | Checks where build.sh clones a tag from (fake repos, no network, no real build). |

## The API (global `EasyProps`)

Arguments are JSON strings or numbers. Results are JSON strings, or `{meta: string, arrays: TypedArray[]}`.
Positions are easy-props' Y-up metres. `style` is `'low-poly'` or `'detailed'` (the app uses `'low-poly'`). A scene is `{generatorId, seed, parameters, components?, materials?, generatorVersion?}`; without
components they are made as easy-props makes them for a preset. In components, a parameter written `"$asset"` stands
for the asset's validated parameters (a painted tree's 52 parts each carry the whole tree otherwise).

Async calls take an optional job token as their last argument (`build` also accepts it in its options; 0 when
none is given). The `MessageChannel` stand-in, which easy-props' `yieldIfBusy` uses between components, asks
`__host.cancelled(token)` every time and throws when it says yes, so the job's promise rejects with
`EasyProps: cancelled`. A host that runs one call at a time can ignore the token and answer for the running call. `exportPackage` also passes
an abort signal built on the same question, and reports `__host.progress(token, done, total)` when the host has it.

| Call | Returns |
|---|---|
| `info()` | `{tag, commit, appVersion, levelShares, farLevel, budgets, generators: [{id, version, label, lowPoly, still, swaysEvenly, offersCollision, offersPivot}]}` |
| `presets()` | `{categories, presets: [{id, name, sub, category\|null, generatorId, seed, parameters, family, thumbnailFresh}]}`: all of `treePresets` (101 in 0.4.2, round-canopy included), no retired ones. `thumbnailFresh` is `presetFingerprint(id) === preset-thumbs.json[id]`. |
| `preset(id)` | One preset through `findPreset`, retired ones included, with `retired`; `null` when unknown. |
| `parameters(generatorId)` | `{specs, colourRows}`: upstream's `ParameterSpec[]` as is; `colourRows` only for painted-tree, made from `COLOUR_KEYS`, `COLOUR_LABELS`, `PALETTES` and `BARK_COLOURS`/`LEAF_COLOURS` (group `Colours`, ownOf `palette`, set `tree`). |
| `shown(gen, values)` | `[{key, label, group}]`: `shownControls` plus the colour rows, through `withOwnColoursShown`, on validated values. Relabels (palm "Fronds") and regroups come from upstream. |
| `adjust(gen, before, after)` | The patch of colours a choice switched to My own colours starts from (`startOwnColours`; for a painted tree, `resolvePalette` of the values before). `{}` when nothing changes. |
| `validate(gen, values)` | Upstream's `{valid, parameters, issues}`. |
| `traits(gen, values)` | `{farAway, sways, swaysEvenly}`. |
| `components(gen, values, seed)` | The prop's components (`generatePropComponents`, low-poly style as `createSceneFromPreset`), compact. |
| `build(scene, style, detail, options?, token?)` | async `{meta, arrays}`: the parts of `sceneExportSource(...).buildAtDetail(detail)`, one component at a time so each part names its component. `options` is `{fullFaces?, token?}`. |
| `texture(key)` | `{meta: {w, h, colorSpace, repeat}, arrays: [Uint8Array RGBA]}` for `bark:<print>:<strength>`, `relief:<print>`, `dab`, `leaf`, `leaf:<style>:<bloom>`, `tree-leaf:<style>`, `stone:<grain>:<lichen>`, `stone-relief:<grain>`, `end-grain`, `end-grain-relief`, `white`. Any key but `white` may end in `@<size>`: the picture drawn that many pixels across, as an export's `atSize` draws it (bark, relief, stone and end grain; the others have one size and come back as they are). The array is the engine's cached texture: read it, do not change it. |
| `exportPackage(scene, style, settings, token?)` | async `[{name, bytes: Uint8Array, faces\|null}]` from upstream `buildExport`. `settings` is upstream `ExportSettings`. |
| `collision(scene, style, token?)` | async `{offered, shapes}`: what the generator offers itself (`[]` when none) and what the export writes (`collisionShapesOf`, made from the solid parts when none are offered). |
| `pivot(scene)` | `[x, y, z]`. |

Phase 3 adds upstream's scene operations and the features around them. Each calls upstream's exported function; the
scenes they return are in the host's form above (components compact).

| Call | Returns |
|---|---|
| `sceneOp(scene, op, style)` | The scene after one of upstream's operations, or `null` when upstream left the asset as it was (refused). `op` is `{kind, componentId, ...}`: `copy` (`copyComponent`; `ids?` names the copies, else upstream's editor names them `man-<type>-<n>`), `remove`, `restore` (`restoreComponentDefault`), `break` (`breakComponent`), `position` (`setComponentPosition`, `position`), `transform` (`updateComponentTransform`, `transform`), `parameters` (`updateComponentParameters`, `patch`), `part` (a painted part's `action`: `{kind: 'patch', patch}`, `{kind: 'move', position}`, `remove`, `break`, `restore`, through the operation that sends it to `applyPartAction`); and, with no component, `tree` (`updateTreeParameters`, `patch`) and `seed` (`setAssetSeed`, `seed`), which make every component again. |
| `partFacts(scene, componentId)` | `{painted, heavy, canBreak}` (`isPaintedPart`, `isHeavyLimbKey`, `canBreakPart`). |
| `parseEdits(text)` | `{limbs, clumps}`; a damaged list is empty. |
| `mergeGenerated(kept, generated)` | Upstream's `mergeGenerated` of two component arrays, as given. |
| `earlierLooks(scene)` | The scene through upstream's `parseProject` (as a version 1 document): `withEarlierLooks` fills what an old file lacks (a cut rock without Grain gets the seven later settings at 0, in the asset and each component's `rock`). Nothing else is validated. |
| `earlierLooksOf(generatorId)` | `{asset: {key: value}, rock: {key: value}}`: what `earlierLooks` fills for that generator; both empty but for `cut-rock`. |
| `fitControls(scene)` | `[{key, label, min, max, step, limit}]` (`fitControlsOf`). |
| `countFaces(scene, style, token?)` | async `{wood, leaves}`: upstream's `countFaces`, the faces at full detail. |
| `makeItFit(scene, style, budget, options?, token?)` | async `{scene, changes: [{key, label, from, to}], description, counts: {faces, budget, fits, stopped}}`. `options` is `{deadlineMs?}`: 5000 as upstream's editor when left out, `null` for none. |
| `batchItems(choices, variations, context)` | `[{name, folder, exportName}]`, names only, numbered as upstream (`old_oak_01`). Choices: `{kind: 'preset', id}`, `{kind: 'scene', name}`, `{kind: 'versions', name, versions}`; `context` is `{style}`. Each item's scene is made at its turn (`versionScene`). |
| `exportNames(name)` | `{folder, export}`: `folderNameOf` and `exportNameOf`. |
| `pageSeeds(seed, page)`, `damageSettings(gen, values)`, `pageDamages(seed, page, settings)` | Show me more's eight seeds, a prop's damage settings (number specs of group Damage), and eight damaged versions `{seed, damage}`. |
| `damageNames(version, settings)`, `versionKey(version)` | JSON strings: "Snapped top, Split trunk"; `"<seed>"` or `"<seed>:<key>=<value>,..."`. |
| `versionScene(scene, version, style)` | The prop on that version's seed, damaged as it says. |
| `pieceLabels()` | `PIECE_LABELS`: `{stump, log, branch, litter}` each `{name, help}`. |
| `piecesOffered(scene)` | The piece kinds this tree can make (`[]` but for a painted tree; a palm has no branch). Plans the tree: heavy. |
| `makePieces(scene, treeName, kinds, style)` | `[{kind, name, scene}]`: each piece as a prop of its own (`tree-<kind>`, carrying `tree`, `treeSeed`, `treeName`). |
| `colourGroups(props)`, `usualColours(groups)` | The colour settings a set shares (`props` is `[{generatorId, parameters}]`) and each group's usual choice. |
| `inSetColours(scene, colours, style)` | The prop made again in the set's colours. |
| `setList(name, entries, groups, colours)` | `{name: '<name>.set.json', text}`. `entries` is `[{name, files: [{name, json?}]}]`, `json` being a GLB's JSON chunk, from which the materials are read as upstream's `materialsInGlb` reads them. |

`exportPackage`'s settings pass upstream's `pictureSize` (512, 1024, 2048: the closest level's pictures) and `setName`
(materials named `<setName>_<picture>[_wetNN]`, shared across a set's files) straight through.

`build` meta: `{parts: [...], fullFaces?}`, each part `{componentId, generatorId, material, role, roleOfPart,
flatShading, matrix (16, column first), tint (linear rgb), growth, proxy, surface: {vertexColors, map, normalMap,
alphaTest, receiveShadow, proxy, roughness, exportName} | null, vertices, faces, attributes: [{name, itemSize, normalized, array}], index:
{type: 'u16'|'u32', array} | null}`. `array` is the position in `arrays`. Every attribute is a Float32Array of
`vertices * itemSize` (names as upstream: `position`, `normal`, `color`, `uv`, `rigid`, ...). `growth` is the
thinning growth baked into the matrix (1 except for thinned foliage of old-style trees). `fullFaces` (asked with
`{fullFaces: true}`) is the non-proxy face count at detail 1.

## The shims

`shims.js` defines: `self`, `console` (to `__host.log(level, text)`), `performance.now`, `queueMicrotask`,
`MessageChannel` (delivers on a microtask; throws when the job is cancelled), `setTimeout`/`setInterval`/
`requestAnimationFrame` on a queue the host drains with `__runTimers()` (true while anything ran or waits),
`TextEncoder`/`TextDecoder` (UTF-8), `Blob`, `FileReader` (answers on a microtask), `ImageData`, `OffscreenCanvas` (a
2D context that holds pixels; `convertToBlob` calls `__host.png(rgba, w, h)`), and a synchronous
`WebAssembly.instantiate`/`compile` (three's SimplifyModifier waits on meshopt's, and an embedded V8 has no
platform to finish an async compile). `Worker` stays undefined, so their bark pool builds on the calling thread.

The host object: `__host.log(level, text)`, `__host.cancelled(token)`, `__host.png(rgba, w, h) -> Uint8Array`,
optionally `__host.progress(token, done, total)`.

No far painter is installed: a `far: true` export has no `_far.glb` (upstream's WebGL painter finds no `document`
and gives none).

## The smoke check

```sh
node tools/engine/smoke.mjs --tag v0.4.5 [--only oak,boulder] [--style low-poly] [--no-export] [--easy-props <clone>]
```

It loads `shims.js` and `engine.js` into an empty `node:vm` context (no Node or browser globals) with a Node-backed
`__host` (zlib PNG encoder), then checks: `info`, `presets` (101 at 0.4.2, round-canopy in, wide-canopy retired),
`parameters`/`shown`/`validate`/`traits` for every generator, `adjust` to own colours, that the compact components
build the same bytes, that a cancelled build rejects, every preset at detail 1, 0.4 and 0.12 (attributes, index
range, faces, `fullFaces`), every texture key a part named, and the oak and boulder exports (LOD0-2 GLBs with valid
headers and PNG images, asset.json, collision, pivot). Then the phase 3 calls: `sceneOp` on oak (turn a limb, move a
clump, break, remove, restore, tree, seed) and `mergeGenerated` equal upstream's own functions, bundled from the clone
(`--easy-props`, default `~/.cache/prop-generator/easy-props-<tag>`) into a second context in the same run; a broken
limb survives the next edit; refusals are `null`; `earlierLooks`/`earlierLooksOf`; Make it fit brings oak under 60 % of
its faces; Show me more's seeds and damage; `batchItems` names; every piece of oak, a palm, a conifer, a dead tree and
a fruit tree at three levels and exported (with its `_ends` mesh); texture keys at `@<size>`; a set of two boulders
"My set" at picture size 2048 (materials `my_set_<picture>`, shared, a 2048 picture at LOD0, the set list); a wet
snowy boulder has roughness under 1. Browser globals the shims do not define are trapped: reading
one outside the known `typeof` guards (`window`, `document`, `navigator`, `Worker`, three's devtools hook, GLTFExporter's
image checks) fails the check.
