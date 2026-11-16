// Smoke check of the engine bundle: loads shims.js and engine.js into a bare V8 context (node:vm, no browser
// globals, none of Node's), then builds every preset at three detail levels, exports two props, and checks the
// rules calls, the texture keys, cancelling and that no browser global beyond the shims is reached.
//
//   node tools/engine/smoke.mjs --engine <engine.js> --shims <shims.js> [--tag v0.4.2] [--only oak,boulder]
//                               [--style low-poly] [--levels 1,0.4,0.12] [--no-export] [--easy-props <checkout>]
//
// `--easy-props` is the clone build.sh bundled (default ~/.cache/prop-generator/easy-props-<tag>): the scene calls
// are checked against upstream's own functions, bundled from it into a second context in the same run.
// Exit code 0 when every check passes, 1 otherwise.
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import zlib from 'node:zlib'

const args = process.argv.slice(2)
const arg = (name, fallback) => {
    const at = args.indexOf(`--${name}`)
    return at >= 0 ? args[at + 1] : fallback
}
const here = path.dirname(new URL(import.meta.url).pathname)
const resources = path.resolve(here, '../../src/PropGenerator.Engine/Resources')
const enginePath = arg('engine', path.join(resources, 'engine.js'))
const shimsPath = arg('shims', path.join(here, 'shims.js'))
const wantTag = arg('tag', null)
const only = arg('only', null)?.split(',')
const style = arg('style', 'low-poly')
const levels = arg('levels', '1,0.4,0.12').split(',').map(Number)
const doExport = !args.includes('--no-export')
const easyPropsArg = arg('easy-props', null)
const JOB_MS = 60_000

const failures = []
const fail = (what) => {
    failures.push(what)
    console.log(`FAIL ${what}`)
}
const check = (ok, what) => {
    if (!ok) fail(what)
    return ok
}

// ---- the context: only the host object, then the shims ----

const context = vm.createContext({})
const inner = (name) => vm.runInContext(name, context)
const InnerUint8Array = inner('Uint8Array')
const cancelledTokens = new Set()
const logs = []
const host = {
    log(level, text) {
        logs.push(`${level}: ${text}`)
        if (level === 'error' || level === 'warn') console.log(`  [${level}] ${String(text).slice(0, 300)}`)
    },
    cancelled: (token) => cancelledTokens.has(token),
    png(rgba, w, h) {
        // PNG, colour type 6, filter 0 on every row, zlib deflate.
        const src = Buffer.from(rgba.buffer, rgba.byteOffset, rgba.byteLength)
        const raw = Buffer.alloc((w * 4 + 1) * h)
        for (let y = 0; y < h; y++) src.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4)
        const chunk = (type, data) => {
            const length = Buffer.alloc(4)
            length.writeUInt32BE(data.length)
            const body = Buffer.concat([Buffer.from(type, 'latin1'), data])
            const crc = Buffer.alloc(4)
            crc.writeUInt32BE(zlib.crc32(body) >>> 0)
            return Buffer.concat([length, body, crc])
        }
        const header = Buffer.alloc(13)
        header.writeUInt32BE(w, 0)
        header.writeUInt32BE(h, 4)
        header[8] = 8
        header[9] = 6
        const png = Buffer.concat([
            Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
            chunk('IHDR', header),
            chunk('IDAT', zlib.deflateSync(raw)),
            chunk('IEND', Buffer.alloc(0)),
        ])
        const out = new InnerUint8Array(png.length)
        out.set(png)
        return out
    },
}
context.__host = host
vm.runInContext(fs.readFileSync(shimsPath, 'utf8'), context, {filename: 'shims.js'})

// Every browser global the shims do not define reports when read (a typeof check reads it too).
const BROWSER = [
    'window', 'document', 'navigator', 'location', 'Worker', 'SharedWorker', 'localStorage', 'sessionStorage', 'indexedDB',
    'fetch', 'XMLHttpRequest', 'URL', 'URLSearchParams', 'crypto', 'structuredClone', 'atob', 'btoa', 'requestIdleCallback',
    'createImageBitmap', 'ImageBitmap', 'HTMLCanvasElement', 'HTMLImageElement', 'HTMLVideoElement', 'VideoFrame',
    'WebGLRenderingContext', 'WebGL2RenderingContext', 'AbortController', 'Event', 'EventTarget', 'process', 'require',
    'module', 'exports', 'Buffer', 'global', 'setImmediate', '__THREE_DEVTOOLS__',
]
// Read only behind `typeof` (or in a try) by the code we run, so reading them is expected: three's devtools hook and
// window check, zod's navigator check, GLTFExporter's canvas and image checks, bark-pool's Worker check, the far
// painter's document check.
const GUARDED = new Set(['window', 'document', 'navigator', 'Worker', '__THREE_DEVTOOLS__', 'HTMLImageElement', 'HTMLCanvasElement', 'ImageBitmap', 'VideoFrame'])
const touched = new Map()
const global = inner('globalThis')
for (const name of BROWSER) {
    if (Object.prototype.hasOwnProperty.call(global, name)) continue
    Object.defineProperty(global, name, {
        configurable: true,
        get() {
            touched.set(name, (touched.get(name) ?? 0) + 1)
            return undefined
        },
    })
}

const t0 = Date.now()
vm.runInContext(fs.readFileSync(enginePath, 'utf8'), context, {filename: 'engine.js'})
const E = inner('EasyProps')
console.log(`loaded ${path.basename(enginePath)} (${fs.statSync(enginePath).size} bytes) in ${Date.now() - t0} ms`)

// ---- helpers ----

let peakRss = 0
const sample = () => {
    peakRss = Math.max(peakRss, process.memoryUsage().rss)
}

