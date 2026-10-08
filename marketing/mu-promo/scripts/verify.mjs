import {execFileSync} from 'node:child_process';
import {stat, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';

const outputs = [
  ['mu-promo-16x9.mp4', 1920, 1080],
  ['mu-promo-9x16.mp4', 1080, 1920],
];
const reports = [];
for (const [name, width, height] of outputs) {
  const file = fileURLToPath(new URL(`../out/${name}`, import.meta.url));
  const media = JSON.parse(execFileSync('ffprobe', ['-v', 'error', '-show_streams', '-show_format', '-of', 'json', file], {encoding: 'utf8'}));
  const video = media.streams.find(s => s.codec_type === 'video');
  const audio = media.streams.find(s => s.codec_type === 'audio');
  // FFprobe names full-range 8-bit 4:2:0 H.264 as yuvj420p.
  if (!video || video.width !== width || video.height !== height || video.r_frame_rate !== '60/1' || Number(video.nb_frames) !== 1800 || !['yuv420p', 'yuvj420p'].includes(video.pix_fmt)) throw new Error(`Invalid video format: ${name}`);
  if (!audio || audio.codec_name !== 'aac' || audio.channels !== 2) throw new Error(`Invalid audio: ${name}`);
  if (Math.abs(Number(media.format.duration) - 30) > .1) throw new Error(`Invalid duration: ${name}`);
  execFileSync('ffmpeg', ['-v', 'error', '-i', file, '-f', 'null', '-'], {stdio: 'pipe'});
  const report = {file: name, width, height, fps: 60, frames: 1800, duration: Number(media.format.duration), videoCodec: video.codec_name, pixelFormat: video.pix_fmt, colorRange: video.color_range, audioCodec: audio.codec_name, audioChannels: audio.channels, bytes: (await stat(file)).size, sha256: createHash('sha256').update(readFileSync(file)).digest('hex'), decode: 'passed'};
  reports.push(report);
  console.log(`${name}: ${width}x${height}, 60 fps, 30 s, H.264 + stereo AAC; complete decode passed.`);
}
await writeFile(new URL('../out/verification.json', import.meta.url), JSON.stringify({verifiedAt: new Date().toISOString(), outputs: reports}, null, 2));
