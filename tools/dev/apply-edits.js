// Applies edit blocks to a target file. All-or-nothing: nothing is written unless every edit matches.
//   ===OLD===     exact substring, must occur exactly once
//   ===LINE===    prefix of a single line, must start exactly one line; the whole line is replaced
//   ===SECTION=== prefix of a heading line; replaced through the line before the next "---" or heading
const fs = require('fs');
const [editsPath, targetPath] = process.argv.slice(2);
const edits = fs.readFileSync(editsPath, 'utf8').replace(/\r\n/g, '\n');
let doc = fs.readFileSync(targetPath, 'utf8');
const re = /===(OLD|LINE|SECTION)===\n([\s\S]*?)\n===NEW===\n([\s\S]*?)\n===END===/g;
const blocks = [];
let m;
while ((m = re.exec(edits))) blocks.push({ kind: m[1], key: m[2], val: m[3] });
let bad = 0, n = 0;
for (const b of blocks) {
  n++;
  if (b.kind === 'OLD') {
    const c = doc.split(b.key).length - 1;
    if (c !== 1) { bad++; console.log(`#${n} OLD: ${c} matches: ${b.key.slice(0, 80)}`); continue; }
    doc = doc.split(b.key).join(b.val);
  } else {
    const lines = doc.split('\n');
    const idx = lines.map((l, i) => (l.startsWith(b.key) ? i : -1)).filter(i => i >= 0);
    if (idx.length !== 1) { bad++; console.log(`#${n} ${b.kind}: ${idx.length} matches: ${b.key.slice(0, 80)}`); continue; }
    const s = idx[0];
    let e = s + 1;
    if (b.kind === 'SECTION') {
      while (e < lines.length && !/^(---\s*$|#{1,3} )/.test(lines[e])) e++;
      lines.splice(s, e - s, ...b.val.split('\n'), '');
    } else {
      lines.splice(s, 1, ...b.val.split('\n'));
    }
    doc = lines.join('\n');
  }
}
if (bad) { console.log(`ABORT: ${bad} of ${blocks.length} edits failed; nothing written`); process.exit(1); }
fs.writeFileSync(targetPath, doc, 'utf8');
console.log(`applied ${blocks.length} edits`);