/** Waits for an engine promise, draining the shims' timers as the host does. */
async function settle(promise, what) {
    let done = false
    let value
    let error
    promise.then(
        (v) => {
            done = true
            value = v
        },
        (e) => {
            done = true
            error = e
        },
    )
    const start = Date.now()
    while (!done) {
        inner('__runTimers')()
        await new Promise((resolve) => setImmediate(resolve))
        if (Date.now() - start > JOB_MS) throw new Error(`${what}: no answer after ${JOB_MS} ms`)
    }
    sample()
    if (error) throw error
    return value
}

const isView = (a) => ArrayBuffer.isView(a)
const typeName = (a) => Object.prototype.toString.call(a).slice(8, -1)

/** Checks one build result; returns its texture keys, face count and a digest of its bytes. */
function checkMesh(result, what) {
    const meta = JSON.parse(result.meta)
    const arrays = result.arrays
    const keys = new Set()
    let faces = 0
    const hash = zlib.crc32
    let digest = 0
    check(Array.isArray(meta.parts), `${what}: meta.parts`)
    for (const [i, part] of meta.parts.entries()) {
        const where = `${what} part ${i} (${part.generatorId}/${part.material})`
        check(typeof part.componentId === 'string', `${where}: componentId`)
        check(part.matrix.length === 16 && part.matrix.every(Number.isFinite), `${where}: matrix`)
        check(part.tint.length === 3 && part.tint.every(Number.isFinite), `${where}: tint`)
        check(Number.isFinite(part.growth) && part.growth > 0, `${where}: growth`)
        const position = part.attributes.find((a) => a.name === 'position')
        if (!check(position && position.itemSize === 3, `${where}: position`)) continue
        for (const attribute of part.attributes) {
            const array = arrays[attribute.array]
            check(isView(array) && typeName(array) === 'Float32Array', `${where}: ${attribute.name} is ${typeName(array)}`)
            check(array.length === part.vertices * attribute.itemSize, `${where}: ${attribute.name} length ${array.length} for ${part.vertices} x ${attribute.itemSize}`)
            let finite = true
            for (let k = 0; k < array.length; k++) if (!Number.isFinite(array[k])) finite = false
            check(finite, `${where}: ${attribute.name} has a value that is not finite`)
            digest = hash(Buffer.from(array.buffer, array.byteOffset, array.byteLength), digest)
        }
        if (part.index) {
            const index = arrays[part.index.array]
            check(typeName(index) === (part.index.type === 'u16' ? 'Uint16Array' : 'Uint32Array'), `${where}: index type`)
            check(index.length % 3 === 0, `${where}: index length ${index.length}`)
            let max = 0
            for (let k = 0; k < index.length; k++) if (index[k] > max) max = index[k]
            check(index.length === 0 || max < part.vertices, `${where}: index ${max} past ${part.vertices} vertices`)
            digest = hash(Buffer.from(index.buffer, index.byteOffset, index.byteLength), digest)
        } else {
            check(part.vertices % 3 === 0, `${where}: unindexed ${part.vertices} vertices`)
        }
        check(part.faces === Math.floor((part.index ? arrays[part.index.array].length : part.vertices) / 3), `${where}: faces`)
        if (!part.proxy) faces += part.faces
        if (part.surface?.map) keys.add(part.surface.map)
        if (part.surface?.normalMap) keys.add(part.surface.normalMap)
    }
    return {meta, keys, faces, digest}
}

const json = (text) => JSON.parse(text)

// ---- info, presets, rules ----

const info = json(E.info())
console.log(`info: tag ${info.tag} commit ${String(info.commit).slice(0, 7)} app ${info.appVersion}, ${info.generators.length} generators`)
if (wantTag) check(info.tag === wantTag, `info.tag ${info.tag} is not ${wantTag}`)
check(info.levelShares.join() === '1,0.4,0.12' && info.farLevel === 3, 'info: levels')
check(info.budgets.length > 0, 'info: budgets')
check(info.maxVariations === 50, 'info: maxVariations (upstream MAX_VARIATIONS)')

const {categories, presets} = json(E.presets())
console.log(`presets: ${presets.length} in ${categories.length} categories, ${presets.filter((p) => p.thumbnailFresh).length} with fresh thumbnails`)
check(presets.length > 0, 'presets: none')
check(presets.some((p) => p.id === 'round-canopy'), 'presets: round-canopy missing')
if (wantTag === 'v0.4.2') check(presets.length === 101, `presets: ${presets.length}, 0.4.2 has 101`)
const retired = json(E.preset('wide-canopy'))
check(retired && retired.retired === true, 'preset(wide-canopy) is retired')
check(json(E.preset('no-such-preset')) === null, 'preset(unknown) is null')

