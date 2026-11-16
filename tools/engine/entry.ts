/**
 * The engine's API: easy-props' own code, wrapped as plain functions over JSON and typed arrays, so the C# host never
 * touches a three.js object. Bundled by build.mjs into one iife whose global is `EasyProps`.
 *
 * Conventions:
 * - Arguments are JSON strings or numbers. Results are JSON strings, or `{meta: string, arrays: TypedArray[]}` for
 *   meshes and pixels (the host copies the arrays out).
 * - Positions are easy-props' own: Y up, metres. The host converts.
 * - `style` is a StyleProfile id: 'low-poly' or 'detailed'.
 * - A scene is `{generatorId, seed, parameters, components?, materials?, generatorVersion?}`. Left out, components
 *   are made from the parameters and seed as easy-props makes them for a preset. A component parameter written as
 *   "$asset" stands for the asset's validated parameters (painted parts carry the whole tree; see `components`).
 * - Async calls take an optional job token last. Between components the yield stand-in asks the host
 *   `__host.cancelled(token)` and stops the job by throwing.
 *
 * Only easy-props' exported functions are called. Nothing here changes what they make.
 */

import * as THREE from 'three'
import {findComponentGenerator} from 'easy-props/assets/component-generators'
import {findPropGenerator, generatePropComponents, offersFarAway, propGenerators, shownControls} from 'easy-props/assets/prop-generators'
import type {SceneComponent} from 'easy-props/assets/shared/component-types'
import {getBarkRelief, getBarkTexture} from 'easy-props/assets/shared/bark-texture'
import {getDabTexture} from 'easy-props/assets/shared/dab-texture'
import {getEndGrainRelief, getEndGrainTexture} from 'easy-props/assets/shared/end-grain-texture'
import {roleOfPart, type GeneratedPart, type PropGenerator} from 'easy-props/assets/shared/generator-types'
import {getLeafTexture, type BushLeafStyle} from 'easy-props/assets/shared/leaf-texture'
import type {ColourParameter, ParameterSpec} from 'easy-props/assets/shared/parameters'
import {getStoneRelief, getStoneTexture} from 'easy-props/assets/shared/stone-texture'
import {getTreeLeafTexture, type PrintedLeafStyle} from 'easy-props/assets/shared/tree-leaf-texture'
import {BARK_COLOURS, DEFAULT_MATERIALS, LEAF_COLOURS} from 'easy-props/assets/style/materials'
import {startOwnColours, withOwnColoursShown} from 'easy-props/assets/style/own-colours'
import {PALETTES} from 'easy-props/assets/style/palettes'
import {getStyleProfile, type StyleProfile, type StyleProfileId} from 'easy-props/assets/style/style-profiles'
import {COLOUR_KEYS, COLOUR_LABELS, paintedTreeDefaults, resolvePalette, validatePaintedTreeParameters} from 'easy-props/assets/trees/painted'
import {findPreset, PRESET_CATEGORIES, familyOfPreset, treePresets, type TreePreset} from 'easy-props/assets/trees/presets'
import {collisionShapesOf} from 'easy-props/core/export/collision-view'
import {APP_VERSION} from 'easy-props/core/export/export-preferences'
import {batchItems as upstreamBatchItems, MAX_VARIATIONS, versionScene as upstreamVersionScene, type BatchChoice} from 'easy-props/core/export/batch-items'
import {countFaces as upstreamCountFaces, describeChanges, fitControlsOf, makeItFit as upstreamMakeItFit} from 'easy-props/core/export/make-it-fit'
import {buildExport} from 'easy-props/core/export/pipeline/package'
import {BUDGET_TARGETS} from 'easy-props/core/export/pipeline/budget'
import {exportNameOf, FAR_LEVEL, folderNameOf, LEVEL_SHARES, type ExportSettings, type PlacedPart} from 'easy-props/core/export/pipeline/types'
import {sceneExportSource} from 'easy-props/core/export/scene-source'
import {colourGroups as upstreamColourGroups, inSetColours as upstreamInSetColours, usualColours as upstreamUsualColours, type ColourGroup, type SetColours, type SetProp} from 'easy-props/core/export/set-colours'
import {setEntryOf, setListFile} from 'easy-props/core/export/set-list'
import {parseProject} from 'easy-props/core/project/project-document'
import type {Scene} from 'easy-props/core/scene/scene-model'
import {
    breakComponent,
    copyComponent,
    mergeGenerated as upstreamMergeGenerated,
    removeComponent,
    restoreComponentDefault,
    setAssetSeed,
    setComponentPosition,
    updateComponentParameters,
    updateComponentTransform,
    updateTreeParameters,
} from 'easy-props/core/scene/scene-ops'
import {makePieces as upstreamMakePieces, PIECE_LABELS, piecesOffered as upstreamPiecesOffered} from 'easy-props/core/scene/tree-pieces'
import {
    damageNames as upstreamDamageNames,
    damageSettings as upstreamDamageSettings,
    pageDamages as upstreamPageDamages,
    pageSeeds as upstreamPageSeeds,
    versionKey as upstreamVersionKey,
    type PropVersion,
} from 'easy-props/core/scene/variations'
import {COMPONENT_TYPES} from 'easy-props/assets/shared/component-types'
import type {NumberParameter} from 'easy-props/assets/shared/parameters'
import {hashSeed} from 'easy-props/assets/shared/random'
import {canBreakPart, isHeavyLimbKey, isPaintedPart, parseEdits as upstreamParseEdits, type PartAction} from 'easy-props/assets/trees/painted/edits'
import {sourceOf, type PieceParameters} from 'easy-props/assets/trees/pieces/piece-generators'
import type {PieceKind} from 'easy-props/assets/trees/pieces/tree-pieces'
import {presetFingerprint} from 'easy-props/panels/preset-fingerprint'
import presetThumbs from 'easy-props/panels/preset-thumbs.json'

