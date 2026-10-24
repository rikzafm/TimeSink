// Draws every TimeSink app icon as SVG (tools/icons/svg/*.svg) and packs multi-size ICOs
// (src/TimeSink.Core/Icons/*.ico, embedded in TimeSink.Core). Dev tool only: the ICOs are checked in.
//   cd tools/icons && npm install && node build-icons.mjs
import { Resvg } from '@resvg/resvg-js';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const svgDir = join(root, "tools", "icons", "svg");
const icoDir = join(root, 'src', 'TimeSink.Core', 'Icons');
mkdirSync(svgDir, { recursive: true });
mkdirSync(icoDir, { recursive: true });

const SIZES = [16, 20, 24, 32, 48, 256];
const HALFTONE_MIN_SIZE = 24; // below this the halftone dots turn to mush, so they are dropped
const f = (n) => +n.toFixed(2);
const INK = '#111111', CREAM = '#fbf6ee', CY = '#00a8e0', MG = '#e6007e', YE = '#ffe900';

const polar = (r, deg, cx = 128, cy = 128) => [cx + r * Math.sin((deg * Math.PI) / 180), cy - r * Math.cos((deg * Math.PI) / 180)];
const pts = (list) => list.map((p) => p.map(f).join(',')).join(' ');
const svg = (body) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">${body}</svg>`;
const tileRect = (fill, stroke, sw) => `<rect x="${16 + sw / 2}" y="${16 + sw / 2}" width="${224 - sw}" height="${224 - sw}" rx="${56 - sw / 2}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}"/>`;
const clip = '<clipPath id="t"><rect x="16" y="16" width="224" height="224" rx="56"/></clipPath>';
const line = (p, q, c, w) => `<line x1="${f(p[0])}" y1="${f(p[1])}" x2="${f(q[0])}" y2="${f(q[1])}" stroke="${c}" stroke-width="${w}" stroke-linecap="round"/>`;
const bar = (x, y, w, h, c) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${Math.min(w, h) / 2}" fill="${c}"/>`;
const ring = (r, c, w, dash = '') => `<circle cx="128" cy="128" r="${r}" fill="none" stroke="${c}" stroke-width="${w}"${dash ? ` stroke-dasharray="${dash}"` : ''}/>`;
const dot = (x, y, r, c) => `<circle cx="${x}" cy="${y}" r="${r}" fill="${c}"/>`;
const arc = (r, a0, a1, c, w) => {
  const [x0, y0] = polar(r, a0), [x1, y1] = polar(r, a1);
  return `<path d="M${f(x0)} ${f(y0)} A${r} ${r} 0 ${a1 - a0 > 180 ? 1 : 0} 1 ${f(x1)} ${f(y1)}" fill="none" stroke="${c}" stroke-width="${w}" stroke-linecap="round"/>`;
};

/** Halftone dot field in a corner (dots shrink away from it). Kept in one <g id="halftone"> so small sizes can drop it. */
function halftone(corner, color) {
  const [cx, cy] = { br: [240, 240], bl: [16, 240] }[corner];
  const sx = cx > 128 ? -1 : 1, sy = -1;
  let dots = '';
  for (let i = 0; i < 8; i++) for (let j = 0; j < 8; j++) {
    const r = 11 * (1 - Math.hypot(i, j) / 8) ** 1.2;
    if (r > 1) dots += `<circle cx="${f(cx + sx * (i * 17 + (j % 2) * 8))}" cy="${f(cy + sy * j * 17)}" r="${f(r)}"/>`;
  }
  return `<g id="halftone" fill="${color}" clip-path="url(#t)">${dots}</g>`;
}

const olive = '#5a7a1f', mid = '#8aa83a', lime = '#c8f564', dark = '#101510';
const cleanTile = tileRect(dark, '#262c1f', 4);
const icons = {};

// ---- Clean ----
icons['clean.bars'] = svg(cleanTile + bar(66, 116, 40, 80, olive) + bar(122, 84, 40, 112, lime) + dot(142, 62, 12, lime));
icons['clean.gauge'] = svg(cleanTile + ring(66, '#2a3320', 22) +
  `<path d="M128 62 A66 66 0 1 1 71.8 162.6" fill="none" stroke="${lime}" stroke-width="22" stroke-linecap="round"/>` + dot(128, 128, 13, lime));