for (const generator of info.generators) {
    const first = presets.find((p) => p.generatorId === generator.id)
    const values = JSON.stringify(first ? first.parameters : {})
    const parameters = json(E.parameters(generator.id))
    check(parameters.specs.length > 0, `parameters(${generator.id}): no specs`)
    check(Array.isArray(parameters.colourRows) && (generator.id === 'painted-tree') === (parameters.colourRows.length === 5), `parameters(${generator.id}): colour rows`)
    const rows = json(E.shown(generator.id, values))
    check(rows.length > 0 && rows.every((r) => typeof r.key === 'string' && typeof r.label === 'string'), `shown(${generator.id})`)
    const validated = json(E.validate(generator.id, values))
    check(validated.valid === true, `validate(${generator.id}) of its first preset`)
    const traits = json(E.traits(generator.id, values))
    check(typeof traits.farAway === 'boolean' && traits.sways === !generator.still, `traits(${generator.id})`)
}
{
    const oak = presets.find((p) => p.id === 'oak')
    const patch = json(E.adjust('painted-tree', JSON.stringify(oak.parameters), JSON.stringify({...oak.parameters, palette: 'custom'})))
    check(Object.keys(patch).sort().join() === 'barkDeep,barkLight,leafDeep,leafLight,leafMid', `adjust(painted-tree) to custom gives ${JSON.stringify(patch)}`)
    const shownCustom = json(E.shown('painted-tree', JSON.stringify({...oak.parameters, palette: 'custom'})))
    check(shownCustom.some((r) => r.key === 'leafMid'), 'shown(painted-tree, custom) has the colour rows')
    const boulder = presets.find((p) => p.id === 'boulder')
    const rockPatch = json(E.adjust('cut-rock', JSON.stringify(boulder.parameters), JSON.stringify({...boulder.parameters, palette: 'custom'})))
    check(Object.keys(rockPatch).length > 0, `adjust(cut-rock) to custom gives ${JSON.stringify(rockPatch)}`)
    console.log(`adjust: painted-tree ${JSON.stringify(patch)}; cut-rock ${JSON.stringify(rockPatch)}`)
}

// ---- components: the compact form builds the same parts ----

for (const id of ['oak', 'low-hedge']) {
    const p = presets.find((x) => x.id === id)
    const compact = E.components(p.generatorId, JSON.stringify(p.parameters), p.seed)
    check(compact.includes('"$asset"'), `components(${id}) is compact`)
    const plain = {generatorId: p.generatorId, seed: p.seed, parameters: p.parameters}
    const a = checkMesh(await settle(E.build(JSON.stringify(plain), style, 1), id), `${id} plain`)
    const b = checkMesh(await settle(E.build(JSON.stringify({...plain, components: JSON.parse(compact)}), style, 1), id), `${id} compact`)
    check(a.digest === b.digest && a.faces === b.faces, `components(${id}): the compact components build other parts`)
    console.log(`components: ${id} ${JSON.parse(compact).length} components, ${compact.length} chars; builds alike`)
}

// ---- cancelling ----

{
    const p = presets.find((x) => x.id === 'oak')
    cancelledTokens.add(99)
    let rejected = null
    try {
        await settle(E.build(JSON.stringify({generatorId: p.generatorId, seed: p.seed, parameters: p.parameters}), style, 1, null, 99), 'cancel')
    } catch (e) {
        rejected = String(e && e.message)
    }
    check(rejected !== null && rejected.includes('cancelled'), `a cancelled build rejects (got ${rejected})`)
    console.log(`cancel: ${rejected}`)
}

// ---- every preset at every level ----

const textureKeys = new Set()
const slow = []
const subjects = only ? presets.filter((p) => only.includes(p.id)) : presets
const fullFaces = {}
for (const p of subjects) {
    const scene = JSON.stringify({generatorId: p.generatorId, seed: p.seed, parameters: p.parameters})
    const line = []
    for (const level of levels) {
        const start = Date.now()
        try {
            const result = await settle(E.build(scene, style, level, level === 1 ? JSON.stringify({fullFaces: true}) : undefined), `${p.id}@${level}`)
            const ms = Date.now() - start
            const {meta, keys, faces} = checkMesh(result, `${p.id}@${level}`)
            for (const key of keys) textureKeys.add(key)
            check(meta.parts.length > 0, `${p.id}@${level}: no parts`)
            if (level === 1) {
                check(meta.fullFaces === faces, `${p.id}: fullFaces ${meta.fullFaces} is not ${faces}`)
                fullFaces[p.id] = faces
            }
            line.push(`${level}: ${meta.parts.length} parts ${faces} faces ${ms} ms`)
            if (ms > 20_000) slow.push(`${p.id}@${level} ${ms} ms`)
        } catch (e) {
            fail(`${p.id}@${level}: ${e && e.stack ? e.stack.split('\n').slice(0, 4).join(' | ') : e}`)
        }
    }
    console.log(`${p.generatorId.padEnd(14)} ${p.id.padEnd(26)} ${line.join(' | ')}`)
}

// ---- textures ----

for (const key of [...textureKeys, 'white']) {
    const result = E.texture(key)
    const meta = JSON.parse(result.meta)
    const data = result.arrays[0]
    check(typeName(data) === 'Uint8Array' && data.length === meta.w * meta.h * 4, `texture(${key}): ${meta.w} x ${meta.h}, ${data.length} bytes`)
}
console.log(`textures: ${[...textureKeys].sort().join(', ')}`)
let unknownTexture = null
try {
    E.texture('nope')
} catch (e) {
    unknownTexture = String(e.message)
}
check(unknownTexture !== null, 'texture(unknown) throws')

// ---- collision, pivot and exports ----

