import {bundle} from '@remotion/bundler';
import {renderMedia, renderStill, selectComposition} from '@remotion/renderer';
import {mkdir, writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
const out = path.join(root, 'out');
const mode = process.argv[2] ?? 'all';
await mkdir(out, {recursive: true});
const serveUrl = await bundle({entryPoint: path.join(root, 'src/index.tsx'), publicDir: path.join(root, 'public')});
const compositions = ['MU-Landscape', 'MU-Portrait'];
const report = [];
for (const id of compositions) {
  const composition = await selectComposition({serveUrl, id});
  if (mode === 'stills') {
    for (const frame of [112, 345, 620, 985, 1285, 1490, 1695]) {
      await renderStill({serveUrl, composition, frame, output: path.join(out, `${id}-${frame}.jpg`), imageFormat: 'jpeg', scale: .6, jpegQuality: 90});
      console.log(`${id}: still ${frame}`);
    }
    continue;
  }
  if (mode === 'preview') {
    await renderMedia({serveUrl, composition, outputLocation: path.join(out, `${id}-preview.mp4`), codec: 'h264', scale: .5, crf: 25, concurrency: 4});
    continue;
  }
  if (mode !== 'all' && mode !== id) continue;
  let lastPercent = -1;
  const outputLocation = path.join(out, `${id === 'MU-Landscape' ? 'mu-promo-16x9' : 'mu-promo-9x16'}.mp4`);
  await renderMedia({
    serveUrl, composition, outputLocation, codec: 'h264', pixelFormat: 'yuv420p',
    crf: 18, audioCodec: 'aac', audioBitrate: '320k', concurrency: 4,
    onProgress: ({progress}) => {
      const percent = Math.floor(progress * 100 / 10) * 10;
      if (percent !== lastPercent) {lastPercent = percent; console.log(`${id}: ${percent}%`);}
    },
  });
  report.push({id, outputLocation, width: composition.width, height: composition.height, fps: composition.fps, frames: composition.durationInFrames});
  console.log(`Rendered ${outputLocation}`);
}
if (report.length) await writeFile(path.join(out, 'render-report.json'), JSON.stringify(report, null, 2));