declare const __ENGINE_TAG__: string
declare const __ENGINE_COMMIT__: string
declare const __host: {cancelled?(token: number): boolean; progress?(token: number, done: number, total: number): void}

type Values = Record<string, unknown>

/** What the host sends as a scene. */
interface SceneInput {
    readonly generatorId: string
    readonly seed: number
    readonly parameters: Values
    readonly components?: readonly SceneComponent[]
    readonly materials?: {bark: string; leaves: string}
    readonly generatorVersion?: number
}

const ASSET = '$asset'
const PAINTED_TREE = 'painted-tree'

const parse = <T>(json: unknown, what: string): T => {
    if (typeof json !== 'string') {
        // Already an object (a test calling in from JS); used as is.
        if (json !== null && typeof json === 'object') {
            return json as T
        }
        throw new Error(`EasyProps: ${what} must be a JSON string`)
    }
    return JSON.parse(json) as T
}

function propGenerator(id: string): PropGenerator {
    const generator = findPropGenerator(id)
    if (!generator) {
        throw new Error(`EasyProps: no generator '${id}'`)
    }
    return generator
}

function styleOf(id: unknown): StyleProfile {
    const style = getStyleProfile(String(id) as StyleProfileId)
    if (!style) {
        throw new Error(`EasyProps: no style '${String(id)}'`)
    }
    return style
}

// ---- the job token, read by the shims' MessageChannel between components ----

const jobs = globalThis as unknown as {__engineToken: number}
jobs.__engineToken = 0

/** Whether the host says to stop this job (a host without tokens answers for the call it is running). */
const cancelled = (token: number) => typeof __host !== 'undefined' && __host.cancelled?.(token) === true

/** Runs an async job under its token, so the yield stand-in can stop it. */
async function asJob<T>(token: unknown, work: () => Promise<T>): Promise<T> {
    const id = typeof token === 'number' && Number.isFinite(token) ? token : 0
    const before = jobs.__engineToken
    jobs.__engineToken = id
    try {
        if (cancelled(id)) {
            throw new Error('EasyProps: cancelled')
        }
        return await work()
    } finally {
        jobs.__engineToken = before
    }
}

// ---- scenes and the compact component form ----

function validatedOf(generator: PropGenerator, parameters: Values): Values {
    return generator.validate(parameters).parameters as Values
}

/** Component parameters equal to the asset's validated parameters become "$asset". */
function compact(components: readonly SceneComponent[], asset: Values): unknown[] {
    const assetJson = JSON.stringify(asset)
    return components.map((component) => {
        const parameters: Values = {}
        for (const [key, value] of Object.entries(component.parameters as Values)) {
            parameters[key] = value !== null && typeof value === 'object' && JSON.stringify(value) === assetJson ? ASSET : value
        }
        return {...component, parameters}
    })
}

/** Puts the asset's validated parameters back where "$asset" stands. */
function expand(components: readonly SceneComponent[], asset: Values): SceneComponent[] {
    return components.map((component) => {
        const parameters: Values = {}
        for (const [key, value] of Object.entries(component.parameters as Values)) {
            parameters[key] = value === ASSET ? asset : value
        }
        return {...component, parameters} as SceneComponent
    })
}

function sceneOf(json: unknown): Scene {
    const input = parse<SceneInput>(json, 'scene')
    const generator = propGenerator(input.generatorId)
    const parameters = input.parameters ?? {}
    const seed = Number(input.seed)
    const validated = validatedOf(generator, parameters)
    const components = input.components
        ? expand(input.components, validated)
        : generatePropComponents(input.generatorId, parameters, seed, getStyleProfile('low-poly'))
    return {
        id: 'scene-1',
        assets: [
            {
                id: 'asset-1',
                kind: 'tree',
                generatorId: input.generatorId,
                generatorVersion: input.generatorVersion ?? 1,
                components,
                seed,
                parameters: parameters as Scene['assets'][number]['parameters'],
                materials: input.materials ?? DEFAULT_MATERIALS,
            },
        ],
    }
}

/** A scene as the host keeps it: the first asset, its components compact. */
function sceneOut(scene: Scene): Values {
    const asset = scene.assets[0]
    const generator = propGenerator(asset.generatorId)
    return {
        generatorId: asset.generatorId,
        seed: asset.seed,
        parameters: asset.parameters,
        components: compact(asset.components, validatedOf(generator, asset.parameters as Values)),
        materials: asset.materials,
        generatorVersion: asset.generatorVersion,
    }
}

// ---- textures, named by what they are ----

const textureKeys = new WeakMap<THREE.Texture, string>()

/**
 * The texture a key names, at the size the app shows it. A key may end in `@<size>`: the picture drawn again that many
 * pixels across, as an export's `atSize` asks for it (a picture that has no larger drawing stays as it is).
 */
function textureOf(key: string): THREE.DataTexture | null {
    const at = key.lastIndexOf('@')
    const size = at < 0 ? undefined : Number(key.slice(at + 1))
    if (size !== undefined && !(Number.isInteger(size) && size > 0)) {
        return null
    }
    const [kind, a, b] = (at < 0 ? key : key.slice(0, at)).split(':')
    switch (kind) {
        case 'dab':
            return getDabTexture()
        case 'leaf':
            // 'leaf' alone is the default bush leaf; 'leaf:<style>:<bloom>' any other.
            return a === undefined ? getLeafTexture() : getLeafTexture(a as Parameters<typeof getLeafTexture>[0], b as Parameters<typeof getLeafTexture>[1])
        case 'tree-leaf':
            return getTreeLeafTexture(a as Parameters<typeof getTreeLeafTexture>[0])
        case 'bark':
            return getBarkTexture(Number(b), a as Parameters<typeof getBarkTexture>[1], size)
        case 'relief':
            return getBarkRelief(a as Parameters<typeof getBarkRelief>[0], size)
        case 'stone':
            return getStoneTexture(Number(a), size, Number(b))
        case 'stone-relief':
            return getStoneRelief(Number(a), size)
        case 'end-grain':
            return getEndGrainTexture(size)
        case 'end-grain-relief':
            return getEndGrainRelief(size)
        default:
            return null
    }
}