const glbJson = (bytes) => {
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
    if (view.getUint32(0, true) !== 0x46546c67 || view.getUint32(4, true) !== 2 || view.getUint32(8, true) !== bytes.length) return null
    const length = view.getUint32(12, true)
    const doc = JSON.parse(Buffer.from(bytes.buffer, bytes.byteOffset + 20, length).toString('utf8'))
    const bin = 20 + length + 8
    return {doc, bin}
}
if (doExport) {
    for (const id of ['oak', 'boulder']) {
        const p = presets.find((x) => x.id === id)
        const scene = JSON.stringify({generatorId: p.generatorId, seed: p.seed, parameters: p.parameters})
        const shapes = json(await settle(E.collision(scene, style), `${id} collision`))
        const pivot = json(E.pivot(scene))
        check(Array.isArray(shapes.offered) && shapes.shapes.length > 0, `collision(${id})`)
        check(pivot.length === 3 && pivot.every(Number.isFinite), `pivot(${id})`)
        const settings = {name: id, engine: 'godot', levels: 3, collision: true, wind: true, far: true, look: 'lit', materials: true}
        const start = Date.now()
        let files = []
        try {
            files = await settle(E.exportPackage(scene, style, JSON.stringify(settings)), `${id} export`)
        } catch (e) {
            fail(`export ${id}: ${e && e.stack ? e.stack.split('\n').slice(0, 4).join(' | ') : e}`)
            continue
        }
        const ms = Date.now() - start
        const names = files.map((f) => f.name)
        for (const level of [0, 1, 2]) check(names.includes(`${id}_LOD${level}.glb`), `export ${id}: ${id}_LOD${level}.glb`)
        check(names.includes(`${id}.asset.json`), `export ${id}: asset.json`)
        let images = 0
        for (const file of files) {
            check(typeName(file.bytes) === 'Uint8Array' && file.bytes.length > 0, `export ${id}: ${file.name} bytes`)
            if (!file.name.endsWith('.glb')) continue
            const glb = glbJson(file.bytes)
            if (!check(glb !== null, `export ${id}: ${file.name} is a GLB`)) continue
            for (const image of glb.doc.images ?? []) {
                const view = glb.doc.bufferViews[image.bufferView]
                const start = glb.bin + (view.byteOffset ?? 0)
                check(file.bytes[start] === 137 && file.bytes[start + 1] === 80, `export ${id}: ${file.name} image is a PNG`)
                images++
            }
        }
        if (id === 'oak') check(images > 0, 'export oak: no images')
        const asset = JSON.parse(Buffer.from(files.find((f) => f.name === `${id}.asset.json`).bytes).toString('utf8'))
        console.log(`export ${id}: ${ms} ms, ${files.length} files: ${files.map((f) => `${f.name} ${f.bytes.length}${f.faces != null ? `/${f.faces}f` : ''}`).join(', ')}; ${images} images; collision offered ${shapes.offered.length}, shown ${shapes.shapes.length}; asset generator ${asset.generator ?? JSON.stringify(Object.keys(asset))}`)
    }
}

// ---- phase 3: scene operations, earlier looks, fitting, versions, pieces, sets ----

/** Upstream's own functions, bundled from the clone into a context of their own, to check ours against. */
async function upstreamReference() {
    const home = process.env.HOME ?? ''
    const checkout = easyPropsArg ?? path.join(home, '.cache/prop-generator', `easy-props-${info.tag}`)
    const frontend = path.join(checkout, 'frontend')
    if (!fs.existsSync(path.join(frontend, 'node_modules/vite/package.json'))) {
        return {missing: checkout}
    }
    const {createRequire} = await import('node:module')
    const fromVite = createRequire(fs.realpathSync(path.join(frontend, 'node_modules/vite/package.json')))
    const esbuild = await import(fromVite.resolve('esbuild'))
    const src = path.join(frontend, 'src')
    const rawText = {
        name: 'raw',
        setup(build) {
            build.onResolve({filter: /\?raw$/}, (a) => ({path: path.resolve(a.resolveDir, a.path.slice(0, -'?raw'.length)), namespace: 'raw'}))
            build.onLoad({filter: /.*/, namespace: 'raw'}, (a) => ({contents: fs.readFileSync(a.path, 'utf8'), loader: 'text'}))
        },
    }
    const contents = [
        "export {copyComponent, removeComponent, restoreComponentDefault, setComponentPosition, updateComponentTransform, updateComponentParameters, breakComponent, updateTreeParameters, setAssetSeed, mergeGenerated} from './core/scene/scene-ops'",
        "export {generatePropComponents, findPropGenerator} from './assets/prop-generators'",
        "export {getStyleProfile} from './assets/style/style-profiles'",
        "export {DEFAULT_MATERIALS} from './assets/style/materials'",
    ].join('\n')
    const result = await esbuild.build({
        stdin: {contents, resolveDir: src, loader: 'ts', sourcefile: 'reference.ts'},
        absWorkingDir: checkout,
        bundle: true,
        format: 'iife',
        globalName: 'Upstream',
        platform: 'neutral',
        mainFields: ['module', 'main'],
        target: 'es2022',
        nodePaths: [path.join(frontend, 'node_modules')],
        define: {'import.meta.url': '"file:///easy-props/reference.js"'},
        write: false,
        plugins: [rawText],
        logLevel: 'error',
    })
    const ref = vm.createContext({})
    ref.__host = host
    vm.runInContext(fs.readFileSync(shimsPath, 'utf8'), ref, {filename: 'shims.js'})
    vm.runInContext(result.outputFiles[0].text, ref, {filename: 'reference.js'})
    return {upstream: vm.runInContext('Upstream', ref), checkout}
}

/** Runs one phase 3 check; a throw (a call the bundle lacks, say) fails it instead of stopping the smoke. */
async function phase3(what, body) {
    const start = Date.now()
    try {
        const note = await body()
        console.log(`${what}: ${note ?? 'ok'} (${Date.now() - start} ms)`)
    } catch (e) {
        fail(`${what}: ${e && e.stack ? e.stack.split('\n').slice(0, 4).join(' | ') : e}`)
    }
}

