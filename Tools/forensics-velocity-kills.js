const fs = require("fs");

const sourcePath = process.argv[2] || "C:/VEGAS/project-inspection.json";
const data = JSON.parse(fs.readFileSync(sourcePath, "utf8").replace(/^\uFEFF/, ""));
const video = data.tracks.flatMap(t => t.isVideo ? t.events : []);
const audio = data.tracks.flatMap(t => t.isAudio ? t.events : []);
const markers = data.markers.map(m => m.timeSeconds).sort((a, b) => a - b);
const weapon = audio.filter(e =>
  (e.takes?.[e.activeTakeIndex]?.mediaPath || "").includes("All Weapons Showcase"));

const velocity = e => e.envelopes?.find(x => x.type === "Velocity");
const nearest = (xs, x) => xs.reduce((a, b) => Math.abs(b - x) < Math.abs(a - x) ? b : a, xs[0]);
const q = (xs, p) => {
  if (!xs.length) return null;
  xs = [...xs].sort((a, b) => a - b);
  const x = (xs.length - 1) * p, lo = Math.floor(x), hi = Math.ceil(x);
  return xs[lo] + (xs[hi] - xs[lo]) * (x - lo);
};
const summary = xs => ({
  n: xs.length,
  min: q(xs, 0),
  p10: q(xs, .1),
  p25: q(xs, .25),
  median: q(xs, .5),
  p75: q(xs, .75),
  p90: q(xs, .9),
  max: q(xs, 1)
});
const containing = time => video.filter(e => e.startSeconds - 1e-6 <= time && time <= e.endSeconds + 1e-6);
const mainContaining = time => containing(time).find(e => e.trackIndex === 1);

const envEvents = video.filter(e => velocity(e));
const shapes = {};
for (const e of envEvents) {
  const ps = velocity(e).points;
  const key = ps.length + ":" + ps.map(p => `${Math.round(p.value * 100) / 100}/${p.curve}`).join(",");
  shapes[key] = (shapes[key] || 0) + 1;
}

const four = envEvents.filter(e => velocity(e).points.length === 4).map(e => {
  const p = velocity(e).points;
  return {
    e,
    entrySpeed: p[0].value,
    lowSpeed: (p[1].value + p[2].value) / 2,
    entryRamp: p[1].timeSeconds - p[0].timeSeconds,
    plateau: p[2].timeSeconds - p[1].timeSeconds,
    exitRamp: p[3].timeSeconds - p[2].timeSeconds,
    tail: e.lengthSeconds - p[3].timeSeconds,
    exitSpeed: p[3].value,
    curves: p.map(x => x.curve).join(">")
  };
});

const weaponRows = weapon.map((a, i) => {
  const t = a.startSeconds;
  const v = mainContaining(t);
  const env = v && velocity(v);
  const pts = env?.points || [];
  const absPts = pts.map(p => v.startSeconds + p.timeSeconds);
  const nearestMarker = nearest(markers, t);
  const nearestPoint = absPts.length ? nearest(absPts, t) : null;
  return {
    index: i,
    t,
    duration: a.lengthSeconds,
    markerDelta: t - nearestMarker,
    videoId: v?.inspectorId || null,
    videoMediaPath: v?.takes?.[v.activeTakeIndex]?.mediaPath || null,
    videoStartDelta: v ? t - v.startSeconds : null,
    videoEndDelta: v ? v.endSeconds - t : null,
    pointDelta: nearestPoint === null ? null : t - nearestPoint,
    pointIndex: nearestPoint === null ? null : absPts.indexOf(nearestPoint),
    points: pts.length,
    lowEntryDelta: pts.length >= 3 ? t - (v.startSeconds + pts[1].timeSeconds) : null,
    lowExitDelta: pts.length >= 3 ? t - (v.startSeconds + pts[2].timeSeconds) : null,
    entrySpeed: pts[0]?.value ?? null,
    lowSpeed: pts[1]?.value ?? null,
    exitSpeed: pts.at(-1)?.value ?? null,
    sourceOffset: a.takes?.[a.activeTakeIndex]?.offsetSeconds ?? null
  };
});

// Sequence heuristic only: consecutive replacement shots no more than 2.25 s apart.
const sequences = [];
let current = [];
for (const r of weaponRows) {
  if (current.length && r.t - current.at(-1).t > 2.25) {
    sequences.push(current); current = [];
  }
  current.push(r);
}
if (current.length) sequences.push(current);
for (const s of sequences) {
  s.forEach((r, i) => r.sequencePosition =
    s.length === 1 ? "single" : i === 0 ? "first" : i === s.length - 1 ? "final" : "middle");
  s.forEach(r => r.sequenceLength = s.length);
}

const byPosition = {};
for (const r of weaponRows) {
  (byPosition[r.sequencePosition] ||= []).push(r);
}
const positionSummary = {};
for (const [pos, rs] of Object.entries(byPosition)) {
  positionSummary[pos] = {
    n: rs.length,
    markerAbsDelta: summary(rs.map(r => Math.abs(r.markerDelta))),
    lowEntryDelta: summary(rs.filter(r => r.lowEntryDelta !== null).map(r => r.lowEntryDelta)),
    lowExitDelta: summary(rs.filter(r => r.lowExitDelta !== null).map(r => r.lowExitDelta)),
    lowSpeed: summary(rs.filter(r => r.lowSpeed !== null).map(r => r.lowSpeed))
  };
}

