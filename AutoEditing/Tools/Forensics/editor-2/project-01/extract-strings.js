const fs = require('fs');
const buf = fs.readFileSync('C:/VEGAS/Glovali Montage 5/Glovali Montage 5/Untitled.veg');
const found = new Set();

// ASCII strings (min length 5)
let cur = '';
for (let i = 0; i < buf.length; i++) {
  const c = buf[i];
  if (c >= 32 && c < 127) { cur += String.fromCharCode(c); }
  else { if (cur.length >= 5) found.add(cur); cur = ''; }
}
if (cur.length >= 5) found.add(cur);

// UTF-16LE strings (min length 5 chars)
cur = '';
for (let i = 0; i + 1 < buf.length; i += 2) {
  const lo = buf[i], hi = buf[i + 1];
  if (hi === 0 && lo >= 32 && lo < 127) { cur += String.fromCharCode(lo); }
  else { if (cur.length >= 5) found.add(cur); cur = ''; }
}
if (cur.length >= 5) found.add(cur);

const media = [...found].filter(s => /\.(avi|mp4|mp3|flac|png|jpg|jpeg|wav)$/i.test(s));
console.log('media-like strings found:', media.length);
console.log([...new Set(media)].sort().join('\n'));
