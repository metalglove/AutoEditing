const fs = require('fs');
const data = JSON.parse(fs.readFileSync('C:/VEGAS/editor-2-project-01-analysis/raw/project-inspection-v1.json', 'utf8'));
const sep = String.fromCharCode(92);
const srcName = e => e.takes[0] ? e.takes[0].mediaPath.split(sep).pop() : null;
const t4 = data.tracks[4], t5 = data.tracks[5];
const markers = data.markers.map(m => m.timeSeconds).sort((a,b)=>a-b);

function nearestMarkerDelta(t) {
  let best = Infinity;
  for (const m of markers) { const d = Math.abs(m - t); if (d < best) best = d; }
  return best;
}
function nearestT4Delta(t) {
  let best = Infinity, bestE = null;
  for (const e of t4.events) { const d = Math.abs(e.startSeconds - t); if (d < best) { best = d; bestE = e; } }
  return { delta: best, event: bestE };
}

const hitEvents = t5.events.filter(e => srcName(e) === 'SA-B 50 Hit.mp3');
console.log('SA-B 50 Hit.mp3 events:', hitEvents.length);
for (const e of hitEvents) {
  const md = nearestMarkerDelta(e.startSeconds);
  const t4n = nearestT4Delta(e.startSeconds);
  console.log(' ', e.inspectorId, 'start='+e.startSeconds.toFixed(3), 'len='+e.lengthSeconds.toFixed(3),
    'nearestMarkerDeltaMs='+(md*1000).toFixed(1),
    'nearestT4EventDeltaMs='+(t4n.delta*1000).toFixed(1), 'nearestT4Event='+(t4n.event?t4n.event.inspectorId:null),
    'normalize='+e.normalize, 'gain='+e.normalizeGain,
    'fx='+JSON.stringify(e.effects.map(f=>f.description)));
}

console.log();
console.log('=== All 251 markers: spacing stats ===');
const iois = [];
for (let i=1;i<markers.length;i++) iois.push(markers[i]-markers[i-1]);
iois.sort((a,b)=>a-b);
console.log('min/median/max IOI (s):', iois[0].toFixed(3), iois[Math.floor(iois.length/2)].toFixed(3), iois[iois.length-1].toFixed(3));

console.log();
console.log('=== last few / first few t4 events (outro / intro structure) ===');
for (const e of t4.events.slice(-8)) {
  console.log(' ', e.inspectorId, 'start='+e.startSeconds.toFixed(3), 'len='+e.lengthSeconds.toFixed(3), 'src='+srcName(e), 'fx='+JSON.stringify(e.effects.map(f=>f.description)));
}

console.log();
console.log('=== fadeIn/fadeOut curve check across all t4 overlaps ===');
for (let i=1;i<t4.events.length;i++){
  const prev = t4.events[i-1], cur = t4.events[i];
  const delta = cur.startSeconds - prev.endSeconds;
  if (delta < -0.001) {
    console.log(prev.inspectorId, '->', cur.inspectorId, 'overlapMs='+(-delta*1000).toFixed(1),
      'prevFadeOut='+JSON.stringify(prev.fadeOut), 'curFadeIn='+JSON.stringify(cur.fadeIn));
  }
}
