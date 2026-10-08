import {copyFile, mkdir, writeFile, access} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';

const root = new URL('../', import.meta.url);
await mkdir(new URL('public/art/', root), {recursive: true});
for (const file of ['mu-home-banner-new.png', 'mu-hero-art.png', 'mu-add-game-art.png', 'amd_dlss_mu_logo.png']) {
  await copyFile(new URL(`../../assets/${file}`, root), new URL(`public/art/${file}`, root));
}
console.log(`Prepared MU brand assets in ${fileURLToPath(new URL('public/art/', root))}`);
await mkdir(new URL('public/fonts/', root), {recursive: true});
const fonts = {
  'noto-sc-bold.ttf': 'https://fonts.gstatic.com/s/notosanssc/v41/k3kCo84MPvpLmixcA63oeAL7Iqp5IZJF9bmaGzjCnYw.ttf',
  'rajdhani-bold.ttf': 'https://fonts.gstatic.com/s/rajdhani/v17/LDI2apCSOBg7S-QT7pa8FsOs.ttf',
  'noto-OFL.txt': 'https://raw.githubusercontent.com/google/fonts/main/ofl/notosanssc/OFL.txt',
  'rajdhani-OFL.txt': 'https://raw.githubusercontent.com/google/fonts/main/ofl/rajdhani/OFL.txt',
};
for (const [name, url] of Object.entries(fonts)) {
  const dest = new URL(`public/fonts/${name}`, root);
  try {await access(dest); continue;} catch {}
  const response = await fetch(url);
  if (!response.ok) throw new Error(`Font download failed: ${response.status} ${name}`);
  await writeFile(dest, new Uint8Array(await response.arrayBuffer()));
  console.log(`Prepared ${name}`);
}
