const fs = require('fs');
const data = JSON.parse(fs.readFileSync('C:/VEGAS/editor-2-project-01-analysis/raw/project-inspection-v1.json', 'utf8'));
const sep = String.fromCharCode(92);
const srcName = e => e.takes[0] ? e.takes[0].mediaPath.split(sep).pop() : null;

const t4 = data.tracks[4]; // main edit, 158 events
const t5 = data.tracks[5]; // audio, 107 events
const t6 = data.tracks[6]; // audio, 5 events
const t0 = data.tracks[0], t1 = data.tracks[1], t2 = data.tracks[2], t3 = data.tracks[3];

console.log('###### TRACKS 0,1,2,3 (small video tracks) ######');
for (const [idx, t] of [[0,t0],[1,t1],[2,t2],[3,t3]]) {
  console.log('--- track', idx, '---');
  for (const e of t.events) {
    console.log(' ', e.inspectorId, 'start='+e.startSeconds.toFixed(3), 'len='+e.lengthSeconds.toFixed(3),
      'src='+srcName(e), 'fx=['+e.effects.map(f=>f.description).join(',')+']');
  }
}

console.log();
console.log('###### TRACK 5 (audio, 107 events) - sources ######');
const t5SrcCounts = {};
for (const e of t5.events) {
  const s = srcName(e);
  t5SrcCounts[s] = (t5SrcCounts[s]||0)+1;
}
console.log(JSON.stringify(t5SrcCounts, null, 1));

console.log();
console.log('###### TRACK 6 (audio, 5 events) ######');
for (const e of t6.events) {
  console.log(' ', e.inspectorId, 'start='+e.startSeconds.toFixed(3), 'len='+e.lengthSeconds.toFixed(3), 'src='+srcName(e),
    'normalize='+e.normalize, 'gain='+e.normalizeGain, 'fadeIn='+JSON.stringify(e.fadeIn), 'fadeOut='+JSON.stringify(e.fadeOut));
}

console.log();
console.log('###### TRACK 4 velocity envelope shapes (first few + stats) ######');
const velShapes = {};
let velCount = 0;
for (const e of t4.events) {
  if (e.envelopes && e.envelopes.length > 0) {
    velCount++;
    const env = e.envelopes[0];
    const n = env.points.length;
    velShapes[n] = (velShapes[n]||0)+1;
  }
}
console.log('events with per-event envelope:', velCount, '/', t4.events.length);
console.log('point-count distribution:', JSON.stringify(velShapes));

console.log();
console.log('###### TRACK 4 source-run grouping ######');
const runs = [];
let curRun = null;
for (const e of t4.events) {
  const s = srcName(e);
  if (curRun && curRun.src === s) { curRun.events.push(e); }
  else { curRun = { src: s, events: [e] }; runs.push(curRun); }
}
console.log('total runs:', runs.length, 'multi-event runs:', runs.filter(r=>r.events.length>1).length);
console.log('run length distribution:', JSON.stringify(runs.reduce((acc,r)=>{acc[r.events.length]=(acc[r.events.length]||0)+1;return acc;},{})));

console.log();
console.log('###### TRACK 4 fade/overlap stats ######');
let overlaps = 0, hardcuts = 0, gaps=0;
const overlapDetail = [];
for (let i=1;i<t4.events.length;i++){
  const prev = t4.events[i-1], cur = t4.events[i];
  const delta = cur.startSeconds - prev.endSeconds;
  if (delta < -0.001) { overlaps++; overlapDetail.push({prevId:prev.inspectorId,curId:cur.inspectorId,overlapMs:(-delta*1000).toFixed(1), sameSource: srcName(prev)===srcName(cur)}); }
  else if (delta > 0.001) gaps++;
  else hardcuts++;
}
console.log('overlaps:', overlaps, 'hardcuts:', hardcuts, 'gaps:', gaps, 'total adjacencies:', t4.events.length-1);
const sameSourceOverlaps = overlapDetail.filter(o=>o.sameSource).length;
const diffSourceOverlaps = overlapDetail.filter(o=>!o.sameSource).length;
console.log('overlaps same-source:', sameSourceOverlaps, 'overlaps diff-source:', diffSourceOverlaps);

console.log();
console.log('###### TRACK 4 media role summary ######');
const roleCounts = {};
for (const e of t4.events) {
  const s = srcName(e);
  roleCounts[s] = (roleCounts[s]||0)+1;
}
console.log(JSON.stringify(roleCounts, null, 1));