// The leaf pictures each getter draws (their types, as values): a style is only asked of the getter that draws it.
const PRINTED_LEAVES: readonly PrintedLeafStyle[] = ['round', 'lobed', 'feathery', 'needles']
const BUSH_LEAVES: readonly BushLeafStyle[] = ['pointed', 'tiny', 'narrow', 'spiky', 'scales', 'glossy']

/** Every value under these keys anywhere in a component's validated parameters. */
function valuesUnder(values: unknown, keys: readonly string[], out: Map<string, unknown[]> = new Map()): Map<string, unknown[]> {
    if (values !== null && typeof values === 'object') {
        for (const [key, value] of Object.entries(values as Values)) {
            if (keys.includes(key)) {
                out.set(key, [...(out.get(key) ?? []), value])
            }
            valuesUnder(value, keys, out)
        }
    }
    return out
}

/** The key of a part's texture: found by identity against the getters that could have made it. */
function keyOfTexture(texture: THREE.Texture, component: SceneComponent): string {
    const known = textureKeys.get(texture)
    if (known) {
        return known
    }
    const generator = findComponentGenerator(component.generatorId)
    const validated = generator ? generator.validate(component.parameters).parameters : component.parameters
    // A piece (stump, log, branch, litter) carries its tree as JSON text; its bark and leaves are that tree's.
    const piece = (validated as {piece?: PieceParameters}).piece
    const tree = piece && typeof piece.tree === 'string' ? sourceOf(piece).tree : null
    const found = valuesUnder([validated, tree], ['barkPattern', 'brushStrokes', 'leafStyle', 'bloom', 'grain', 'lichen'])
    const all = (key: string) => found.get(key) ?? []
    const candidates: string[] = ['dab', 'leaf', 'end-grain', 'end-grain-relief']
    for (const pattern of all('barkPattern')) {
        candidates.push(`relief:${String(pattern)}`, ...all('brushStrokes').map((strokes) => `bark:${String(pattern)}:${String(strokes)}`))
    }
    for (const style of all('leafStyle')) {
        if (PRINTED_LEAVES.includes(style as PrintedLeafStyle)) {
            candidates.push(`tree-leaf:${String(style)}`)
        }
        if (BUSH_LEAVES.includes(style as BushLeafStyle)) {
            candidates.push(...all('bloom').map((bloom) => `leaf:${String(style)}:${String(bloom)}`))
        }
    }
    for (const grain of all('grain')) {
        candidates.push(`stone-relief:${String(grain)}`, ...[0, ...all('lichen')].map((lichen) => `stone:${String(grain)}:${String(lichen)}`))
    }
    for (const key of candidates) {
        if (textureOf(key) === texture) {
            textureKeys.set(texture, key)
            return key
        }
    }
    throw new Error(`EasyProps: a texture on '${component.generatorId}' is none the engine can name`)
}

// ---- meshes out ----

interface PartMeta {
    componentId: string
    generatorId: string
    material: string
    role: string | null
    roleOfPart: string
    flatShading: boolean
    matrix: readonly number[]
    tint: readonly number[]
    growth: number
    proxy: boolean
    surface: {
        vertexColors: boolean
        map: string | null
        normalMap: string | null
        alphaTest: number | null
        receiveShadow: boolean | null
        proxy: boolean
        roughness: number | null
        exportName: string | null
    } | null
    vertices: number
    faces: number
    attributes: {name: string; itemSize: number; normalized: boolean; array: number}[]
    index: {type: 'u16' | 'u32'; array: number} | null
}

function attributeArray(attribute: THREE.BufferAttribute | THREE.InterleavedBufferAttribute): Float32Array {
    if (!(attribute as THREE.InterleavedBufferAttribute).isInterleavedBufferAttribute && attribute.array instanceof Float32Array) {
        const array = attribute.array
        const length = attribute.count * attribute.itemSize
        return array.length === length ? array : array.subarray(0, length)
    }
    // Interleaved or not float: copied out as float32, component by component.
    const out = new Float32Array(attribute.count * attribute.itemSize)
    for (let i = 0; i < attribute.count; i++) {
        for (let k = 0; k < attribute.itemSize; k++) {
            out[i * attribute.itemSize + k] = attribute.getComponent(i, k)
        }
    }
    return out
}

function facesOf(part: GeneratedPart): number {
    const geometry = part.geometry
    return Math.floor((geometry.index ? geometry.index.count : geometry.getAttribute('position').count) / 3)
}

function growthOf(placed: PlacedPart, component: SceneComponent): number {
    const m = placed.matrix
    const sx = component.transform.scale[0]
    return sx === 0 ? 1 : Math.hypot(m[0], m[1], m[2]) / Math.abs(sx)
}

/** The parts of a scene at a detail level, one component at a time so each part knows its component. */
async function placedParts(scene: Scene, style: StyleProfile, detail: number): Promise<{placed: PlacedPart; component: SceneComponent}[]> {
    const asset = scene.assets[0]
    const out: {placed: PlacedPart; component: SceneComponent}[] = []
    for (const component of asset.components) {
        // The same buildAtDetail, given one component: it builds, thins and places exactly as for the whole scene.
        const one: Scene = {...scene, assets: [{...asset, components: [component]}]}
        for (const placed of await sceneExportSource(one, style, APP_VERSION).buildAtDetail(detail)) {
            out.push({placed, component})
        }
    }
    return out
}

