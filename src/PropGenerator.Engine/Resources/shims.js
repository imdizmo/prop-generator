// Stand-ins for the few browser things easy-props' code reaches, for a bare V8 (ClearScript in the app, node:vm in
// the smoke script). Loaded before engine.js. Nothing here changes what easy-props makes: each stand-in only does
// what the browser would, synchronously or on a queue the host drains.
//
// The host provides one object, `__host`, before this file runs:
//   __host.log(level, text)           console output (optional)
//   __host.cancelled(token) -> bool   whether a job was cancelled (optional)
//   __host.png(rgba, w, h) -> bytes   a PNG of RGBA pixels, for OffscreenCanvas.convertToBlob (needed for export)
//
// The host drains timers with `__runTimers()` (setTimeout, setInterval, requestAnimationFrame), which returns true
// while anything ran or is still waiting. `Worker` stays undefined, so easy-props builds bark on the calling thread.
(function (g) {
    'use strict'

    const host = () => (typeof g.__host !== 'undefined' ? g.__host : null)

    if (typeof g.self === 'undefined') {
        g.self = g
    }

    // console: V8 without an inspector has a silent one (ClearScript) or none (node:vm).
    const text = (args) =>
        args
            .map((a) => {
                if (typeof a === 'string') return a
                if (a instanceof Error) return a.stack || String(a)
                try {
                    return JSON.stringify(a)
                } catch {
                    return String(a)
                }
            })
            .join(' ')
    const out = (level) => (...args) => {
        const h = host()
        if (h && typeof h.log === 'function') h.log(level, text(args))
    }
    g.console = {
        log: out('log'),
        info: out('info'),
        warn: out('warn'),
        error: out('error'),
        debug: () => {},
        trace: () => {},
        time: () => {},
        timeEnd: () => {},
        group: () => {},
        groupEnd: () => {},
    }

    // performance.now, in milliseconds from load.
    if (typeof g.performance === 'undefined') {
        const t0 = Date.now()
        g.performance = {now: () => Date.now() - t0, timeOrigin: t0}
    }

    if (typeof g.queueMicrotask === 'undefined') {
        g.queueMicrotask = (fn) => {
            Promise.resolve().then(fn)
        }
    }

    // MessageChannel: easy-props' yieldIfBusy posts one message to give way between components. Here the message
    // arrives on a microtask, so nothing waits. Posting is also where a cancelled job stops: it asks the host
    // (`__host.cancelled(token)`, the running job's token or 0 when it gave none) and throws inside the Promise
    // executor that posts, which rejects the job's promise.
    g.MessageChannel = class MessageChannel {
        constructor() {
            const port1 = {onmessage: null, close() {}, postMessage() {}}
            const port2 = {
                onmessage: null,
                close() {},
                postMessage(data) {
                    const h = host()
                    const token = g.__engineToken | 0
                    if (h && typeof h.cancelled === 'function' && h.cancelled(token)) {
                        throw new Error('EasyProps: cancelled')
                    }
                    Promise.resolve().then(() => port1.onmessage && port1.onmessage({data}))
                },
            }
            this.port1 = port1
            this.port2 = port2
        }
    }

    // Timers on a queue the host drains: V8 alone has no event loop.
    let nextId = 1
    const timers = new Map()
    g.setTimeout = (fn, ms, ...args) => {
        const id = nextId++
        timers.set(id, {fn, args, at: Date.now() + (ms | 0), every: 0})
        return id
    }
    g.setInterval = (fn, ms, ...args) => {
        const id = nextId++
        timers.set(id, {fn, args, at: Date.now() + (ms | 0), every: Math.max(1, ms | 0)})
        return id
    }
    g.clearTimeout = g.clearInterval = (id) => {
        timers.delete(id)
    }
    // A frame comes as soon as the host pumps: there is no screen to wait for.
    g.requestAnimationFrame = (fn) => g.setTimeout(() => fn(g.performance.now()), 0)
    g.cancelAnimationFrame = g.clearTimeout
    g.__runTimers = () => {
        const now = Date.now()
        let ran = false
        for (const [id, t] of [...timers]) {
            if (t.at > now) continue
            if (t.every) t.at = now + t.every
            else timers.delete(id)
            ran = true
            t.fn(...t.args)
        }
        return ran || timers.size > 0
    }
    g.__clearTimers = () => {
        timers.clear()
    }

    // TextEncoder / TextDecoder, UTF-8 only.
    g.TextEncoder = class TextEncoder {
        get encoding() {
            return 'utf-8'
        }
        encode(s = '') {
            s = String(s)
            const out = new Uint8Array(s.length * 3)
            let p = 0
            for (let i = 0; i < s.length; i++) {
                let c = s.charCodeAt(i)
                if (c >= 0xd800 && c < 0xdc00 && i + 1 < s.length) {
                    const d = s.charCodeAt(i + 1)
                    if (d >= 0xdc00 && d < 0xe000) {
                        c = 0x10000 + ((c - 0xd800) << 10) + (d - 0xdc00)
                        i++
                    } else c = 0xfffd
                } else if (c >= 0xd800 && c < 0xe000) c = 0xfffd
                if (c < 0x80) out[p++] = c
                else if (c < 0x800) {
                    out[p++] = 0xc0 | (c >> 6)
                    out[p++] = 0x80 | (c & 63)
                } else if (c < 0x10000) {
                    out[p++] = 0xe0 | (c >> 12)
                    out[p++] = 0x80 | ((c >> 6) & 63)
                    out[p++] = 0x80 | (c & 63)
                } else {
                    out[p++] = 0xf0 | (c >> 18)
                    out[p++] = 0x80 | ((c >> 12) & 63)
                    out[p++] = 0x80 | ((c >> 6) & 63)
                    out[p++] = 0x80 | (c & 63)
                }
            }
            return out.slice(0, p)
        }
    }
    g.TextDecoder = class TextDecoder {
        constructor(label = 'utf-8') {
            this.encoding = String(label).toLowerCase()
        }
        decode(buf) {
            if (buf == null) return ''
            const b = buf instanceof Uint8Array ? buf : ArrayBuffer.isView(buf) ? new Uint8Array(buf.buffer, buf.byteOffset, buf.byteLength) : new Uint8Array(buf)
            let s = ''
            let chunk = []
            for (let i = 0; i < b.length; ) {
                let c = b[i++]
                if (c >= 0xf0) c = ((c & 7) << 18) | ((b[i++] & 63) << 12) | ((b[i++] & 63) << 6) | (b[i++] & 63)
                else if (c >= 0xe0) c = ((c & 15) << 12) | ((b[i++] & 63) << 6) | (b[i++] & 63)
                else if (c >= 0xc0) c = ((c & 31) << 6) | (b[i++] & 63)
                if (c >= 0x10000) {
                    c -= 0x10000
                    chunk.push(0xd800 + (c >> 10), 0xdc00 + (c & 1023))
                } else chunk.push(c)
                if (chunk.length > 8192) {
                    s += String.fromCharCode.apply(null, chunk)
                    chunk = []
                }
            }
            return s + String.fromCharCode.apply(null, chunk)
        }
    }

    // Blob: bytes in memory. FileReader: answers on a microtask, after the caller has set its handlers
    // (GLTFExporter sets onloadend after calling readAsArrayBuffer).
    const toBytes = (p) =>
        p instanceof Uint8Array
            ? p
            : ArrayBuffer.isView(p)
              ? new Uint8Array(p.buffer, p.byteOffset, p.byteLength)
              : p instanceof ArrayBuffer
                ? new Uint8Array(p)
                : p instanceof g.Blob
                  ? p._bytes
                  : new g.TextEncoder().encode(String(p))
    g.Blob = class Blob {
        constructor(parts = [], options = {}) {
            const all = parts.map(toBytes)
            let n = 0
            for (const u of all) n += u.length
            const bytes = new Uint8Array(n)
            let o = 0
            for (const u of all) {
                bytes.set(u, o)
                o += u.length
            }
            this._bytes = bytes
            this.type = options.type || ''
        }
        get size() {
            return this._bytes.length
        }
        arrayBuffer() {
            return Promise.resolve(this._bytes.slice().buffer)
        }
        text() {
            return Promise.resolve(new g.TextDecoder().decode(this._bytes))
        }
        slice(start, end, type) {
            const b = new g.Blob([], {type})
            b._bytes = this._bytes.slice(start, end)
            return b
        }
    }
    const B64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
    const base64 = (u8) => {
        let s = ''
        let i = 0
        for (; i + 2 < u8.length; i += 3) {
            const n = (u8[i] << 16) | (u8[i + 1] << 8) | u8[i + 2]
            s += B64[n >> 18] + B64[(n >> 12) & 63] + B64[(n >> 6) & 63] + B64[n & 63]
        }
        if (i < u8.length) {
            const n = (u8[i] << 16) | ((u8[i + 1] || 0) << 8)
            s += B64[n >> 18] + B64[(n >> 12) & 63] + (i + 1 < u8.length ? B64[(n >> 6) & 63] : '=') + '='
        }
        return s
    }
    g.FileReader = class FileReader {
        constructor() {
            this.result = null
            this.readyState = 0
            this.onload = this.onloadend = this.onerror = null
        }
        _done(result) {
            this.readyState = 1
            Promise.resolve().then(() => {
                this.result = result
                this.readyState = 2
                const event = {target: this}
                if (this.onload) this.onload(event)
                if (this.onloadend) this.onloadend(event)
            })
        }
        readAsArrayBuffer(blob) {
            this._done(blob._bytes.slice().buffer)
        }
        readAsDataURL(blob) {
            this._done('data:' + (blob.type || 'application/octet-stream') + ';base64,' + base64(blob._bytes))
        }
        readAsText(blob) {
            this._done(new g.TextDecoder().decode(blob._bytes))
        }
    }

    // ImageData and an OffscreenCanvas whose 2D context holds pixels: enough for GLTFExporter, which puts a
    // DataTexture's pixels and asks for a PNG. putImageData ignores the transform, as in a browser.
    g.ImageData = class ImageData {
        constructor(data, width, height) {
            if (typeof data === 'number') {
                height = width
                width = data
                data = new Uint8ClampedArray(width * height * 4)
            }
            this.data = data
            this.width = width
            this.height = height
            this.colorSpace = 'srgb'
        }
    }
    class Context2D {
        constructor(canvas) {
            this.canvas = canvas
        }
        _pixels() {
            const c = this.canvas
            if (!c._pixels || c._pixels.length !== c.width * c.height * 4) c._pixels = new Uint8ClampedArray(c.width * c.height * 4)
            return c._pixels
        }
        translate() {}
        scale() {}
        setTransform() {}
        resetTransform() {}
        save() {}
        restore() {}
        putImageData(image, dx = 0, dy = 0) {
            const px = this._pixels()
            const w = this.canvas.width
            const h = this.canvas.height
            for (let y = 0; y < image.height; y++) {
                const ty = y + dy
                if (ty < 0 || ty >= h) continue
                const x0 = Math.max(0, -dx)
                const x1 = Math.min(image.width, w - dx)
                if (x1 <= x0) continue
                px.set(image.data.subarray((y * image.width + x0) * 4, (y * image.width + x1) * 4), (ty * w + x0 + dx) * 4)
            }
        }
        getImageData(sx, sy, sw, sh) {
            const px = this._pixels()
            const w = this.canvas.width
            const out = new g.ImageData(sw, sh)
            for (let y = 0; y < sh; y++) out.data.set(px.subarray(((y + sy) * w + sx) * 4, ((y + sy) * w + sx + sw) * 4), y * sw * 4)
            return out
        }
        drawImage() {
            throw new Error('EasyProps shims: drawImage is not available')
        }
    }
    g.OffscreenCanvas = class OffscreenCanvas {
        constructor(width, height) {
            this.width = width
            this.height = height
            this._pixels = null
        }
        getContext(kind) {
            if (kind !== '2d') return null
            if (!this._context) this._context = new Context2D(this)
            return this._context
        }
        convertToBlob(options = {}) {
            const type = options.type || 'image/png'
            if (type !== 'image/png') return Promise.reject(new Error('EasyProps shims: only image/png is encoded'))
            const h = host()
            if (!h || typeof h.png !== 'function') return Promise.reject(new Error('EasyProps shims: the host has no PNG encoder'))
            const px = this._context ? this._context._pixels() : new Uint8ClampedArray(this.width * this.height * 4)
            const rgba = new Uint8Array(px.buffer, px.byteOffset, px.byteLength)
            return Promise.resolve(new g.Blob([h.png(rgba, this.width, this.height)], {type}))
        }
    }

    // WebAssembly.instantiate, compiled synchronously: an embedded V8 has no platform to finish an async compile
    // (three's SimplifyModifier waits for meshopt's). The result is the same Module and Instance.
    if (typeof g.WebAssembly !== 'undefined') {
        const wasm = g.WebAssembly
        wasm.instantiate = function instantiate(source, imports) {
            try {
                if (source instanceof wasm.Module) return Promise.resolve(new wasm.Instance(source, imports))
                const module = new wasm.Module(source)
                return Promise.resolve({module, instance: new wasm.Instance(module, imports)})
            } catch (error) {
                return Promise.reject(error)
            }
        }
        wasm.compile = (bytes) => {
            try {
                return Promise.resolve(new wasm.Module(bytes))
            } catch (error) {
                return Promise.reject(error)
            }
        }
    }
})(globalThis)
