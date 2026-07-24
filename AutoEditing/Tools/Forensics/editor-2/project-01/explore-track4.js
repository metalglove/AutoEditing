const fs = require('fs');
const data = JSON.parse(fs.readFileSync('C:/VEGAS/editor-2-project-01-analysis/raw/project-inspection-v1.json', 'utf8'));
const sep = String.fromCharCode(92);
const t4 = data.tracks[4];
console.log('track4 envelope:', JSON.stringify(t4.envelopes));
console.log();
console.log('total events:', t4.events.length);

// effect signature summary
const chainCounts = {};
for (const e of t4.events) {
  const chain = e.effects.map(fx => fx.description).join('>');
  chainCounts[chain || '(none)'] = (chainCounts[chain || '(none)']||0)+1;
}
console.log('=== effect chain distribution ===');
console.log(JSON.stringify(chainCounts, null, 1));

console.log();
console.log('=== first 20 events summary ===');
for (const e of t4.events.slice(0, 20)) {
  const src = e.takes[0] ? e.takes[0].mediaPath.split(sep).pop() : null;
  console.log(e.inspectorId, 'start='+e.startSeconds.toFixed(3), 'len='+e.lengthSeconds.toFixed(3), 'src='+src,
    'fx=['+e.effects.map(f=>f.description).join(',')+']',
    'velocityEnv='+(e.envelopes.length>0));
}