function meshOut(parts: {placed: PlacedPart; component: SceneComponent}[]): {metas: PartMeta[]; arrays: ArrayBufferView[]} {
    const metas: PartMeta[] = []
    const arrays: ArrayBufferView[] = []
    for (const {placed, component} of parts) {
        const part = placed.part
        const geometry = part.geometry
        const attributes: PartMeta['attributes'] = []
        for (const [name, attribute] of Object.entries(geometry.attributes)) {
            attributes.push({name, itemSize: attribute.itemSize, normalized: attribute.normalized, array: arrays.length})
            arrays.push(attributeArray(attribute))
        }
        let index: PartMeta['index'] = null
        if (geometry.index) {
            const array = geometry.index.array
            const typed = array instanceof Uint16Array || array instanceof Uint32Array ? array : Uint32Array.from(array as ArrayLike<number>)
            index = {type: typed instanceof Uint16Array ? 'u16' : 'u32', array: arrays.length}
            arrays.push(typed.length === geometry.index.count ? typed : typed.subarray(0, geometry.index.count))
        }
        const surface = part.surface
        metas.push({
            componentId: component.id,
            generatorId: component.generatorId,
            material: part.material,
            role: part.role ?? null,
            roleOfPart: roleOfPart(part),
            flatShading: placed.flatShading,
            matrix: placed.matrix,
            tint: placed.tint,
            growth: growthOf(placed, component),
            proxy: surface?.proxy === true,
            surface: surface
                ? {
                      vertexColors: surface.vertexColors,
                      map: surface.map ? keyOfTexture(surface.map, component) : null,
                      normalMap: surface.normalMap ? keyOfTexture(surface.normalMap, component) : null,
                      alphaTest: surface.alphaTest ?? null,
                      receiveShadow: surface.receiveShadow ?? null,
                      proxy: surface.proxy === true,
                      roughness: surface.roughness ?? null,
                      exportName: surface.exportName ?? null,
                  }
                : null,
            vertices: geometry.getAttribute('position').count,
            faces: facesOf(part),
            attributes,
            index,
        })
    }
    return {metas, arrays}
}

const facesOfAll = (parts: {placed: PlacedPart}[]) =>
    parts.reduce((sum, {placed}) => sum + (placed.part.surface?.proxy === true ? 0 : facesOf(placed.part)), 0)

// ---- the API ----

/** 1. What this engine is. */
function info(): string {
    return JSON.stringify({
        tag: __ENGINE_TAG__,
        commit: __ENGINE_COMMIT__,
        appVersion: APP_VERSION,
        levelShares: LEVEL_SHARES,
        farLevel: FAR_LEVEL,
        budgets: BUDGET_TARGETS,
        maxVariations: MAX_VARIATIONS,
        generators: Object.values(propGenerators).map((generator) => ({
            id: generator.id,
            version: generator.version,
            label: generator.label,
            lowPoly: generator.lowPoly === true,
            still: generator.still === true,
            swaysEvenly: generator.swaysEvenly === true,
            offersCollision: typeof generator.collision === 'function',
            offersPivot: typeof generator.pivot === 'function',
        })),
    })
}

const thumbs = presetThumbs as Readonly<Record<string, string>>

function presetOut(preset: TreePreset) {
    return {
        id: preset.id,
        name: preset.name,
        sub: preset.sub,
        category: preset.category ?? null,
        generatorId: preset.generatorId,
        seed: preset.seed,
        parameters: preset.parameters,
        family: familyOfPreset(preset.id),
        thumbnailFresh: thumbs[preset.id] !== undefined && presetFingerprint(preset.id) === thumbs[preset.id],
    }
}

/** 2. Every preset easy-props lists, in its order (retired ones are left out). */
function presets(): string {
    return JSON.stringify({categories: PRESET_CATEGORIES, presets: treePresets.map(presetOut)})
}

/** 2b. One preset by id, retired ones included; null when there is none. */
function preset(id: string): string {
    const found = findPreset(String(id))
    return JSON.stringify(found ? {...presetOut(found), retired: !treePresets.includes(found)} : null)
}

/** The painted tree's own colours, as the rows easy-props' Details panel draws under Colours. */
function colourRowsOf(generatorId: string): ColourParameter[] {
    if (generatorId !== PAINTED_TREE) {
        return []
    }
    const start = PALETTES[paintedTreeDefaults.palette as keyof typeof PALETTES] ?? PALETTES.blossom
    return COLOUR_KEYS.map((key) => ({
        kind: 'colour',
        key,
        label: COLOUR_LABELS[key],
        group: 'Colours',
        default: start[key],
        ownOf: 'palette',
        set: 'tree',
        named: Object.fromEntries(Object.entries(PALETTES).map(([id, palette]) => [id, palette[key]])),
        swatches: key.startsWith('bark') ? BARK_COLOURS : LEAF_COLOURS,
    })) as unknown as ColourParameter[]
}

/** 3. A generator's controls, as upstream defines them, plus the painted tree's colour rows. */
function parameters(generatorId: string): string {
    const generator = propGenerator(generatorId)
    return JSON.stringify({specs: generator.parameters, colourRows: colourRowsOf(generator.id)})
}

/** 4a. The controls shown for these values, in order, under the names and groups they go by. */
function shown(generatorId: string, valuesJson: string): string {
    const generator = propGenerator(generatorId)
    const values = validatedOf(generator, parse<Values>(valuesJson, 'values'))
    const rows: readonly ParameterSpec[] = withOwnColoursShown(
        [...shownControls(generator.id, generator.parameters, values), ...colourRowsOf(generator.id)],
        values,
    )
    return JSON.stringify(rows.map((spec) => ({key: spec.key, label: spec.label, group: spec.group ?? null})))
}