icons['clean.funnel'] = svg(cleanTile + bar(60, 62, 136, 22, lime) + bar(78, 98, 100, 22, mid) + bar(96, 134, 64, 22, olive) + dot(128, 188, 11, lime));
icons['clean.marker'] = svg(cleanTile + bar(58, 62, 140, 36, lime) + bar(112, 100, 32, 60, lime) + dot(128, 188, 12, lime));
icons['clean.ticks'] = svg(cleanTile + Array.from({ length: 12 }, (_, i) => line(polar(40, i * 30), polar(74, i * 30), lime, 12)).join('') + dot(128, 128, 9, '#fff'));
icons['clean.dashed'] = svg(cleanTile + `<rect x="60" y="60" width="136" height="136" rx="30" fill="none" stroke="${lime}" stroke-width="12" stroke-dasharray="26 18" stroke-linecap="round"/>` + dot(128, 128, 18, lime));
const diamond = (y, c) => `<path d="M128 ${y} L188 ${y + 28} L128 ${y + 56} L68 ${y + 28}Z" fill="${c}" stroke="${c}" stroke-width="10" stroke-linejoin="round"/>`;
icons['clean.layers'] = svg(cleanTile + diamond(140, olive) + diamond(112, mid) + diamond(84, lime));
icons['clean.ripple'] = svg(cleanTile + ring(78, '#3d5016', 10) + ring(58, mid, 10) + ring(38, '#a8c84a', 10) + dot(128, 128, 14, lime));

// ---- Comic ----
{
  const lens = [60, 56, 62, 54, 62, 56, 60, 56, 62, 54, 62, 56];
  const ticks = lens.map((len, i) => line(polar(88 - len, i * 30), polar(88, i * 30), INK, 13)).join('');
  const reds = [[222, 50, 12], [198, 56, 9], [176, 58, 6], [226, 74, 9], [220, 94, 5],
    [34, 206, 12], [58, 204, 9], [80, 200, 6], [30, 182, 9], [36, 162, 5]]
    .map(([x, y, r]) => `<circle cx="${x}" cy="${y}" r="${r}"/>`).join('');
  icons['comic.ticks'] = svg(clip + tileRect('#fccc17', INK, 10) + ticks +
    `<circle cx="128" cy="128" r="52" fill="#fff" stroke="${INK}" stroke-width="12"/>` +
    `<path d="M128 106 V130 L146 142" fill="none" stroke="${INK}" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/>` +
    `<g id="halftone" fill="#f2494a">${reds}</g>`);
}

// ---- Misprint ----
const mpTile = tileRect(CREAM, INK, 10);
/** Draws shape(color) three times (cyan and magenta offset, yellow on top) for the misprint look. */
const layered = (shape, o = 7) =>
  `<g transform="translate(${-o} ${-o * 0.6})">${shape(CY)}</g><g transform="translate(${o} ${-o * 0.3})">${shape(MG)}</g>${shape(YE)}`;
const mp = (body, corner = 'br', htColor = MG) => svg(clip + mpTile + `<g clip-path="url(#t)">${body}</g>` + halftone(corner, htColor));

icons['misprint.ticks'] = mp(layered((c) => Array.from({ length: 16 }, (_, i) => line(polar(56, i * 22.5), polar(98, i * 22.5), c, 22)).join(''), 6) + dot(128, 128, 14, INK));

const tShape = 'M58 60 H198 V100 H152 V198 H104 V100 H58Z';
icons['misprint.t'] = mp(layered((c) => `<path d="${tShape}" fill="${c}"/>`, 10) +
  `<path d="${tShape}" fill="${YE}" stroke="${INK}" stroke-width="10" stroke-linejoin="round"/>`, 'br', YE);

const web = (c) => {
  let s = '';
  for (let i = 0; i < 8; i++) s += line([128, 128], polar(110, i * 45), c, 11);
  for (const r of [34, 62, 90, 112]) s += `<polygon points="${pts(Array.from({ length: 8 }, (_, i) => polar(r, i * 45)))}" fill="none" stroke="${c}" stroke-width="11" stroke-linejoin="round"/>`;
  return s;
};
icons['misprint.web'] = mp(layered(web, 7) + dot(128, 128, 15, INK));

