// A stand-in for shims.js + engine.js, for the host's own tests. It defines the global EasyProps with the frozen
// API's names, returning canned data, and a global Test with what the host's tests need (a heap blow-up, a long
// cancellable job, a job that never ends). It carries the two shims the host relies on: the timer queue drained by
// __runTimers() and a MessageChannel that checks __host.cancelled(). Nothing here is easy-props' code.
(function (g) {
  'use strict';

  // ---- shims (the parts the host drives) ----
  let nextId = 1;
  const timers = new Map();
  g.setTimeout = (fn, ms, ...args) => { const id = nextId++; timers.set(id, { fn, args, at: Date.now() + (ms | 0) }); return id; };
  g.clearTimeout = (id) => { timers.delete(id); };
  g.requestAnimationFrame = (fn) => g.setTimeout(() => fn(Date.now()), 16);
  g.__clearTimers = () => { timers.clear(); };
  g.__runTimers = () => {
    const now = Date.now();
    let ran = false;
    for (const [id, t] of [...timers]) {
      if (t.at > now) continue;
      timers.delete(id);
      t.fn(...t.args);
      ran = true;
    }
    return ran || timers.size > 0;
  };
  g.MessageChannel = class MessageChannel {
    constructor() {
      const port1 = { onmessage: null };
      this.port1 = port1;
      this.port2 = {
        postMessage: (data) => {
          if (__host.cancelled()) throw new Error('cancelled');
          Promise.resolve().then(() => port1.onmessage && port1.onmessage({ data }));
        },
      };
    }
  };
  const yieldOnce = () => new Promise((resolve) => { const c = new MessageChannel(); c.port1.onmessage = resolve; c.port2.postMessage(0); });
  const later = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

  // ---- canned data ----
  const generators = [
    { id: 'painted-tree', version: 3, label: 'Painted tree', lowPoly: true, still: false, swaysEvenly: false, offersCollision: true, offersPivot: false },
    { id: 'cut-rock', version: 1, label: 'Cut rock', lowPoly: true, still: true, swaysEvenly: false, offersCollision: false, offersPivot: true },
  ];
  const presets = [
    { id: 'oak', name: 'Oak', sub: 'Broad and old', category: 'Broadleaf', generatorId: 'painted-tree', seed: 4242, parameters: { height: 8, palette: 'oak' }, family: 'tree', thumbnailFresh: true },
    { id: 'round-canopy', name: 'Round canopy', sub: 'The old style, to compare', category: null, generatorId: 'branching-tree', seed: 12345, parameters: { height: 4.2 }, family: 'tree', thumbnailFresh: false },
    { id: 'boulder', name: 'Boulder', sub: 'Cut stone', category: 'Rocks', generatorId: 'cut-rock', seed: 7, parameters: { size: 1.5 }, family: 'rock', thumbnailFresh: true },
  ];
  const retired = { 'wide-canopy': { id: 'wide-canopy', name: 'Wide canopy', sub: 'Retired', category: null, generatorId: 'branching-tree', seed: 1, parameters: {}, family: 'tree', thumbnailFresh: false } };

  const pngOf = (w, h) => {
    const rgba = new Uint8Array(w * h * 4);
    for (let i = 0; i < rgba.length; i++) rgba[i] = (i * 37) & 255;
    return __host.png(rgba, w, h);
  };

  g.EasyProps = {
    info: () => JSON.stringify({
      tag: 'v0.4.2', commit: '27c6956', appVersion: '0.4.2', levelShares: [1, 0.4, 0.12], farLevel: 3,
      budgets: { hero: 60000, standard: 20000, background: 6000, mobile: 3000 }, generators,
    }),
    presets: () => JSON.stringify({ categories: ['Broadleaf', 'Rocks'], presets }),
    preset: (id) => { const p = presets.find((x) => x.id === id) || retired[id]; return p ? JSON.stringify({ ...p, retired: !presets.includes(p) }) : null; },
    parameters: (gen) => JSON.stringify({
      specs: [{ key: 'height', label: 'Height', kind: 'number', min: 1, max: 30, step: 0.1, default: 8, group: 'Shape' }],
      colourRows: gen === 'painted-tree' ? [{ key: 'barkDeep', label: 'Bark, deep', group: 'Colours', default: '#3a2a1c', ownOf: 'palette', set: 'tree', named: { oak: '#3a2a1c' }, swatches: [{ name: 'Dark', value: '#201810' }] }] : [],
    }),
    shown: (gen, values) => {
      const v = JSON.parse(values);
      const rows = v.height > 10 ? [{ key: 'height', label: 'Height', group: 'Shape' }, { key: 'spread', label: 'Spread', group: 'Crown' }] : [{ key: 'height', label: 'Height', group: 'Shape' }];
      // A spec with no group of its own, as entry.ts writes it (group: null).
      if (v.height > 20) rows.push({ key: 'note', label: 'Note', group: null });
      return JSON.stringify(rows);
    },
    adjust: (gen, before, after) => { const a = JSON.parse(after); return JSON.stringify(a.palette === 'birch' ? { barkDeep: '#d8d2c4' } : {}); },
    validate: (gen, values) => { const v = JSON.parse(values); const ok = typeof v.height === 'number'; return JSON.stringify({ valid: ok, parameters: ok ? v : { height: 8 }, issues: ok ? [] : [{ key: 'height', message: 'Height must be a number.' }] }); },
    traits: (gen) => JSON.stringify({ farAway: gen === 'painted-tree', sways: gen !== 'cut-rock', swaysEvenly: false }),
    components: (gen, values, seed) => JSON.stringify([{ id: 'c1', type: 'part', generatorId: gen, parameters: '$asset', seed, parentId: null }]),
    // The real entry.ts' mesh format: each part names its attributes and index by their place in `arrays`.
    build: async (scene, style, detail, options) => {
      await later(1);
      await yieldOnce();
      const opts = options ? JSON.parse(options) : {};
      const attr = (name, itemSize, array) => ({ name, itemSize, normalized: false, array });
      const meta = {
        parts: [
          { componentId: 'trunk', generatorId: 'painted-tree', material: 'bark', role: 'wood', roleOfPart: 'trunk', flatShading: false, proxy: false,
            matrix: [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1], growth: 1.25, tint: [1, 0.5, 0.25],
            surface: { vertexColors: true, map: 'bark:2:0.5', normalMap: 'relief:2', alphaTest: null, receiveShadow: true, proxy: false },
            vertices: 3, faces: 1,
            attributes: [attr('position', 3, 0), attr('normal', 3, 1), attr('color', 3, 2), attr('uv', 2, 3)],
            index: { type: 'u16', array: 4 } },
          { componentId: 'crown', generatorId: 'painted-tree', material: 'leaf', role: null, roleOfPart: 'crown', flatShading: true, proxy: true,
            matrix: [2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 1, 2, 3, 1], growth: 0.5, tint: [1, 1, 1],
            surface: { vertexColors: false, map: 'leaf', normalMap: null, alphaTest: 0.5, receiveShadow: null, proxy: true },
            vertices: 3, faces: 1,
            attributes: [attr('position', 3, 5), attr('normal', 3, 1), attr('uv1', 2, 6), attr('rigid', 1, 7)],
            index: { type: 'u32', array: 8 } },
          { componentId: 'loose', generatorId: 'painted-tree', material: 'bark', role: null, roleOfPart: 'twig', flatShading: false,
            matrix: [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1], growth: 1, tint: [1, 1, 1], proxy: false, surface: null,
            vertices: 3, faces: 1, attributes: [attr('position', 3, 0)], index: null },
        ],
        echo: { scene: JSON.parse(scene), style, detail },
      };
      if (opts.fullFaces) meta.fullFaces = 1234;
      const p = new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, detail]);
      const n = new Float32Array([0, 0, 1, 0, 0, 1, 0, 0, 1]);
      const c = new Float32Array([1, 0, 0, 0, 1, 0, 0, 0, 1]);
      const uv = new Float32Array([0, 0, 1, 0, 0, 1]);
      const i16 = new Uint16Array([0, 1, 2]);
      // A view into a bigger buffer, as entry.ts returns when a geometry's array is longer than its count.
      const wide = new Float32Array([9, 9, 9, 5, 5, 5, 6, 6, 6, 7, 7, 7, 9]).subarray(3, 12);
      const uv1 = new Float32Array([0.5, 0.5, 1, 1, 0, 0]);
      const rigid = new Float32Array([1, 0, 1]);
      const i32 = new Uint32Array([0, 2, 1, 70000, 9]).subarray(1, 4);
      return { meta: JSON.stringify(meta), arrays: [p, n, c, uv, i16, wide, uv1, rigid, i32] };
    },
    texture: (key) => {
      const rgba = new Uint8Array(2 * 2 * 4);
      for (let i = 0; i < rgba.length; i++) rgba[i] = i;
      // As three has them: bark sRGB and repeating, relief linear ('') and repeating, the rest sRGB and clamped.
      const colorSpace = key.startsWith('relief') ? '' : 'srgb';
      const repeat = key.startsWith('bark') || key.startsWith('relief');
      return { meta: JSON.stringify({ w: 2, h: 2, key, colorSpace, repeat }), arrays: [rgba] };
    },
    // Reports its progress through __host.progress as entry.ts does (halfway, then done); `slow` keeps reporting
    // until it is cancelled.
    exportPackage: async (scene, style, settings) => {
      await later(1);
      const s = JSON.parse(settings);
      for (let done = 1; s.slow; done++) {
        __host.progress(0, done, done + 1);
        await yieldOnce();
        await later(1);
      }
      __host.progress(0, 1, 2);
      await yieldOnce();
      __host.progress(0, 2, 2);
      return [
        { name: s.name + '.glb', bytes: new TextEncoderLite().encode('glTF-standin'), faces: 321 },
        { name: s.name + '_bark.png', bytes: pngOf(3, 2), faces: null },
      ];
    },
    collision: async () => {
      await later(1);
      return JSON.stringify({ offered: [], shapes: [{ start: [0, 0, 0], end: [0, 2, 0], startRadius: 0.3, endRadius: 0.2 }] });
    },
    pivot: () => JSON.stringify([0, 0.25, 0]),
  };

  class TextEncoderLite { encode(s) { const u = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) u[i] = s.charCodeAt(i) & 255; return u; } }

  // ---- what only the host's tests call ----
  g.Test = {
    blowUp: () => { const keep = []; for (let i = 0; ; i++) keep.push({ i, s: 'x' + i, a: [i, i, i, i] }); },
    bigBuffer: (bytes) => new ArrayBuffer(bytes).byteLength,
    // Yields through the MessageChannel shim forever: only the host's cancel flag stops it.
    slow: async () => { for (;;) { await yieldOnce(); await later(1); } },
    // A synchronous loop that never yields: only an interrupt stops it.
    spin: () => { for (;;) { } },
    // A promise that never settles.
    hang: () => new Promise(() => {}),
    // Leaves a timer behind, then never settles: the timer must not run in a later call.
    leaveTimer: () => { g.__fired = false; setTimeout(() => { g.__fired = true; }, 300); return new Promise(() => {}); },
    firedLater: async () => { await later(500); return g.__fired; },
    count: () => { g.__count = (g.__count || 0) + 1; return g.__count; },
    png: (w, h) => pngOf(w, h),
    grow: (mb) => { g.__keep = g.__keep || []; g.__keep.push(new Uint8Array(mb << 20)); return g.__keep.length; },
  };
})(globalThis);