/** 4b. What else changes with an edit: own colours start from the colours that were showing. */
function adjust(generatorId: string, beforeJson: string, afterJson: string): string {
    const generator = propGenerator(generatorId)
    const before = validatedOf(generator, parse<Values>(beforeJson, 'before'))
    const after = parse<Values>(afterJson, 'after')
    const specs = [...generator.parameters, ...colourRowsOf(generator.id)]
    const patch: Values = {}
    for (const spec of specs) {
        if (spec.kind === 'choice' && after[spec.key] !== undefined && after[spec.key] !== before[spec.key]) {
            patch[spec.key] = after[spec.key]
        }
    }
    if (Object.keys(patch).length === 0) {
        return '{}'
    }
    const start: Record<string, string> = startOwnColours(specs, before, patch)
    if (generator.id === PAINTED_TREE && patch.palette === 'custom' && before.palette !== 'custom') {
        // The colours showing: the palette's, with its bark tone's wood.
        const showing = resolvePalette(validatePaintedTreeParameters(before).parameters)
        for (const key of COLOUR_KEYS) {
            start[key] = showing[key]
        }
    }
    return JSON.stringify(start)
}

/** 4c. Upstream's own validation: clamped values and what was wrong. */
function validate(generatorId: string, valuesJson: string): string {
    return JSON.stringify(propGenerator(generatorId).validate(parse<Values>(valuesJson, 'values')))
}

/** 4d. How the prop behaves, for the view and the export settings. */
function traits(generatorId: string, valuesJson: string): string {
    const generator = propGenerator(generatorId)
    return JSON.stringify({
        farAway: offersFarAway(generator.id, parse<Values>(valuesJson, 'values')),
        sways: generator.still !== true,
        swaysEvenly: generator.swaysEvenly === true,
    })
}

/** 5. The prop's components, compact: parameters equal to the asset's validated ones are written "$asset". */
function components(generatorId: string, valuesJson: string, seed: number): string {
    const generator = propGenerator(generatorId)
    const values = parse<Values>(valuesJson, 'values')
    const made = generatePropComponents(generator.id, values, Number(seed), getStyleProfile('low-poly'))
    return JSON.stringify(compact(made, validatedOf(generator, values)))
}

/** 6. The parts at a detail level, as upstream's export builds them (sceneExportSource().buildAtDetail). */
function build(sceneJson: string, style: string, detail: number, optionsJson?: string, token?: number) {
    const options = optionsJson ? parse<{fullFaces?: boolean; token?: number}>(optionsJson, 'options') : {}
    return asJob(token ?? options.token, async () => {
        const scene = sceneOf(sceneJson)
        const profile = styleOf(style)
        const parts = await placedParts(scene, profile, Number(detail))
        let fullFaces: number | undefined
        if (options.fullFaces) {
            fullFaces = Number(detail) === 1 ? facesOfAll(parts) : facesOfAll(await placedParts(scene, profile, 1))
        }
        const {metas, arrays} = meshOut(parts)
        return {meta: JSON.stringify({parts: metas, ...(fullFaces === undefined ? {} : {fullFaces})}), arrays}
    })
}

/** 7. A texture's RGBA pixels, by the key a part names it with. */
function texture(key: string) {
    const text = String(key)
    if (text === 'white') {
        // What the export puts on a mesh with no texture of its own (merge.ts whiteTexture): 4 x 4 white.
        return {meta: JSON.stringify({w: 4, h: 4, colorSpace: 'srgb', repeat: false}), arrays: [new Uint8Array(4 * 4 * 4).fill(255)]}
    }
    const found = textureOf(text)
    if (!found) {
        throw new Error(`EasyProps: no texture '${text}'`)
    }
    const image = found.image as {data: Uint8Array; width: number; height: number}
    return {
        meta: JSON.stringify({w: image.width, h: image.height, colorSpace: found.colorSpace, repeat: found.wrapS === THREE.RepeatWrapping}),
        arrays: [image.data],
    }
}

/** 8. Upstream's whole export package: every file, as name and bytes. */
function exportPackage(sceneJson: string, style: string, settingsJson: string, token?: number) {
    return asJob(token, async () => {
        const id = jobs.__engineToken
        const settings = parse<ExportSettings>(settingsJson, 'settings')
        const source = sceneExportSource(sceneOf(sceneJson), styleOf(style), APP_VERSION)
        const signal = {get aborted() { return cancelled(id) }} as unknown as AbortSignal
        const progress = (done: number, total: number) => {
            if (typeof __host !== 'undefined') {
                __host.progress?.(id, done, total)
            }
        }
        const files = await buildExport(source, settings, progress, signal)
        return files.map((file) => ({name: file.name, bytes: file.bytes, faces: file.faces ?? null}))
    })
}

/**
 * 9a. Collision: `offered` is what the generator offers itself (empty for one that offers none); `shapes` is what
 * the export writes, made from the solid parts when none are offered (upstream collisionShapesOf).
 */
function collision(sceneJson: string, style: string, token?: number) {
    return asJob(token, async () => {
        const scene = sceneOf(sceneJson)
        const profile = styleOf(style)
        const offered = sceneExportSource(scene, profile, APP_VERSION).collision()
        const shapes = await collisionShapesOf(scene, profile)
        return JSON.stringify({offered, shapes})
    })
}

/** 9b. Where the prop's base meets the ground. */
function pivot(sceneJson: string): string {
    return JSON.stringify(sceneExportSource(sceneOf(sceneJson), getStyleProfile('low-poly'), APP_VERSION).pivot())
}

// ---- phase 3: upstream's scene operations, fitting, versions, pieces and sets ----

/** One part edit, as upstream's scene operations take it (E1 of the phase 3 plan). */
type SceneOp =
    | {readonly kind: 'copy'; readonly componentId: string; readonly ids?: readonly string[]}
    | {readonly kind: 'remove' | 'restore' | 'break'; readonly componentId: string}
    | {readonly kind: 'position'; readonly componentId: string; readonly position: readonly [number, number, number]}
    | {readonly kind: 'transform'; readonly componentId: string; readonly transform: SceneComponent['transform']}
    | {readonly kind: 'parameters'; readonly componentId: string; readonly patch: Values}
    | {readonly kind: 'part'; readonly componentId: string; readonly action: PartAction}
    | {readonly kind: 'tree'; readonly patch: Readonly<Record<string, number | string>>}
    | {readonly kind: 'seed'; readonly seed: number}