/** A scene as the engine returns it, with "$asset" put back, as upstream's asset would hold it. */
function expanded(scene) {
    const validated = json(E.validate(scene.generatorId, JSON.stringify(scene.parameters))).parameters
    return {
        seed: scene.seed,
        parameters: scene.parameters,
        components: scene.components.map((c) => ({
            ...c,
            parameters: Object.fromEntries(Object.entries(c.parameters).map(([k, v]) => [k, v === '$asset' ? validated : v])),
        })),
    }
}
const ofAsset = (asset) => ({seed: asset.seed, parameters: asset.parameters, components: asset.components})
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b)
const presetScene = (id) => {
    const p = presets.find((x) => x.id === id)
    return {generatorId: p.generatorId, seed: p.seed, parameters: p.parameters}
}

const reference = await upstreamReference()
const U = reference.upstream
if (!U) fail(`no easy-props checkout with node_modules at ${reference.missing}: the scene calls cannot be checked against upstream`)

await phase3('sceneOp on oak equals upstream', async () => {
    const oak = presetScene('oak')
    const compact = json(E.components(oak.generatorId, JSON.stringify(oak.parameters), oak.seed))
    const heavy = compact.find((c) => c.generatorId === 'painted-limb' && /^(l\d+|c\d+s\d+)$/.test(String(c.parameters.key)))
    const clump = compact.find((c) => c.generatorId === 'painted-clump')
    check(heavy && clump, 'oak has a heavy limb and a clump')
    const upstreamScene = () => ({
        id: 'scene-1',
        assets: [
            {
                id: 'asset-1',
                kind: 'tree',
                generatorId: oak.generatorId,
                generatorVersion: 1,
                components: U.generatePropComponents(oak.generatorId, oak.parameters, oak.seed, U.getStyleProfile('low-poly')),
                seed: oak.seed,
                parameters: oak.parameters,
                materials: U.DEFAULT_MATERIALS,
            },
        ],
    })
    const clumpPosition = clump.transform.position.map((v, i) => v + (i === 0 ? 0.25 : 0))
    const cases = [
        ['turn a limb', {kind: 'part', componentId: heavy.id, action: {kind: 'patch', patch: {turn: [0, 20, 0]}}}, (s) => U.updateComponentParameters(s, heavy.id, {turn: [0, 20, 0]})],
        ['move a clump', {kind: 'position', componentId: clump.id, position: clumpPosition}, (s) => U.setComponentPosition(s, clump.id, clumpPosition)],
        ['break a heavy limb', {kind: 'break', componentId: heavy.id}, (s) => U.breakComponent(s, heavy.id)],
        ['remove a clump', {kind: 'remove', componentId: clump.id}, (s) => U.removeComponent(s, clump.id)],
        ['restore a limb', {kind: 'restore', componentId: heavy.id}, (s) => U.restoreComponentDefault(s, heavy.id)],
        ['tree', {kind: 'tree', patch: {height: 9}}, (s) => U.updateTreeParameters(s, 'asset-1', {height: 9}, U.getStyleProfile(style))],
        ['seed', {kind: 'seed', seed: 777}, (s) => U.setAssetSeed(s, 'asset-1', 777, U.getStyleProfile(style))],
    ]
    const notes = []
    for (const [what, op, theirs] of cases) {
        const out = json(E.sceneOp(JSON.stringify(oak), JSON.stringify(op), style))
        if (!check(out !== null, `sceneOp ${what}: refused`)) continue
        if (U) check(same(expanded(out), ofAsset(theirs(upstreamScene()).assets[0])), `sceneOp ${what}: differs from upstream`)
        notes.push(what)
    }
    // Broken, then edited again: the break stays.
    const broken = json(E.sceneOp(JSON.stringify(oak), JSON.stringify({kind: 'break', componentId: heavy.id}), style))
    // (The clumps on the broken limb went with it: the edit is to one that is left.)
    const left = broken.components.find((c) => c.generatorId === 'painted-clump')
    const after = json(E.sceneOp(JSON.stringify(broken), JSON.stringify({kind: 'part', componentId: left.id, action: {kind: 'patch', patch: {sizeScale: 1.2}}}), style))
    check(after && json(E.parseEdits(after.parameters.edits)).limbs[heavy.parameters.key]?.broken === true, 'a broken limb survives the next edit')
    // Refused: a painted limb cannot be moved or copied, and a second break of the same limb does nothing.
    check(json(E.sceneOp(JSON.stringify(oak), JSON.stringify({kind: 'position', componentId: heavy.id, position: [0, 1, 0]}), style)) === null, 'moving a painted limb is refused')
    check(json(E.sceneOp(JSON.stringify(oak), JSON.stringify({kind: 'copy', componentId: heavy.id}), style)) === null, 'copying a painted part is refused')
    check(json(E.sceneOp(JSON.stringify(oak), JSON.stringify({kind: 'remove', componentId: 'no-such-part'}), style)) === null, 'an op on a missing part is refused')
    const facts = json(E.partFacts(JSON.stringify(oak), heavy.id))
    check(facts.painted === true && facts.heavy === true && facts.canBreak === true, `partFacts(heavy limb) ${JSON.stringify(facts)}`)
    check(json(E.partFacts(JSON.stringify(broken), heavy.id)).canBreak === false, 'partFacts: a broken limb cannot break again')
    check(json(E.partFacts(JSON.stringify(oak), clump.id)).heavy === false, 'partFacts: a clump is not heavy')
    const edits = json(E.parseEdits(broken.parameters.edits))
    check(typeof edits.limbs === 'object' && typeof edits.clumps === 'object', 'parseEdits')
    check(same(json(E.parseEdits('not json')), {limbs: {}, clumps: {}}), 'parseEdits of a damaged list is empty')
    return notes.join(', ')
})