const arrow = 'M92 54 L184 130 H150 L170 178 L142 190 L122 144 L92 168Z';
icons['misprint.cursor'] = mp(layered((c) => `<path d="${arrow}" fill="${c}"/>`, 8) +
  `<path d="${arrow}" fill="${YE}" stroke="${INK}" stroke-width="10" stroke-linejoin="round"/>` +
  [[92, 78], [124, 74], [156, 78]].map(([y, x2]) => line([52, y], [x2, y], INK, 10)).join(''));

{
  const holes = (y) => Array.from({ length: 7 }, (_, i) => `<rect x="${52 + i * 25}" y="${y}" width="14" height="12" rx="3" fill="${CREAM}"/>`).join('');
  icons['misprint.film'] = mp(
    '<g transform="rotate(-12 128 128)">' +
    `<rect x="40" y="70" width="178" height="112" fill="${CY}" transform="translate(-8 4)"/><rect x="40" y="70" width="178" height="112" fill="${MG}" transform="translate(8 8)"/>` +
    `<rect x="40" y="70" width="178" height="112" fill="${INK}"/>${holes(80)}${holes(160)}` +
    `<rect x="52" y="102" width="46" height="50" fill="${MG}"/><rect x="106" y="102" width="46" height="50" fill="${YE}"/><rect x="160" y="102" width="46" height="50" fill="${CY}"/></g>`, 'bl');
}

icons['misprint.rings'] = mp(
  `<g transform="translate(-8 -4)">${arc(88, 0, 250, CY, 22)}${arc(34, 20, 290, CY, 22)}</g>` +
  `<g transform="translate(8 -2)">${arc(88, 0, 250, MG, 22)}${arc(60, 0, 270, MG, 22)}</g>` +
  arc(60, 20, 300, YE, 22) + arc(34, 20, 280, YE, 22) +
  ring(88, INK, 5, '10 12') + ring(60, INK, 5, '10 12') + ring(34, INK, 5, '10 12') + dot(128, 128, 14, INK), 'br', YE);

{
  const star = Array.from({ length: 32 }, (_, i) => polar(i % 2 ? 66 : 108, i * 11.25));
  const poly = (c, extra = '') => `<polygon points="${pts(star)}" fill="${c}"${extra}/>`;
  icons['misprint.burst'] = mp(
    `<g transform="translate(-8 -4)">${poly(CY)}</g><g transform="translate(8 8)">${poly(YE)}</g>` +
    poly(MG, ` stroke="${INK}" stroke-width="10" stroke-linejoin="round"`) +
    `<path d="M128 128 V68 M128 128 L162 148" stroke="${INK}" stroke-width="16" stroke-linecap="round"/>` + dot(128, 128, 15, INK), 'bl');
}

// ---- Render + pack ----
function png(source, size) {
  const s = size < HALFTONE_MIN_SIZE ? source.replace(/<g id="halftone"[\s\S]*?<\/g>/, '') : source;
  return new Resvg(s, { fitTo: { mode: 'width', value: size } }).render().asPng();
}

function ico(source) {
  const frames = SIZES.map((s) => [s, png(source, s)]);
  const head = Buffer.alloc(6 + 16 * frames.length);
  head.writeUInt16LE(1, 2);
  head.writeUInt16LE(frames.length, 4);
  let offset = head.length;
  frames.forEach(([s, data], i) => {
    const e = 6 + i * 16;
    head[e] = s === 256 ? 0 : s;
    head[e + 1] = s === 256 ? 0 : s;
    head.writeUInt16LE(1, e + 4);
    head.writeUInt16LE(32, e + 6);
    head.writeUInt32LE(data.length, e + 8);
    head.writeUInt32LE(offset, e + 12);
    offset += data.length;
  });
  return Buffer.concat([head, ...frames.map(([, d]) => d)]);
}

for (const [id, source] of Object.entries(icons)) {
  writeFileSync(join(svgDir, `${id}.svg`), source);
  writeFileSync(join(icoDir, `${id}.ico`), ico(source));
}
console.log(`built ${Object.keys(icons).length} icons`);