const FIRST_ASSET = 'asset-1'

/**
 * A copy's ids as upstream's editor names them (editor-store copyComponent and uniqueComponentId): `man-<type>-<n>`,
 * `n` from the part's seed and the attempt, with `-1`, `-2` on when the scene already has that id.
 */
function copyIds(scene: Scene, componentId: string, given?: readonly string[]): (attempt: number) => string {
    const source = scene.assets[0]?.components.find((component) => component.id === componentId)
    const taken = new Set(scene.assets[0]?.components.map((component) => component.id) ?? [])
    return (attempt) => {
        if (given && given[attempt] !== undefined) {
            return String(given[attempt])
        }
        const base = `man-${source?.type ?? 'part'}-${hashSeed(source?.seed ?? 0, attempt + 1) % 100000}`
        let id = base
        for (let n = 1; taken.has(id); n++) {
            id = `${base}-${n}`
        }
        return id
    }
}

/** The painted part an action is for, or null: upstream's editPaintedPart acts only on those. */
function paintedPart(scene: Scene, componentId: string): SceneComponent | null {
    const component = scene.assets[0]?.components.find((candidate) => candidate.id === componentId)
    return component && isPaintedPart(component.generatorId) ? component : null
}

function applied(scene: Scene, op: SceneOp, style: string): Scene {
    switch (op.kind) {
        case 'copy':
            return copyComponent(scene, op.componentId, copyIds(scene, op.componentId, op.ids))
        case 'remove':
            return removeComponent(scene, op.componentId)
        case 'restore':
            return restoreComponentDefault(scene, op.componentId)
        case 'break':
            return breakComponent(scene, op.componentId)
        case 'position':
            return setComponentPosition(scene, op.componentId, [Number(op.position[0]), Number(op.position[1]), Number(op.position[2])])
        case 'transform':
            return updateComponentTransform(scene, op.componentId, op.transform)
        case 'parameters':
            return updateComponentParameters(scene, op.componentId, op.patch)
        case 'part': {
            // Upstream's painted applyPartAction, reached through the operation that sends each action to it.
            if (!paintedPart(scene, op.componentId)) {
                return scene
            }
            const action = op.action
            switch (action.kind) {
                case 'patch':
                    return updateComponentParameters(scene, op.componentId, action.patch)
                case 'move':
                    return setComponentPosition(scene, op.componentId, action.position)
                case 'remove':
                    return removeComponent(scene, op.componentId)
                case 'break':
                    return breakComponent(scene, op.componentId)
                case 'restore':
                    return restoreComponentDefault(scene, op.componentId)
                default:
                    throw new Error(`EasyProps: no part action '${String((action as {kind?: unknown}).kind)}'`)
            }
        }
        case 'tree':
            return updateTreeParameters(scene, FIRST_ASSET, op.patch, styleOf(style))
        case 'seed':
            return setAssetSeed(scene, FIRST_ASSET, Math.trunc(Number(op.seed)), styleOf(style))
        default:
            throw new Error(`EasyProps: no scene op '${String((op as {kind?: unknown}).kind)}'`)
    }
}

/**
 * 10a. One scene operation of upstream's, by kind: copy (copyComponent), remove, restore (restoreComponentDefault),
 * position (setComponentPosition), transform (updateComponentTransform), parameters (updateComponentParameters),
 * part (a painted part's patch, move, remove, break or restore), break (breakComponent), tree (updateTreeParameters)
 * and seed (setAssetSeed). The scene after it, or `null` when upstream refused (left the asset as it was).
 * tree and seed make every component again: the host runs them off the UI thread.
 */
function sceneOp(sceneJson: string, opJson: string, style: string): string {
    const scene = sceneOf(sceneJson)
    const after = applied(scene, parse<SceneOp>(opJson, 'op'), style)
    return JSON.stringify(after.assets[0] === scene.assets[0] ? null : sceneOut(after))
}

/** 10b. What can be done to a part: is it a painted one, a heavy limb, and can it break now. */
function partFacts(sceneJson: string, componentId: string): string {
    const scene = sceneOf(sceneJson)
    const asset = scene.assets[0]
    const component = asset.components.find((candidate) => candidate.id === String(componentId))
    if (!component) {
        return JSON.stringify({painted: false, heavy: false, canBreak: false})
    }
    const key = typeof component.parameters.key === 'string' ? component.parameters.key : ''
    const edits = typeof asset.parameters.edits === 'string' ? asset.parameters.edits : ''
    return JSON.stringify({
        painted: isPaintedPart(component.generatorId),
        heavy: component.generatorId === 'painted-limb' && isHeavyLimbKey(key),
        canBreak: canBreakPart(component, edits),
    })
}

/** 10c. A painted tree's edit list, read as upstream reads it (a damaged list is no edits). */
function parseEdits(text: string): string {
    return JSON.stringify(upstreamParseEdits(typeof text === 'string' ? text : ''))
}

/** 10d. Upstream's mergeGenerated: fresh generated components folded into the kept ones. Components as given. */
function mergeGenerated(keptJson: string, generatedJson: string): string {
    return JSON.stringify(upstreamMergeGenerated(parse<SceneComponent[]>(keptJson, 'kept'), parse<SceneComponent[]>(generatedJson, 'generated')))
}

/**
 * 11a. A scene as upstream opens a saved one: its parseProject fills what a file saved before a setting existed lacks
 * (withEarlierLooks: a cut rock without Grain gets the seven later rock settings at 0, in the asset and in each
 * component's `rock`). Read through a minimal version 1 project document, so parts without `slot` or `edited` get
 * upstream's migration. Returns the scene with its parameters and components as parseProject left them ("$asset"
 * kept where it stood; nothing validated).
 */
