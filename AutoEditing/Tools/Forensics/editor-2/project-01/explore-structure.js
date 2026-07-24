const fs = require('fs');
const data = JSON.parse(fs.readFileSync('C:/VEGAS/editor-2-project-01-analysis/raw/project-inspection-v1.json', 'utf8'));

console.log('=== PROJECT ===');
console.log('vegasVersion:', data.vegasVersion, 'build:', data.vegasBuildNumber);
console.log('lengthSeconds:', data.lengthSeconds);
console.log('video:', JSON.stringify(data.video));
console.log('audio:', JSON.stringify(data.audio));
console.log('ruler:', JSON.stringify(data.ruler));
console.log('masterBus effects/envelopes:', data.masterBus.effects.length, data.masterBus.envelopes.length);
console.log('videoBus effects/envelopes:', data.videoBus.effects.length, data.videoBus.envelopes.length);
console.log('markers:', data.markers.length, 'regions:', data.regions.length);

console.log();
console.log('=== TRACKS ===');
for (const t of data.tracks) {
  console.log('track', t.index, t.isVideo ? 'VIDEO' : 'AUDIO', 'name=' + t.name, 'mute=' + t.mute,
    t.isVideo ? ('compositeMode=' + t.compositeMode + ' compositeLevel=' + t.compositeLevel) : ('volume=' + t.volume),
    'trackEffects=' + t.effects.length, 'trackEnvelopes=' + t.envelopes.length, 'events=' + t.events.length);
  for (const fx of t.effects) console.log('   trackFX:', fx.description, 'bypass=' + fx.bypass);
}

console.log();
console.log('=== MEDIA (46 items) ===');
const sep = String.fromCharCode(92);
for (const m of data.media) {
  console.log(m.index, m.filePath.split(sep).pop(), 'offline=' + m.isOffline, 'hasVideo=' + m.hasVideo, 'hasAudio=' + m.hasAudio, 'useCount=' + m.useCount, m.isSubclip ? 'SUBCLIP' : '');
}