await phase3('sceneOp on a boulder: copy, transform, parameters', async () => {
    const boulder = presetScene('boulder')
    const compact = json(E.components(boulder.generatorId, JSON.stringify(boulder.parameters), boulder.seed))
    const first = compact[0]
    const copied = json(E.sceneOp(JSON.stringify(boulder), JSON.stringify({kind: 'copy', componentId: first.id}), style))
    check(copied && copied.components.length > compact.length && copied.components.at(-1).slot === null, 'copy adds a part of the user')
    const named = json(E.sceneOp(JSON.stringify(boulder), JSON.stringify({kind: 'copy', componentId: first.id, ids: ['mine-1']}), style))
    check(named && named.components.some((c) => c.id === 'mine-1'), 'copy takes the ids given')
    const transform = {position: [1, 0, 0], rotation: [0, 0.5, 0], scale: [1, 1, 1]}
    const moved = json(E.sceneOp(JSON.stringify(boulder), JSON.stringify({kind: 'transform', componentId: first.id, transform}), style))
    check(moved && same(moved.components[0].transform, transform), 'transform')
    const patched = json(E.sceneOp(JSON.stringify(boulder), JSON.stringify({kind: 'parameters', componentId: first.id, patch: {seed: 5}}), style))
    check(patched && patched.components[0].edited === true, 'parameters marks the part edited')
})

await phase3('mergeGenerated equals upstream', async () => {
    const oak = presetScene('oak')
    const generated = json(E.components(oak.generatorId, JSON.stringify(oak.parameters), oak.seed))
    const fresh = json(E.components(oak.generatorId, JSON.stringify({...oak.parameters, height: 9}), oak.seed))
    const [movedPart, removedPart, copiedPart] = [generated[3], generated[4], generated[5]]
    const kept = generated
        .filter((c) => c.id !== removedPart.id)
        .map((c) => (c.id === movedPart.id ? {...c, moved: [0.5, 0, 0], transform: {...c.transform, position: c.transform.position.map((v, i) => v + (i === 0 ? 0.5 : 0))}} : c))
    kept.push({...copiedPart, id: 'man-copy-1', slot: null, edited: true})
    const ours = json(E.mergeGenerated(JSON.stringify(kept), JSON.stringify(fresh)))
    check(ours.some((c) => c.id === 'man-copy-1') && ours.length === fresh.length + 1, 'mergeGenerated keeps the copy')
    if (U) check(same(ours, U.mergeGenerated(kept, fresh)), 'mergeGenerated differs from upstream')
})

await phase3('earlier looks', async () => {
    const boulder = presetScene('boulder')
    const seven = ['grain', 'rounded', 'blend', 'dents', 'layers', 'overhang', 'lichen']
    const without = (values) => Object.fromEntries(Object.entries(values).filter(([k]) => !seven.includes(k)))
    const validated = json(E.validate(boulder.generatorId, JSON.stringify(boulder.parameters))).parameters
    const components = json(E.components(boulder.generatorId, JSON.stringify(boulder.parameters), boulder.seed)).map((c) => ({
        ...c,
        parameters: Object.fromEntries(Object.entries(c.parameters).map(([k, v]) => [k, v === '$asset' ? without(validated) : v])),
    }))
    const old = {...boulder, parameters: without(boulder.parameters), components}
    const filled = json(E.earlierLooks(JSON.stringify(old)))
    check(seven.every((k) => filled.parameters[k] === 0), `earlierLooks: asset ${JSON.stringify(filled.parameters)}`)
    const rocks = filled.components.filter((c) => c.parameters.rock && typeof c.parameters.rock === 'object')
    check(rocks.length > 0 && rocks.every((c) => seven.every((k) => c.parameters.rock[k] === 0)), 'earlierLooks: components[].parameters.rock')
    const kept = json(E.earlierLooks(JSON.stringify({...boulder, parameters: {...boulder.parameters, grain: 0.6}})))
    check(kept.parameters.grain === 0.6, 'earlierLooks keeps a grain the file has')
    const probe = json(E.earlierLooksOf('cut-rock'))
    const filledAsset = Object.fromEntries(seven.map((k) => [k, filled.parameters[k]]))
    check(same(probe.asset, filledAsset) && same(probe.rock, filledAsset), `earlierLooksOf(cut-rock) ${JSON.stringify(probe)}`)
    for (const generator of info.generators.filter((g) => g.id !== 'cut-rock')) {
        const other = json(E.earlierLooksOf(generator.id))
        check(same(other, {asset: {}, rock: {}}), `earlierLooksOf(${generator.id}) is ${JSON.stringify(other)}`)
    }
    return JSON.stringify(probe.asset)
})

await phase3('make it fit', async () => {
    const oak = presetScene('oak')
    const counts = json(await settle(E.countFaces(JSON.stringify(oak), style), 'countFaces'))
    const total = counts.wood + counts.leaves
    check(total > 0 && Number.isFinite(counts.wood) && Number.isFinite(counts.leaves), `countFaces ${JSON.stringify(counts)}`)
    const controls = json(E.fitControls(JSON.stringify(oak)))
    check(controls.length > 0 && controls.every((c) => typeof c.key === 'string' && typeof c.label === 'string' && c.min <= c.max), 'fitControls')
    const budget = Math.round(total * 0.6)
    const fit = json(await settle(E.makeItFit(JSON.stringify(oak), style, budget, JSON.stringify({deadlineMs: null})), 'makeItFit'))
    check(fit.counts.fits === true && fit.counts.faces <= budget && fit.changes.length > 0 && fit.description.length > 0, `makeItFit ${JSON.stringify({...fit, scene: undefined})}`)
    const recount = json(await settle(E.countFaces(JSON.stringify(fit.scene), style), 'countFaces after'))
    check(recount.wood + recount.leaves <= budget, `the fitted oak has ${recount.wood + recount.leaves} faces, over ${budget}`)
    return `${total} -> ${fit.counts.faces} (budget ${budget}): ${fit.description}`
})