function earlierLooks(sceneJson: string): string {
    const input = parse<SceneInput>(sceneJson, 'scene')
    const document = {
        schemaVersion: 1,
        generator: 'easy-props',
        createdAt: 'engine',
        modifiedAt: 'engine',
        style: 'low-poly',
        scene: {
            id: 'scene-1',
            assets: [
                {
                    id: FIRST_ASSET,
                    kind: 'tree',
                    generatorId: input.generatorId,
                    generatorVersion: input.generatorVersion ?? 1,
                    seed: Math.trunc(Number(input.seed ?? 0)),
                    components: input.components ?? [],
                    parameters: input.parameters ?? {},
                    materials: input.materials ?? DEFAULT_MATERIALS,
                },
            ],
        },
    }
    const asset = parseProject(JSON.stringify(document)).scene.assets[0]
    return JSON.stringify({
        ...input,
        parameters: asset.parameters,
        ...(input.components ? {components: asset.components} : {}),
    })
}

/**
 * 11b. What earlierLooks fills for a generator, by asking it of an asset with no settings and one part with an empty
 * `rock`: `{asset: {key: value}, rock: {key: value}}`, both empty for a generator upstream leaves alone.
 */
function earlierLooksOf(generatorId: string): string {
    const probe = {
        generatorId: String(generatorId),
        seed: 0,
        parameters: {},
        components: [
            {
                id: 'probe',
                type: COMPONENT_TYPES[0],
                generatorId: 'probe',
                generatorVersion: 1,
                parentId: null,
                transform: {position: [0, 0, 0], rotation: [0, 0, 0], scale: [1, 1, 1]},
                parameters: {rock: {}},
                seed: 0,
                slot: null,
                edited: false,
            },
        ],
    }
    const filled = JSON.parse(earlierLooks(JSON.stringify(probe))) as {parameters: Values; components: {parameters: {rock: Values}}[]}
    return JSON.stringify({asset: filled.parameters, rock: filled.components[0].parameters.rock})
}

/** 12a. The controls Make it fit may lower, for these values: `[{key, label, min, max, step, limit}]`. */
function fitControls(sceneJson: string): string {
    return JSON.stringify(
        fitControlsOf(sceneOf(sceneJson)).map(({control, spec}) => ({key: spec.key, label: spec.label, min: spec.min, max: spec.max, step: spec.step, limit: control.limit})),
    )
}

/** 12b. The faces at full detail, as upstream's status bar counts them: `{wood, leaves}`. */
function countFaces(sceneJson: string, style: string, token?: number) {
    return asJob(token, async () => JSON.stringify(await upstreamCountFaces(sceneOf(sceneJson), styleOf(style))))
}

/** Upstream's editor gives Make it fit five seconds (editor-store FIT_TIME_MS). */
const FIT_TIME_MS = 5000

/**
 * 12c. Upstream's Make it fit: `{scene, changes: [{key, label, from, to}], description, counts: {faces, budget,
 * fits, stopped}}`. `options` is `{deadlineMs?}` (null for no deadline; 5000 when left out, as upstream).
 */
function makeItFit(sceneJson: string, style: string, budget: number, optionsJson?: string, token?: number) {
    const options = optionsJson ? parse<{deadlineMs?: number | null}>(optionsJson, 'options') : {}
    return asJob(token, async () => {
        const limit = options.deadlineMs === null ? Infinity : Number(options.deadlineMs ?? FIT_TIME_MS)
        const result = await upstreamMakeItFit(sceneOf(sceneJson), styleOf(style), Number(budget), {deadline: performance.now() + limit})
        return JSON.stringify({
            scene: sceneOut(result.scene),
            changes: result.changes,
            description: describeChanges(result.changes),
            counts: {faces: result.faces, budget: Number(budget), fits: result.fits, stopped: result.stopped},
        })
    })
}

/** A batch choice as the host sends it: a preset, the open prop (a scene, by name), or its versions. */
type ChoiceInput =
    | {readonly kind: 'preset'; readonly id: string}
    | {readonly kind: 'scene'; readonly name: string; readonly scene?: unknown}
    | {readonly kind: 'versions'; readonly name: string; readonly versions: readonly PropVersion[]; readonly scene?: unknown}

/** The scene a choice of names only stands on: upstream's items read it only when loaded, which the host does itself. */
const NO_SCENE: Scene = {id: 'scene-1', assets: []}

/**
 * 13a. A batch's names, as upstream's batchItems numbers them (`oak_01`, `oak_02`, ...): `[{name, folder,
 * exportName}]`, where `folder` is the prop's own folder when each prop gets one. Names only: the host makes each
 * item's scene at its turn (versionScene), so the batch never holds every scene at once. `context` is `{style}`.
 */
function batchItems(choicesJson: string, variations: number, contextJson: string): string {
    const context = parse<{style?: string}>(contextJson, 'context')
    const choices = parse<ChoiceInput[]>(choicesJson, 'choices').map((choice): BatchChoice => {
        switch (choice.kind) {
            case 'preset':
                return {kind: 'preset', id: String(choice.id)}
            case 'scene':
                return {kind: 'saved', id: '', name: String(choice.name)}
            case 'versions':
                return {kind: 'versions', name: String(choice.name), scene: NO_SCENE, versions: choice.versions}
            default:
                throw new Error(`EasyProps: no batch choice '${String((choice as {kind?: unknown}).kind)}'`)
        }
    })
    const items = upstreamBatchItems(choices, Number(variations), {
        style: (context.style ?? 'low-poly') as StyleProfileId,
        appVersion: APP_VERSION,
        readInSavedProp: () => Promise.reject(new Error('EasyProps: batchItems gives names only')),
    })
    return JSON.stringify(items.map((item) => ({name: item.name, folder: item.name, exportName: item.name})))
}

/** 13b. The names a typed name makes: `{folder: folderNameOf(name), export: exportNameOf(name)}`. */
function exportNames(name: string): string {
    return JSON.stringify({folder: folderNameOf(String(name)), export: exportNameOf(String(name))})
}

