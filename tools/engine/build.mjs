// Bundles tools/engine/entry.ts with easy-props' own sources into one iife (global `EasyProps`), using easy-props'
// own esbuild (the one its vite resolves), and writes the resources the app embeds:
//   <out>/engine.js, <out>/shims.js, <out>/thumbs/*.png + preset-thumbs.json, <out>/engine-pin.json
//
//   node tools/engine/build.mjs --easy-props <checkout> --tag v0.4.2 --commit <sha> --out <Resources folder>
//
// build.sh runs this after cloning and installing; run it by hand only on a checkout made that way.
import crypto from 'node:crypto'
import fs from 'node:fs'
import {createRequire} from 'node:module'
import path from 'node:path'

const args = process.argv.slice(2)
const arg = (name) => {
    const at = args.indexOf(`--${name}`)
    if (at < 0 || at + 1 >= args.length) throw new Error(`build.mjs: --${name} is needed`)
    return args[at + 1]
}
const easyProps = path.resolve(arg('easy-props'))
const tag = arg('tag')
const commit = arg('commit')
const out = path.resolve(arg('out'))
const here = path.dirname(new URL(import.meta.url).pathname)
const frontend = path.join(easyProps, 'frontend')
const src = path.join(frontend, 'src')

// Their esbuild: the one their vite depends on, so the bundle is made by the same tool as their app.
const fromVite = createRequire(fs.realpathSync(path.join(frontend, 'node_modules/vite/package.json')))
const esbuild = await import(fromVite.resolve('esbuild'))
const esbuildVersion = fromVite('esbuild/package.json').version
const threeVersion = JSON.parse(fs.readFileSync(path.join(frontend, 'node_modules/three/package.json'), 'utf8')).version

/** `easy-props/<path>` names a module under their frontend/src, resolved as their own imports are. */
const easyPropsImports = {
    name: 'easy-props',
    setup(build) {
        build.onResolve({filter: /^easy-props\//}, (a) =>
            build.resolve(`./${a.path.slice('easy-props/'.length)}`, {kind: a.kind, resolveDir: src}),
        )
    },
}

/**
 * `?raw` imports (their engine kits' shader files) as text, as vite does. The path is kept relative to their checkout:
 * esbuild writes it into the bundle as a comment.
 */
const rawText = {
    name: 'raw',
    setup(build) {
        build.onResolve({filter: /\?raw$/}, (a) => ({
            path: path.relative(easyProps, path.resolve(a.resolveDir, a.path.slice(0, -'?raw'.length))),
            namespace: 'raw',
        }))
        build.onLoad({filter: /.*/, namespace: 'raw'}, (a) => ({contents: fs.readFileSync(path.resolve(easyProps, a.path), 'utf8'), loader: 'text'}))
    },
}

/**
 * Our entry.ts, named `ours:tools/engine/entry.ts` in the bundle whatever folder the repo is in. Its imports resolve
 * from tools/engine as they would for the file itself.
 */
const entryName = 'tools/engine/entry.ts'
const ourEntry = {
    name: 'ours',
    setup(build) {
        build.onResolve({filter: /^tools\/engine\/entry\.ts$/}, (a) => (a.kind === 'entry-point' ? {path: a.path, namespace: 'ours'} : undefined))
        build.onLoad({filter: /.*/, namespace: 'ours'}, () => ({contents: fs.readFileSync(path.join(here, 'entry.ts'), 'utf8'), loader: 'ts', resolveDir: here}))
    },
}

fs.mkdirSync(out, {recursive: true})
const engine = path.join(out, 'engine.js')
const started = Date.now()
const result = await esbuild.build({
    // The bundle is unminified, so esbuild writes each module's path as a comment, relative to absWorkingDir. Their
    // checkout as the working folder, and our entry under a fixed name (ourEntry), make those paths (and so the
    // bytes and the pinned SHA-256) the same whoever builds it, from wherever.
    absWorkingDir: easyProps,
    entryPoints: [entryName],
    bundle: true,
    format: 'iife',
    globalName: 'EasyProps',
    platform: 'neutral',
    mainFields: ['module', 'main'],
    target: 'es2022',
    // The entry lives outside their tree; 'three' and their other packages resolve from their node_modules.
    nodePaths: [path.join(frontend, 'node_modules')],
    // Only bark-pool's Worker URL reads import.meta.url, and it is never reached without Worker.
    define: {
        'import.meta.url': '"file:///easy-props/engine.js"',
        __ENGINE_TAG__: JSON.stringify(tag),
        __ENGINE_COMMIT__: JSON.stringify(commit),
    },
    legalComments: 'none',
    charset: 'utf8',
    outfile: engine,
    metafile: true,
    plugins: [ourEntry, easyPropsImports, rawText],
    logLevel: 'warning',
})
const bytes = fs.readFileSync(engine)
const inputs = Object.entries(result.metafile.outputs[path.relative(easyProps, engine)]?.inputs ?? {})
const byPackage = {}
for (const [file, {bytesInOutput}] of inputs) {
    const key = file.includes('node_modules') ? file.replace(/.*node_modules\/(?:\.pnpm\/[^/]+\/node_modules\/)?((?:@[^/]+\/)?[^/]+).*/, '$1') : file.startsWith('frontend/src/') ? 'easy-props' : path.basename(file)
    byPackage[key] = (byPackage[key] ?? 0) + bytesInOutput
}
console.log(`engine.js: ${bytes.length} bytes in ${Date.now() - started} ms (esbuild ${esbuildVersion}, three ${threeVersion})`)
console.log(
    `  by source: ${Object.entries(byPackage)
        .sort((a, b) => b[1] - a[1])
        .map(([k, v]) => `${k} ${(v / 1024).toFixed(0)} KiB`)
        .join(', ')}`,
)

// The shims beside it: the host loads them first.
fs.copyFileSync(path.join(here, 'shims.js'), path.join(out, 'shims.js'))

// Their preset pictures, and the fingerprints that say which are still fresh.
const thumbs = path.join(out, 'thumbs')
fs.rmSync(thumbs, {recursive: true, force: true})
fs.mkdirSync(thumbs)
const pictures = fs.readdirSync(path.join(frontend, 'public/preset-thumbs')).filter((name) => name.endsWith('.png'))
for (const name of pictures) fs.copyFileSync(path.join(frontend, 'public/preset-thumbs', name), path.join(thumbs, name))
fs.copyFileSync(path.join(src, 'panels/preset-thumbs.json'), path.join(thumbs, 'preset-thumbs.json'))

const sha256 = (file) => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const pin = {
    tag,
    commit,
    sha256: sha256(engine),
    bytes: bytes.length,
    shimsSha256: sha256(path.join(out, 'shims.js')),
    esbuild: esbuildVersion,
    three: threeVersion,
    thumbnails: pictures.length,
}
fs.writeFileSync(path.join(out, 'engine-pin.json'), `${JSON.stringify(pin, null, 4)}\n`)
console.log(`pin: ${JSON.stringify(pin)}`)