await phase3('versions and batch names', async () => {
    const oak = presetScene('oak')
    const seeds = json(E.pageSeeds(oak.seed, 0))
    check(seeds.length === 8 && new Set(seeds).size === 8, 'pageSeeds')
    const settings = json(E.damageSettings('painted-tree', JSON.stringify(oak.parameters)))
    check(settings.length > 0 && settings.every((s) => s.group === 'Damage'), 'damageSettings of oak')
    check(json(E.damageSettings('cut-rock', '{}')).length > 0, 'damageSettings of a rock')
    const damages = json(E.pageDamages(oak.seed, 0, JSON.stringify(settings)))
    check(damages.length === 8 && damages.every((d) => d.seed === oak.seed && d.damage), 'pageDamages')
    check(json(E.damageNames(JSON.stringify(damages[0]), JSON.stringify(settings))).length > 0, 'damageNames')
    check(json(E.versionKey(JSON.stringify({seed: 5}))) === '5', 'versionKey')
    const damaged = json(E.versionScene(JSON.stringify(oak), JSON.stringify(damages[0]), style))
    check(Object.entries(damages[0].damage).every(([k, v]) => damaged.parameters[k] === v), 'versionScene damages')
    const reseeded = json(E.versionScene(JSON.stringify(oak), JSON.stringify({seed: seeds[0]}), style))
    check(reseeded.seed === seeds[0] && reseeded.components.length > 0, 'versionScene reseeds')
    const items = json(E.batchItems(JSON.stringify([{kind: 'versions', name: 'Old oak', versions: [{seed: 1}, {seed: 2}, {seed: 3}]}]), 1, JSON.stringify({style})))
    check(same(items.map((i) => i.name), ['old_oak_01', 'old_oak_02', 'old_oak_03']), `batchItems ${JSON.stringify(items)}`)
    check(items.every((i) => i.scene === undefined && i.exportName === i.name && typeof i.folder === 'string'), 'batchItems are names only')
    const mixed = json(E.batchItems(JSON.stringify([{kind: 'preset', id: 'oak'}, {kind: 'scene', name: 'Old oak'}]), 2, JSON.stringify({style})))
    check(same(mixed.map((i) => i.name), ['old_oak_01', 'old_oak_02', 'old_oak_03', 'old_oak_04']), `batchItems of a preset and a scene ${JSON.stringify(mixed)}`)
    check(same(json(E.exportNames('Old Oak #1')), {folder: 'Old Oak #1', export: 'old_oak_1'}), 'exportNames')
})

const pieceExport = {engine: 'godot', levels: 3, collision: true, wind: true, far: false, look: 'lit', materials: false}

await phase3('pieces', async () => {
    const labels = json(E.pieceLabels())
    check(same(Object.keys(labels), ['stump', 'log', 'branch', 'litter']) && labels.stump.name === 'Stump', 'pieceLabels')
    const notes = []
    const pieceKeys = new Set()
    for (const id of ['oak', 'coconut-palm-painted', 'spruce', 'dead-oak', 'apple']) {
        const tree = presetScene(id)
        const offered = json(E.piecesOffered(JSON.stringify(tree)))
        if (id === 'coconut-palm-painted') check(!offered.includes('branch') && offered.length > 0, `a palm offers ${offered.join()}`)
        if (id === 'oak') check(offered.length === 4, `oak offers ${offered.join()}`)
        for (const kind of offered) {
            const [piece] = json(E.makePieces(JSON.stringify(tree), presets.find((p) => p.id === id).name, JSON.stringify([kind]), style))
            if (!check(piece && piece.kind === kind && piece.scene.generatorId === `tree-${kind}`, `makePieces(${id}, ${kind})`)) continue
            const scene = JSON.stringify(piece.scene)
            const line = []
            for (const level of levels) {
                const {keys, faces} = checkMesh(await settle(E.build(scene, style, level), `${id} ${kind}@${level}`), `${id} ${kind}@${level}`)
                check(faces > 0, `${id} ${kind}@${level}: no faces`)
                for (const key of keys) pieceKeys.add(key)
                line.push(faces)
            }
            if (doExport) {
                const files = await settle(E.exportPackage(scene, style, JSON.stringify({...pieceExport, name: `${id}_${kind}`})), `${id} ${kind} export`)
                check(files.some((f) => f.name === `${id}_${kind}_LOD0.glb`), `export ${id} ${kind}: LOD0`)
                if (kind !== 'litter') {
                    const lod0 = glbJson(files.find((f) => f.name === `${id}_${kind}_LOD0.glb`).bytes)
                    check(lod0.doc.materials.some((m) => m.name === `${id}_${kind}_ends`), `export ${id} ${kind}: no ends mesh`)
                }
            }
            notes.push(`${id} ${kind} ${line.join('/')}`)
        }
    }
    for (const key of pieceKeys) {
        const meta = JSON.parse(E.texture(key).meta)
        check(meta.w > 0, `texture(${key})`)
    }
    check(pieceKeys.has('end-grain') && pieceKeys.has('end-grain-relief'), `piece textures ${[...pieceKeys].join()}`)
    return `${notes.join(', ')}; textures ${[...pieceKeys].sort().join(', ')}`
})

await phase3('texture keys at a size', async () => {
    for (const key of ['stone:0.5:0', 'end-grain', 'end-grain-relief', 'bark:plated:1', 'relief:plated']) {
        const at = JSON.parse(E.texture(`${key}@1024`).meta)
        check(at.w === 1024, `texture(${key}@1024) is ${at.w} wide`)
    }
    const leaf = JSON.parse(E.texture('tree-leaf:lobed@2048').meta)
    check(leaf.w === JSON.parse(E.texture('tree-leaf:lobed').meta).w, 'a picture with no larger size keeps its own')
})