/** 14a. The eight seeds of a page of Show me more. */
function pageSeeds(seed: number, page: number): string {
    return JSON.stringify(upstreamPageSeeds(Number(seed), Number(page)))
}

/** 14b. A prop's damage settings for these values (their number specs), `[]` for one that cannot be damaged. */
function damageSettings(generatorId: string, valuesJson: string): string {
    return JSON.stringify(upstreamDamageSettings(String(generatorId), parse<Values>(valuesJson, 'values')))
}

/** 14c. The eight damaged versions of a page, on the prop's own seed. `settings` is damageSettings' answer. */
function pageDamages(seed: number, page: number, settingsJson: string): string {
    return JSON.stringify(upstreamPageDamages(Number(seed), Number(page), parse<NumberParameter[]>(settingsJson, 'settings')))
}

/** 14d. What a damaged version has, in its settings' words ("Snapped top, Split trunk"), as a JSON string. */
function damageNames(versionJson: string, settingsJson: string): string {
    return JSON.stringify(upstreamDamageNames(parse<PropVersion>(versionJson, 'version'), parse<NumberParameter[]>(settingsJson, 'settings')))
}

/** 14e. A version's name that is the same each time, as a JSON string. */
function versionKey(versionJson: string): string {
    return JSON.stringify(upstreamVersionKey(parse<PropVersion>(versionJson, 'version')))
}

/** 14f. The prop in one version: on its seed, damaged as it says (upstream's versionScene). */
function versionScene(sceneJson: string, versionJson: string, style: string): string {
    return JSON.stringify(sceneOut(upstreamVersionScene(sceneOf(sceneJson), parse<PropVersion>(versionJson, 'version'), String(style) as StyleProfileId)))
}

/** 15a. Upstream's names and help for each piece: `{stump: {name, help}, log, branch, litter}`. */
function pieceLabels(): string {
    return JSON.stringify(PIECE_LABELS)
}

/** 15b. The pieces this tree can make (`[]` for anything but a painted tree). Plans the tree: heavy. */
function piecesOffered(sceneJson: string): string {
    return JSON.stringify(upstreamPiecesOffered(sceneOf(sceneJson)))
}

/** 15c. The pieces of the tree, each as a prop's scene: `[{kind, name, scene}]`. The host asks one kind at a time. */
function makePieces(sceneJson: string, treeName: string, kindsJson: string, style: string): string {
    const kinds = parse<PieceKind[]>(kindsJson, 'kinds')
    const made = upstreamMakePieces(sceneOf(sceneJson), String(treeName), kinds, String(style) as StyleProfileId)
    return JSON.stringify(made.map((piece) => ({kind: piece.kind, name: piece.name, scene: sceneOut(piece.scene)})))
}

/** 16a. The colour settings a set of props shares (`props` is `[{generatorId, parameters}]`). */
function colourGroups(propsJson: string): string {
    return JSON.stringify(upstreamColourGroups(parse<SetProp[]>(propsJson, 'props')))
}

/** 16b. The set's starting colours: each group's usual choice, by group id. */
function usualColours(groupsJson: string): string {
    return JSON.stringify(upstreamUsualColours(parse<ColourGroup[]>(groupsJson, 'groups')))
}

/** 16c. The prop in the set's colours (made again with its colour settings changed). */
function inSetColours(sceneJson: string, coloursJson: string, style: string): string {
    return JSON.stringify(sceneOut(upstreamInSetColours(sceneOf(sceneJson), parse<SetColours>(coloursJson, 'colours'), String(style) as StyleProfileId)))
}

/** A GLB holding only its JSON chunk: all upstream's materialsInGlb reads. */
function glbOfJson(text: string): Uint8Array {
    const body = new TextEncoder().encode(text)
    const padded = (body.length + 3) & ~3
    const bytes = new Uint8Array(20 + padded).fill(0x20, 20)
    const view = new DataView(bytes.buffer)
    view.setUint32(0, 0x46546c67, true)
    view.setUint32(4, 2, true)
    view.setUint32(8, bytes.length, true)
    view.setUint32(12, padded, true)
    view.setUint32(16, 0x4e4f534a, true)
    bytes.set(body, 20)
    return bytes
}

/**
 * 16d. The list a set ends with (upstream's setEntryOf and setListFile): `{name, text}`. `entries` is
 * `[{name, files: [{name, json?}]}]`, one per prop, where `json` is a GLB's JSON chunk as text (the materials are
 * read from it as upstream reads them from the file).
 */
function setList(name: string, entriesJson: string, groupsJson: string, coloursJson: string): string {
    const entries = parse<{name: string; files: {name: string; json?: string}[]}[]>(entriesJson, 'entries').map((entry) =>
        setEntryOf(
            String(entry.name),
            entry.files.map((file) => ({name: String(file.name), bytes: typeof file.json === 'string' ? glbOfJson(file.json) : new Uint8Array(0)})),
        ),
    )
    const file = setListFile(String(name), entries, parse<ColourGroup[]>(groupsJson, 'groups'), parse<SetColours>(coloursJson, 'colours'))
    return JSON.stringify({name: file.name, text: new TextDecoder().decode(file.bytes)})
}

export {
    adjust,
    batchItems,
    build,
    collision,
    colourGroups,
    components,
    countFaces,
    damageNames,
    damageSettings,
    earlierLooks,
    earlierLooksOf,
    exportNames,
    exportPackage,
    fitControls,
    info,
    inSetColours,
    makeItFit,
    makePieces,
    mergeGenerated,
    pageDamages,
    pageSeeds,
    parameters,
    parseEdits,
    partFacts,
    pieceLabels,
    piecesOffered,
    pivot,
    preset,
    presets,
    sceneOp,
    setList,
    shown,
    texture,
    traits,
    usualColours,
    validate,
    versionKey,
    versionScene,
}
