import {createServer} from 'node:http';
import {createReadStream} from 'node:fs';
import {stat} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import path from 'node:path';

const root = path.resolve(fileURLToPath(new URL('../', import.meta.url)));
const types = {'.html': 'text/html; charset=utf-8', '.mp4': 'video/mp4', '.jpg': 'image/jpeg', '.png': 'image/png'};
createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname);
    const file = path.resolve(root, '.' + (pathname === '/' ? '/preview.html' : pathname));
    if (!file.startsWith(root + path.sep)) {response.writeHead(403).end(); return;}
    const info = await stat(file);
    if (!info.isFile()) {response.writeHead(404).end(); return;}
    const headers = {'Content-Type': types[path.extname(file)] ?? 'application/octet-stream', 'Accept-Ranges': 'bytes'};
    const range = request.headers.range;
    if (range) {
      const match = /^bytes=(\d+)-(\d*)$/.exec(range);
      if (!match) {response.writeHead(416).end(); return;}
      const start = Number(match[1]);
      const end = match[2] ? Math.min(Number(match[2]), info.size - 1) : info.size - 1;
      if (start > end || start >= info.size) {response.writeHead(416, {'Content-Range': `bytes */${info.size}`}).end(); return;}
      response.writeHead(206, {...headers, 'Content-Length': end - start + 1, 'Content-Range': `bytes ${start}-${end}/${info.size}`});
      createReadStream(file, {start, end}).pipe(response);
    } else {
      response.writeHead(200, {...headers, 'Content-Length': info.size});
      createReadStream(file).pipe(response);
    }
  } catch {response.writeHead(404).end();}
}).listen(4189, '127.0.0.1', () => console.log('MU promo preview: http://127.0.0.1:4189'));