// A more defensible multi-kill heuristic: consecutive replacement shots associated
// with the same source media. A source-path transition starts a new clip family.
const sourceSequences = [];
let sourceCurrent = [];
for (const r of weaponRows) {
  if (sourceCurrent.length && r.videoMediaPath !== sourceCurrent.at(-1).videoMediaPath) {
    sourceSequences.push(sourceCurrent); sourceCurrent = [];
  }
  sourceCurrent.push(r);
}
if (sourceCurrent.length) sourceSequences.push(sourceCurrent);
for (const s of sourceSequences) {
  s.forEach((r, i) => r.sourceSequencePosition =
    s.length === 1 ? "single" : i === 0 ? "first" : i === s.length - 1 ? "final" : "middle");
  s.forEach(r => r.sourceSequenceLength = s.length);
}
const sourcePositionSummary = {};
for (const pos of ["single", "first", "middle", "final"]) {
  const rs = weaponRows.filter(r => r.sourceSequencePosition === pos);
  sourcePositionSummary[pos] = {
    n: rs.length,
    lowEntryDelta: summary(rs.filter(r => r.lowEntryDelta !== null).map(r => r.lowEntryDelta)),
    lowExitDelta: summary(rs.filter(r => r.lowExitDelta !== null).map(r => r.lowExitDelta)),
    entrySpeed: summary(rs.filter(r => r.entrySpeed !== null).map(r => r.entrySpeed)),
    lowSpeed: summary(rs.filter(r => r.lowSpeed !== null).map(r => r.lowSpeed)),
    exitSpeed: summary(rs.filter(r => r.exitSpeed !== null).map(r => r.exitSpeed))
  };
}

const result = {
  counts: {
    markers: markers.length,
    videoEvents: video.length,
    velocityEvents: envEvents.length,
    fourPointVelocityEvents: four.length,
    weaponEvents: weapon.length,
    weaponWithContainingMainVideo: weaponRows.filter(r => r.videoId).length,
    weaponWithVelocity: weaponRows.filter(r => r.points).length,
    inferredSequences: sequences.length,
    multiShotSequences: sequences.filter(s => s.length > 1).length
  },
  fourPointRecipe: {
    entrySpeed: summary(four.map(x => x.entrySpeed)),
    lowSpeed: summary(four.map(x => x.lowSpeed)),
    entryRampSeconds: summary(four.map(x => x.entryRamp)),
    plateauSeconds: summary(four.map(x => x.plateau)),
    exitRampSeconds: summary(four.map(x => x.exitRamp)),
    postLastPointTailSeconds: summary(four.map(x => x.tail)),
    exitSpeed: summary(four.map(x => x.exitSpeed)),
    curves: Object.entries(four.reduce((o, x) => (o[x.curves] = (o[x.curves] || 0) + 1, o), {}))
      .sort((a, b) => b[1] - a[1])
  },
  weaponAlignment: {
    markerDeltaSeconds: summary(weaponRows.map(r => r.markerDelta)),
    markerAbsDeltaSeconds: summary(weaponRows.map(r => Math.abs(r.markerDelta))),
    exactWithinOneFrameAt29_97: weaponRows.filter(r => Math.abs(r.markerDelta) <= 1 / 29.97).length,
    videoStartDeltaSeconds: summary(weaponRows.filter(r => r.videoStartDelta !== null).map(r => r.videoStartDelta)),
    videoEndDeltaSeconds: summary(weaponRows.filter(r => r.videoEndDelta !== null).map(r => r.videoEndDelta)),
    nearestVelocityPointDeltaSeconds: summary(weaponRows.filter(r => r.pointDelta !== null).map(r => r.pointDelta)),
    nearestVelocityPointIndexCounts: Object.entries(weaponRows.reduce((o, r) =>
      (o[String(r.pointIndex)] = (o[String(r.pointIndex)] || 0) + 1, o), {})),
    relativeToLowEntrySeconds: summary(weaponRows.filter(r => r.lowEntryDelta !== null).map(r => r.lowEntryDelta)),
    relativeToLowExitSeconds: summary(weaponRows.filter(r => r.lowExitDelta !== null).map(r => r.lowExitDelta)),
    sourceOffsetSeconds: summary(weaponRows.map(r => r.sourceOffset))
  },
  positionSummary,
  sourceSequenceCounts: {
    sequences: sourceSequences.length,
    multiShotSequences: sourceSequences.filter(s => s.length > 1).length,
    lengths: sourceSequences.map(s => s.length)
  },
  sourcePositionSummary,
  commonEnvelopeShapes: Object.entries(shapes).sort((a, b) => b[1] - a[1]).slice(0, 20),
  sequences: sequences.map(s => ({length: s.length, start: s[0].t, end: s.at(-1).t})),
  weaponRows
};

console.log(JSON.stringify(result, null, 2));