/** The width of a PNG, from its header. */
const pngWidth = (bytes, start) => new DataView(bytes.buffer, bytes.byteOffset + start + 16, 4).getUint32(0)

await phase3('set colours and a set export', async () => {
    const boulders = [presetScene('boulder'), {...presetScene('boulder'), seed: 402}]
    const groups = json(E.colourGroups(JSON.stringify(boulders.map((b) => ({generatorId: b.generatorId, parameters: b.parameters})))))
    check(groups.length > 0 && groups.every((g) => g.props === 2), `colourGroups ${JSON.stringify(groups)}`)
    const trees = json(E.colourGroups(JSON.stringify(['oak', 'beech'].map((id) => ({generatorId: 'painted-tree', parameters: presetScene(id).parameters})))))
    check(trees.some((g) => g.key === 'palette' && g.props === 2), 'two trees share the palette')
    const colours = json(E.usualColours(JSON.stringify(groups)))
    const palette = groups.find((g) => g.key === 'palette')
    const other = palette.options.find((o) => o.value !== palette.usual).value
    const recoloured = json(E.inSetColours(JSON.stringify(boulders[0]), JSON.stringify({...colours, [palette.id]: other}), style))
    check(recoloured.parameters.palette === other, 'inSetColours')
    const setName = json(E.exportNames('My set')).export
    check(setName === 'my_set', `exportNames(My set) is ${setName}`)
    const entries = []
    const materials = []
    for (const [n, boulder] of boulders.entries()) {
        const name = `boulder_0${n + 1}`
        const files = await settle(E.exportPackage(JSON.stringify(boulder), style, JSON.stringify({...pieceExport, name, pictureSize: 2048, setName})), `set ${name}`)
        const lod0 = files.find((f) => f.name === `${name}_LOD0.glb`)
        const glb = glbJson(lod0.bytes)
        const names = glb.doc.materials.map((m) => m.name)
        check(names.every((m) => m.startsWith('my_set_')), `set ${name}: materials ${names.join()}`)
        materials.push(names)
        const widths = (glb.doc.images ?? []).map((image) => pngWidth(lod0.bytes, glb.bin + (glb.doc.bufferViews[image.bufferView].byteOffset ?? 0)))
        check(widths.includes(2048), `set ${name}: LOD0 pictures ${widths.join()}`)
        const lod1 = glbJson(files.find((f) => f.name === `${name}_LOD1.glb`).bytes)
        check(lod1.doc.materials.every((m) => m.name.startsWith('my_set_')), `set ${name}: LOD1 materials`)
        entries.push({name, files: files.map((f) => ({name: f.name, ...(f.name.endsWith('.glb') ? {json: JSON.stringify(glbJson(f.bytes).doc)} : {})}))})
    }
    check(materials[0].some((m) => materials[1].includes(m)), `the two boulders share no material: ${JSON.stringify(materials)}`)
    const list = json(E.setList(setName, JSON.stringify(entries), JSON.stringify(groups), JSON.stringify(colours)))
    const text = JSON.parse(list.text)
    check(list.name === 'my_set.set.json' && text.set === 'my_set' && text.props.length === 2, `setList ${list.name}`)
    check(text.materials.length > 0 && text.materials.every((m) => m.startsWith('my_set_')), `setList materials ${text.materials.join()}`)
    return materials[0].join(', ')
})

await phase3('weather: a wet snowy boulder shines', async () => {
    const wet = {...presetScene('snowy-boulder'), parameters: {...presetScene('snowy-boulder').parameters, wet: 0.6}}
    const {meta} = checkMesh(await settle(E.build(JSON.stringify(wet), style, 1), 'wet boulder'), 'wet boulder')
    const roughness = meta.parts.map((p) => p.surface?.roughness).filter((r) => r !== undefined)
    check(roughness.some((r) => typeof r === 'number' && r < 1), `wet snowy-boulder roughness ${JSON.stringify(roughness)}`)
    check(meta.parts.every((p) => !p.surface || 'exportName' in p.surface), 'surface.exportName')
    const dry = checkMesh(await settle(E.build(JSON.stringify(presetScene('boulder')), style, 1), 'dry boulder'), 'dry boulder').meta
    check(dry.parts.every((p) => !p.surface || p.surface.roughness === null), 'a dry boulder has no roughness of its own')
})

// ---- globals ----

const reached = [...touched.keys()]
const unguarded = reached.filter((name) => !GUARDED.has(name))
console.log(`globals read beyond the shims: ${reached.length ? reached.map((n) => `${n} (${touched.get(n)})`).join(', ') : 'none'}`)
check(unguarded.length === 0, `browser globals reached that the shims do not stand in for: ${unguarded.join(', ')}`)
const errors = logs.filter((l) => l.startsWith('error'))
check(errors.length === 0, `console.error was called ${errors.length} times`)

sample()
console.log(`done in ${((Date.now() - t0) / 1000).toFixed(1)} s, peak RSS ${(peakRss / 2 ** 20).toFixed(0)} MiB, ${logs.length} console lines (${logs.filter((l) => l.startsWith('warn')).length} warnings)`)
if (slow.length) console.log(`over 20 s: ${slow.join(', ')}`)
console.log(failures.length === 0 ? `SMOKE OK: ${subjects.length} presets x ${levels.length} levels${doExport ? ', 2 exports' : ''}` : `SMOKE FAILED: ${failures.length} failures`)
process.exit(failures.length === 0 ? 0 : 1)
