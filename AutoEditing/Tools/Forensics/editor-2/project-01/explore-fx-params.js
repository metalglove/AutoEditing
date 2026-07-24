const fs = require('fs');
const data = JSON.parse(fs.readFileSync('C:/VEGAS/editor-2-project-01-analysis/raw/project-inspection-v1.json', 'utf8'));
const sep = String.fromCharCode(92);
const srcName = e => e.takes[0] ? e.takes[0].mediaPath.split(sep).pop() : null;
const t4 = data.tracks[4];

// Find a representative S_Shake-only event and print full OFX param detail
const shakeEvt = t4.events.find(e => e.effects.length===1 && e.effects[0].description==='S_Shake');
console.log('=== Sample S_Shake-only event:', shakeEvt.inspectorId, 'start='+shakeEvt.startSeconds, 'len='+shakeEvt.lengthSeconds, '===');
const shakeFx = shakeEvt.effects[0];
for (const p of shakeFx.ofxParameters) {
  if (p.isAnimated || (p.keyframes && p.keyframes.length)) {
    console.log(' ANIMATED', p.name, JSON.stringify(p.keyframes));
  }
}
console.log('static (non-animated) double params:', shakeFx.ofxParameters.filter(p=>p.parameterType==='Double' && !p.isAnimated).map(p=>p.name+'='+p.value).join(', '));
console.log('static boolean params:', shakeFx.ofxParameters.filter(p=>p.parameterType==='Boolean').map(p=>p.name+'='+p.value).join(', '));

// Find a representative S_FilmDamage event
const fdEvt = t4.events.find(e => e.effects.length===1 && e.effects[0].description==='S_FilmDamage');
console.log();
console.log('=== Sample S_FilmDamage-only event:', fdEvt.inspectorId, 'start='+fdEvt.startSeconds, 'len='+fdEvt.lengthSeconds, 'src='+srcName(fdEvt), '===');
const fdFx = fdEvt.effects[0];
for (const p of fdFx.ofxParameters) {
  if (p.isAnimated || (p.keyframes && p.keyframes.length)) {
    console.log(' ANIMATED', p.name, JSON.stringify(p.keyframes));
  }
}
console.log('static double params:', fdFx.ofxParameters.filter(p=>p.parameterType==='Double' && !p.isAnimated).map(p=>p.name+'='+p.value).join(', '));

// velocity 7-point sample
const vel7 = t4.events.find(e => e.envelopes.length && e.envelopes[0].points.length===7);
console.log();
console.log('=== Sample 7-point velocity envelope:', vel7.inspectorId, '===');
console.log(JSON.stringify(vel7.envelopes[0].points));

// gap location
for (let i=1;i<t4.events.length;i++){
  const prev = t4.events[i-1], cur = t4.events[i];
  const delta = cur.startSeconds - prev.endSeconds;
  if (delta > 0.001) console.log('GAP:', prev.inspectorId, '->', cur.inspectorId, 'gap='+delta.toFixed(3)+'s at '+prev.endSeconds.toFixed(3));
}

// missing plugin scan (pluginAvailable false)
console.log();
console.log('=== missing/unavailable plugin scan across all tracks ===');
for (const t of data.tracks) {
  for (const e of t.events) {
    for (const fx of e.effects) {
      if (fx.pluginAvailable === false) console.log('MISSING in event', e.inspectorId, ':', fx.description, JSON.stringify(fx.plugin));
    }
  }
  for (const fx of t.effects) {
    if (fx.pluginAvailable === false) console.log('MISSING at track level, track', t.index, ':', fx.description);
  }
}
console.log('(scan complete)');

// S_Shake Motion Blur check
console.log();
console.log('=== Motion Blur param on S_Shake ===');
const mb = shakeFx.ofxParameters.find(p => p.name === 'Motion Blur');
console.log(mb ? JSON.stringify(mb) : 'not found by that name; listing all param names:');
if (!mb) console.log(shakeFx.ofxParameters.map(p=>p.name).join(', '));
