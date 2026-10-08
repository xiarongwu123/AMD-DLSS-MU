import React, {useEffect, useState} from 'react';
import {AbsoluteFill, Html5Audio, Img, Sequence, continueRender, delayRender, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig} from 'remotion';

const ACID = '#adff18';
const INK = '#f2f7fb';
const MUTED = '#94a1af';
const BG = '#05080d';
const CUTS = [0, 225, 450, 787, 1125, 1350, 1575, 1800];
const sans = '"MU Sans", "PingFang SC", "Microsoft YaHei", sans-serif';
const tech = '"MU Display", "Arial Narrow", sans-serif';
const clamp = (f: number, a: number, b: number, x: number, y: number) => interpolate(f, [a, b], [x, y], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
const mod = (a: number, b: number) => ((a % b) + b) % b;
const hash = (n: number) => mod(Math.sin(n * 127.1 + 311.7) * 43758.5453, 1);

const FontGate = () => {
  const [handle] = useState(() => delayRender('Load packaged typography'));
  useEffect(() => {
    Promise.all([
      new FontFace('MU Sans', `url(${staticFile('fonts/noto-sc-bold.ttf')})`, {weight: '100 900'}).load(),
      new FontFace('MU Display', `url(${staticFile('fonts/rajdhani-bold.ttf')})`, {weight: '100 900'}).load(),
    ]).then(fonts => {fonts.forEach(font => document.fonts.add(font)); continueRender(handle);});
  }, [handle]);
  return null;
};

const Label = ({children, style}: {children: React.ReactNode; style?: React.CSSProperties}) => <div style={{fontFamily: tech, fontSize: 24, fontWeight: 700, letterSpacing: 5, color: ACID, ...style}}>{children}</div>;

const Reveal = ({children, delay = 0, style}: {children: React.ReactNode; delay?: number; style?: React.CSSProperties}) => {
  const f = useCurrentFrame();
  const p = spring({frame: f - delay, fps: 60, config: {damping: 22, stiffness: 140, mass: .85}});
  return <div style={{opacity: clamp(f, delay, delay + 12, 0, 1), transform: `translateY(${(1 - p) * 48}px)`, ...style}}>{children}</div>;
};

const Title = ({first, second, delay = 10, size = 106, style}: {first: string; second?: string; delay?: number; size?: number; style?: React.CSSProperties}) => <div style={{fontWeight: 900, fontSize: size, letterSpacing: -4, lineHeight: 1.16, ...style}}>
  <Reveal delay={delay}>{first}</Reveal>
  {second && <Reveal delay={delay + 9} style={{color: ACID}}>{second}</Reveal>}
</div>;

const Logo = ({width = 500}: {width?: number}) => <div style={{width, height: width * .5, overflow: 'hidden', position: 'relative'}}>
  <Img src={staticFile('art/amd_dlss_mu_logo.png')} style={{position: 'absolute', width, top: -width * .105, left: 0}} />
</div>;

const Brand = () => <div style={{display: 'flex', alignItems: 'center', gap: 16}}><div style={{height: 39, overflow: 'hidden'}}><Logo width={64} /></div><span style={{fontFamily: tech, fontSize: 25, letterSpacing: 3, fontWeight: 700}}>AMD DLSS <span style={{color: ACID}}>MU</span></span></div>;

const Grid = ({opacity = .35}: {opacity?: number}) => {
  const f = useCurrentFrame();
  return <AbsoluteFill style={{opacity, backgroundImage: 'linear-gradient(#adff1813 1px, transparent 1px), linear-gradient(90deg, #adff1813 1px, transparent 1px)', backgroundSize: '72px 72px', transform: `perspective(800px) rotateX(60deg) translateY(${f % 72}px) scale(1.8)`, transformOrigin: 'center 90%'}} />;
};

const Particles = ({count = 42}: {count?: number}) => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  return <AbsoluteFill style={{pointerEvents: 'none'}}>{Array.from({length: count}, (_, i) => {
    const x = mod(hash(i + 1) * w + f * (hash(i + 2) - .5) * .5, w);
    const y = mod(hash(i + 9) * h - f * (.35 + hash(i + 4)), h);
    return <div key={i} style={{position: 'absolute', left: x, top: y, width: i % 7 === 0 ? 3 : 2, height: i % 7 === 0 ? 18 : 2, background: ACID, opacity: .15 + hash(i) * .5, boxShadow: `0 0 12px ${ACID}`}} />;
  })}</AbsoluteFill>;
};

const Chrome = ({chapter, number}: {chapter: string; number: string}) => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const v = h > w;
  const margin = v ? 72 : 92;
  return <AbsoluteFill style={{pointerEvents: 'none'}}>
    <div style={{position: 'absolute', left: margin, top: v ? 102 : 56}}><Brand /></div>
    <Label style={{position: 'absolute', right: margin, top: v ? 160 : 60, color: MUTED, fontSize: 20, letterSpacing: 3}}>{number} / {chapter}</Label>
    <div style={{position: 'absolute', left: margin, right: margin, bottom: v ? 110 : 46, height: 2, background: '#ffffff15'}}><div style={{width: `${f / 1800 * 100}%`, height: '100%', background: ACID}} /></div>
    <Label style={{position: 'absolute', left: margin, bottom: v ? 140 : 70, fontSize: 17, color: '#839497', letterSpacing: 3}}>GAME GRAPHICS CONFIGURATION</Label>
    <Label style={{position: 'absolute', right: margin, bottom: v ? 140 : 70, fontSize: 17, color: '#839497', letterSpacing: 3}}>MU / {String(Math.floor(f / 60)).padStart(2, '0')} : {String(f % 60).padStart(2, '0')}</Label>
    {[[-1, -1], [1, -1], [-1, 1], [1, 1]].map(([x, y], i) => <div key={i} style={{position: 'absolute', left: x < 0 ? 32 : undefined, right: x > 0 ? 32 : undefined, top: y < 0 ? 32 : undefined, bottom: y > 0 ? 32 : undefined, width: 24, height: 24, borderLeft: x < 0 ? '2px solid #adff1870' : undefined, borderRight: x > 0 ? '2px solid #adff1870' : undefined, borderTop: y < 0 ? '2px solid #adff1870' : undefined, borderBottom: y > 0 ? '2px solid #adff1870' : undefined}} />)}
  </AbsoluteFill>;
};

// Frame-driven perspective geometry makes the flythrough reproducible at any frame.
const GameWorld = ({variant = 0, time, compact = false}: {variant?: number; time?: number; compact?: boolean}) => {
  const frame = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const t = time ?? frame / 60;
  const vw = 1920, vh = 1080;
  const green = variant === 1 ? '#6fefff' : ACID;
  const horizon = 438 + Math.sin(t * .65) * 12;
  const camX = Math.sin(t * .48) * 15;
  const project = (x: number, y: number, z: number): [number, number] => [960 + (x - camX) * 820 / z, horizon + (20 - y) * 820 / z];
  const pts = (vertices: [number, number, number][]) => vertices.map(v => project(...v).map(n => n.toFixed(1)).join(',')).join(' ');
  const boxes: React.ReactNode[] = [];
  const depthOrder = Array.from({length: 24}, (_, index) => ({index, z: mod(index * 56 - t * 72, 1344) + 36})).sort((a, b) => b.z - a.z);
  for (const {index, z} of depthOrder) {
    for (const sign of [-1, 1]) {
      const n = index + (sign > 0 ? 42 : 90) + variant * 19;
      const x = sign * (74 + hash(n) * 68);
      const bw = 24 + hash(n + 1) * 33;
      const bh = 35 + hash(n + 2) * 215;
      const bx = x + sign * bw;
      const opacity = Math.min(1, 800 / z);
      boxes.push(<g key={`${index}-${sign}`} opacity={opacity}>
        <polygon points={pts([[x, 0, z], [x, bh, z], [bx, bh, z], [bx, 0, z]])} fill={sign < 0 ? '#09151e' : '#101922'} stroke='#235047' strokeWidth={1.3} />
        <polygon points={pts([[x, 0, z], [x, bh, z], [x, bh, z + 34], [x, 0, z + 34]])} fill='#07111a' stroke='#214138' strokeWidth={1.2} />
        <polygon points={pts([[x, bh, z], [bx, bh, z], [bx, bh, z + 34], [x, bh, z + 34]])} fill='#142821' stroke='#315346' />
        {Array.from({length: 7}, (_, j) => <polyline key={j} points={pts([[x, bh * (.1 + j * .12), z - .1], [bx, bh * (.1 + j * .12), z - .1]])} fill='none' stroke={green} strokeWidth={j % 3 === 0 ? 2 : 1} opacity={j % 3 === 0 ? .52 : .12} />)}
        {Array.from({length: 4}, (_, j) => <polyline key={`window${j}`} points={pts([[x + (bx - x) * (.15 + j * .22), bh * .2, z - .2], [x + (bx - x) * (.15 + j * .22), bh * .82, z - .2]])} fill='none' stroke={j % 2 ? '#b9e8d2' : green} strokeWidth={3.5} strokeDasharray='4 7 4 12' opacity={.17 + hash(n + j) * .2} />)}
        <polyline points={pts([[x, 0, z - .2], [x, bh, z - .2]])} stroke={green} strokeWidth={3} fill='none' opacity={.7} />
        {index % 5 === 0 && <polygon points={pts([[x, bh * .6, z - .3], [bx, bh * .6, z - .3], [bx, bh * .8, z - .3], [x, bh * .8, z - .3]])} fill={green} opacity={.65} />}
      </g>);
    }
  }
  const rails = [-68, -64, -32, 0, 32, 64, 68];
  return <svg viewBox={`0 0 ${vw} ${vh}`} preserveAspectRatio='xMidYMid slice' width='100%' height='100%' style={{position: 'absolute', inset: 0}}>
    <defs>
      <linearGradient id={`sky${variant}`} x2='0' y2='1'><stop stopColor='#02050b' /><stop offset='.56' stopColor='#07312c' /><stop offset='1' stopColor='#05090c' /></linearGradient>
      <radialGradient id={`sun${variant}`}><stop stopColor={green} stopOpacity='.32' /><stop offset='1' stopColor={green} stopOpacity='0' /></radialGradient>
      <linearGradient id={`road${variant}`} x2='0' y2='1'><stop stopColor='#11362f' /><stop offset='.65' stopColor='#09121c' /><stop offset='1' stopColor='#02040a' /></linearGradient>
      <radialGradient id={`fog${variant}`}><stop stopColor={green} stopOpacity='.19' /><stop offset='1' stopColor={green} stopOpacity='0' /></radialGradient>
    </defs>
    <rect width={vw} height={vh} fill={`url(#sky${variant})`} />
    <ellipse cx={1000} cy={340} rx={700} ry={360} fill={`url(#sun${variant})`} />
    <circle cx={1150} cy={245} r={138} fill='#12362e' stroke='#9af3b7' strokeOpacity='.25' />
    <circle cx={1134} cy={223} r={135} fill='#061318' />
    {Array.from({length: 44}, (_, i) => <circle key={i} cx={hash(i + 99) * vw} cy={hash(i + 212) * 400} r={hash(i + 81) * 1.7 + .5} fill='#cfefe3' opacity={hash(i + 231) * .5} />)}
    <polygon points='0,540 0,422 95,371 156,427 269,319 335,408 400,369 524,453 590,380 703,455 902,421 1100,464 1392,363 1460,389 1560,301 1650,378 1740,350 1920,406 1920,540' fill='#0b242a' />
    <rect y={horizon} width={vw} height={vh} fill={`url(#road${variant})`} />
    {boxes}
    {rails.map((x, i) => <polyline key={i} points={pts([[x, 0, 1600], [x, 0, 8]])} fill='none' stroke={green} strokeWidth={i === 0 || i === 6 ? 7 : 1.5} opacity={i === 0 || i === 6 ? .8 : .28} />)}
    {Array.from({length: 26}, (_, i) => {
      const z = mod(i * 48 - t * 72, 1248) + 10;
      return <polyline key={i} points={pts([[-68, 0, z], [68, 0, z]])} stroke={green} strokeWidth={1.6} opacity={Math.min(.42, 200 / z)} />;
    })}
    {Array.from({length: 20}, (_, i) => {
      const z = mod(i * 53 - t * 115, 1060) + 12;
      const lane = i % 2 ? -35 : 35;
      return <polyline key={i} points={pts([[lane, .4, z], [lane, .4, z + 24]])} fill='none' stroke={green} strokeWidth={4} opacity={.55} />;
    })}
    <ellipse cx='960' cy={horizon} rx='600' ry='130' fill={`url(#fog${variant})`} />
    <line x1='640' x2='1280' y1={horizon} y2={horizon} stroke={green} strokeWidth='2' opacity='.3' />
    {!compact && <g transform={`translate(${Math.sin(t * 1.7) * 23},${Math.cos(t * 1.1) * 7})`}>
      <ellipse cx='960' cy='978' rx='175' ry='22' fill={green} opacity='.08' />
      <polygon points='730,980 800,905 907,871 960,893 1013,871 1120,905 1190,980 1034,956 960,966 886,956' fill='#0d1822' stroke='#4b7765' strokeWidth='2' />
      <polygon points='840,945 960,888 1080,945 1000,936 960,949 920,936' fill='#142a35' />
      <polyline points='752,971 892,946 926,953' stroke={green} strokeWidth='8' fill='none' />
      <polyline points='1168,971 1028,946 994,953' stroke={green} strokeWidth='8' fill='none' />
      <path d='M900 968 L930 1035 L960 973 L990 1035 L1020 968' fill={green} opacity={.3 + Math.sin(t * 45) * .1} />
    </g>}
    <rect width={vw} height={vh} fill='url(#vignette)' />
  </svg>;
};

const Opening = () => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const v = h > w;
  const z = clamp(f, 0, 225, 1.14, 1.02);
  return <AbsoluteFill>
    <Img src={staticFile('art/mu-hero-art.png')} style={{width: w, height: h, objectFit: 'cover', objectPosition: v ? '72% center' : 'center', transform: `scale(${z})`, opacity: clamp(f, 0, 24, .25, 1)}} />
    <AbsoluteFill style={{background: v ? 'linear-gradient(180deg,#05080dcc,transparent 50%,#05080dee)' : 'linear-gradient(90deg,#05080df0 3%,#05080d70 42%,transparent 70%)'}} />
    <Particles />
    <div style={{position: 'absolute', left: v ? 78 : 136, top: v ? 295 : 306}}>
      <Reveal delay={8}><Label>CONTROL YOUR NEXT GAME</Label></Reveal>
      <Title first='下一局' second='由你掌控' size={v ? 122 : 130} delay={18} style={{marginTop: 26}} />
      <Reveal delay={39} style={{fontSize: 29, marginTop: 32, color: '#b2c3c5', letterSpacing: 5}}>AMD DLSS MU</Reveal>
    </div>
    <div style={{position: 'absolute', right: v ? 80 : 118, top: v ? 1220 : 650, display: 'flex', gap: 18, alignItems: 'center', opacity: clamp(f, 45, 70, 0, 1)}}>
      <div style={{width: 7, height: 54, background: ACID, boxShadow: `0 0 24px ${ACID}`}} />
      <div><Label style={{color: INK, fontSize: 30, letterSpacing: 3}}>SYSTEM / MU</Label><Label style={{fontSize: 18, color: MUTED, letterSpacing: 4}}>GAME GRAPHICS TOOLKIT</Label></div>
    </div>
  </AbsoluteFill>;
};

const Play = () => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const v = h > w;
  return <AbsoluteFill>
    <GameWorld time={f / 60 + 1} variant={f > 112 ? 1 : 0} />
    <AbsoluteFill style={{background: 'linear-gradient(180deg,#05080d9c 0%,transparent 44%,#05080d96 100%)'}} />
    <div style={{position: 'absolute', left: v ? 78 : 130, top: v ? 295 : 200}}>
      <Reveal><Label>LESS SETUP. MORE PLAY.</Label></Reveal>
      <Title first='把注意力' second='留给游戏' size={v ? 108 : 100} />
    </div>
    <div style={{position: 'absolute', left: v ? 78 : 130, bottom: v ? 290 : 162, display: 'flex', flexDirection: v ? 'column' : 'row', gap: 16}}>
      {['超分配置', '帧生成配置', '窗口缩放'].map((s, i) => <Reveal key={s} delay={25 + i * 15} style={{padding: '15px 28px', borderLeft: `3px solid ${ACID}`, background: '#05080dcc', fontSize: 27, letterSpacing: 3}}>{s}</Reveal>)}
    </div>
    <Label style={{position: 'absolute', right: v ? 78 : 130, bottom: v ? 224 : 117, fontSize: 17, color: '#89a3a1', letterSpacing: 1}}>原创场景 · 视觉演绎</Label>
  </AbsoluteFill>;
};

const Library = () => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const v = h > w;
  const select = f > 104;
  const panelW = v ? 920 : 1510;
  const panelH = v ? 970 : 545;
  return <AbsoluteFill>
    <Grid opacity={.2} /><Particles count={24} />
    <div style={{position: 'absolute', left: v ? 78 : 132, top: v ? 290 : 166}}>
      <Reveal><Label>YOUR LIBRARY. YOUR RULES.</Label></Reveal>
      <Title first='你的游戏' second='一个主场' size={v ? 98 : 76} delay={12} style={{display: v ? 'block' : 'flex', gap: 32, marginTop: 20}} />
    </div>
    <Reveal delay={28} style={{position: 'absolute', left: (w - panelW) / 2, top: v ? 705 : 420, width: panelW, height: panelH, background: '#0b111af5', border: '1px solid #38533e', borderRadius: 18, overflow: 'hidden', boxShadow: '0 35px 100px #000b', transform: `perspective(2000px) rotateX(${clamp(f, 28, 100, 8, 0)}deg)`}}>
      <div style={{height: 78, padding: '0 30px', display: 'flex', alignItems: 'center', gap: 30, borderBottom: '1px solid #233041'}}><Brand /><span style={{color: ACID, fontSize: 23, marginLeft: 'auto'}}>游戏库</span><span style={{fontSize: 21, color: MUTED}}>兼容查询</span><span style={{fontSize: 21, color: MUTED}}>已登录</span></div>
      <div style={{height: 80, padding: '0 32px', display: 'flex', alignItems: 'center', justifyContent: 'space-between'}}><b style={{fontSize: 30}}>我的游戏 <span style={{fontSize: 20, color: MUTED, fontWeight: 500}}> / LIBRARY</span></b><span style={{padding: '10px 18px', border: '1px solid #233041', color: MUTED, fontSize: 20}}>扫描 · 添加 · 搜索</span></div>
      <div style={{display: 'grid', gridTemplateColumns: v ? '1fr 1fr' : 'repeat(4,1fr)', gap: 18, padding: '0 28px'}}>
        {['霓虹边境', '深空回响', '遗迹之夜', '添加游戏'].map((name, i) => <div key={name} style={{height: v ? 356 : 320, position: 'relative', border: `1px solid ${i === 0 && select ? ACID : '#233041'}`, borderRadius: 10, overflow: 'hidden', background: BG, boxShadow: i === 0 && select ? '0 0 25px #adff1830' : undefined}}>
          <div style={{position: 'absolute', inset: '0 0 66px', overflow: 'hidden'}}>{i === 0 ? <GameWorld time={f / 180} compact /> : <Img src={staticFile(`art/${i === 1 ? 'mu-hero-art.png' : i === 2 ? 'mu-home-banner-new.png' : 'mu-add-game-art.png'}`)} style={{width: '100%', height: '100%', objectFit: 'cover', objectPosition: i === 2 ? '70% center' : 'center', opacity: .85}} />}</div>
          <div style={{position: 'absolute', left: 16, right: 16, bottom: 17, display: 'flex', justifyContent: 'space-between', alignItems: 'center'}}><b style={{fontSize: 22}}>{name}</b><span style={{fontFamily: tech, color: i === 0 && select ? ACID : MUTED, fontSize: 20}}>{i === 3 ? '+' : i === 0 && select ? 'SELECTED' : 'READY'}</span></div>
          {i === 0 && select && <div style={{position: 'absolute', inset: '0 0 66px', background: '#05080d88', display: 'grid', placeItems: 'center'}}><div style={{background: ACID, color: BG, padding: '15px 24px', fontSize: 24, fontWeight: 900}}>开启 DLSS5 ↗</div></div>}
        </div>)}
      </div>
      <div style={{margin: '20px 30px', color: MUTED, fontSize: 18, display: 'flex', justifyContent: 'space-between'}}><span>按游戏管理配置</span><span>界面动画演示 · 示例游戏</span></div>
      {f > 75 && f < 160 && <div style={{position: 'absolute', left: clamp(f, 75, 106, panelW * .56, panelW * .14), top: clamp(f, 75, 106, panelH * .8, v ? 324 : 286), transform: 'rotate(-15deg)'}}><svg width='34' height='44' viewBox='0 0 34 44'><path d='M2 2 L2 36 L12 28 L19 42 L26 38 L19 25 L32 24Z' fill='white' stroke='#05080d' strokeWidth='2' /></svg></div>}
    </Reveal>
  </AbsoluteFill>;
};

const Paths = () => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const v = h > w;
  const options = [
    {name: 'DLSS-NR', sub: '神经渲染方案', label: 'NEURAL RENDERING', detail: '画面风格 / 曝光 / 色调'},
    {name: 'OptiScaler', sub: '超分与帧生成配置', label: 'UPSCALING + FRAME GENERATION', detail: '固定版本 / 下载校验'},
    {name: '大力喜鹊', sub: '窗口缩放', label: 'WINDOW SCALING', detail: '下载启动 / 快捷键控制'},
  ];
  return <AbsoluteFill>
    <Img src={staticFile('art/mu-hero-art.png')} style={{width: w, height: h, objectFit: 'cover', opacity: .22, transform: `scale(${1 + f * .0002})`}} />
    <AbsoluteFill style={{background: 'linear-gradient(180deg,#05080d80,#05080dd9)'}} /><Particles />
    <div style={{position: 'absolute', left: v ? 78 : 132, top: v ? 290 : 185}}><Reveal><Label>CHOOSE YOUR PATH</Label></Reveal><Title first='三种方案' second='一个入口' size={v ? 100 : 88} style={{display: v ? 'block' : 'flex', gap: 30, marginTop: 20}} /></div>
    <div style={{position: 'absolute', left: v ? 78 : 132, right: v ? 78 : 132, top: v ? 720 : 452, display: 'grid', gridTemplateColumns: v ? '1fr' : 'repeat(3,1fr)', gap: 24}}>
      {options.map((o, i) => {
        const active = Math.floor(Math.max(0, f - 48) / 78) % 3 === i;
        return <Reveal key={o.name} delay={26 + i * 14} style={{height: v ? 250 : 367, background: active ? '#153222e0' : '#0b111ae8', border: `1px solid ${active ? '#75ae32' : '#233041'}`, borderTop: `3px solid ${active ? ACID : '#3c5142'}`, padding: v ? '24px 30px' : '33px 34px', position: 'relative', overflow: 'hidden'}}>
          <Label style={{fontSize: 20, letterSpacing: 3, color: active ? ACID : MUTED}}>MODE / 0{i + 1}</Label>
          <div style={{fontFamily: i === 2 ? sans : tech, fontSize: i === 2 ? 58 : 66, letterSpacing: -1, fontWeight: 700, marginTop: v ? 6 : 22}}>{o.name}</div>
          <div style={{fontSize: 27, marginTop: 3, color: '#d5e5de'}}>{o.sub}</div>
          <div style={{fontSize: 20, color: MUTED, marginTop: v ? 12 : 32}}>{o.detail}</div>
          <Label style={{fontSize: 15, letterSpacing: 2, marginTop: 13}}>{o.label}</Label>
          <div style={{position: 'absolute', right: 26, top: 26, width: 8, height: 8, borderRadius: 4, background: active ? ACID : '#3d5545', boxShadow: active ? '0 0 20px #adff18' : undefined}} />
        </Reveal>;
      })}
    </div>
    <Reveal delay={80} style={{position: 'absolute', left: v ? 78 : 132, bottom: v ? 245 : 130, color: MUTED, fontSize: v ? 21 : 23}}>根据游戏与硬件条件，选择适合的配置路径。</Reveal>
  </AbsoluteFill>;
};

const Restore = () => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const v = h > w;
  const step = f < 70 ? 0 : f < 139 ? 1 : 2;
  return <AbsoluteFill>
    <Grid opacity={.3} />
    <div style={{position: 'absolute', left: v ? 78 : 132, top: v ? 300 : 210}}><Reveal><Label>CONFIGURE WITH CONFIDENCE</Label></Reveal><Title first='放心配置' second='有路可回' size={v ? 108 : 94} style={{marginTop: 22}} /><Reveal delay={35} style={{fontSize: 26, marginTop: 26, color: MUTED}}>记录原始状态，恢复时核验变化。</Reveal></div>
    <div style={{position: 'absolute', left: v ? 125 : 910, top: v ? 830 : 302, width: v ? 830 : 850}}>
      {['记录原始文件状态', '应用游戏配置', '核验并恢复文件'].map((text, i) => <Reveal key={i} delay={28 + i * 14} style={{height: v ? 190 : 154, marginBottom: 22, padding: '25px 30px', border: `1px solid ${step === i ? '#87bd36' : '#233041'}`, background: step === i ? '#17301e' : '#0b111ae8', display: 'flex', alignItems: 'center', gap: 28}}>
        <div style={{width: 67, height: 74, border: `2px solid ${step >= i ? ACID : '#426049'}`, position: 'relative', padding: '18px 12px'}}>{[0, 1, 2].map(n => <div key={n} style={{height: 3, background: step >= i ? ACID : '#426049', marginBottom: 9, width: n === 2 ? 25 : 38}} />)}</div>
        <div><Label style={{fontSize: 18, color: MUTED, letterSpacing: 3}}>0{i + 1} / {['BACKUP', 'CONFIGURE', 'RESTORE'][i]}</Label><div style={{fontSize: 31, marginTop: 8}}>{text}</div></div>
        <div style={{marginLeft: 'auto', fontFamily: tech, fontSize: 40, color: ACID}}>{step > i || (step === 2 && i === 2) ? '✓' : step === i ? '···' : '—'}</div>
      </Reveal>)}
    </div>
  </AbsoluteFill>;
};

const Power = () => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const v = h > w;
  const lw = v ? 800 : 790;
  const y = v ? 570 : 262;
  return <AbsoluteFill>
    <Grid opacity={.18} />
    <AbsoluteFill style={{background: 'radial-gradient(ellipse at center,#1c511a66 0%,#05080d 68%)'}} />
    <svg width={w} height={h} style={{position: 'absolute', inset: 0}}>
      <defs><linearGradient id='beam'><stop stopColor={ACID} stopOpacity='0' /><stop offset='.55' stopColor={ACID} /><stop offset='1' stopColor={ACID} stopOpacity='0' /></linearGradient></defs>
      {Array.from({length: 18}, (_, i) => {
        const p = mod(f / 170 + i / 18, 1);
        const theta = i / 18 * Math.PI * 2;
        const r = (1 - p) * w * .9;
        const cx = w / 2, cy = y + lw * .28;
        return <line key={i} x1={cx + Math.cos(theta) * r} y1={cy + Math.sin(theta) * r * .55} x2={cx + Math.cos(theta) * (r + 150)} y2={cy + Math.sin(theta) * (r + 150) * .55} stroke='url(#beam)' strokeWidth={i % 3 ? 2 : 4} opacity={Math.sin(p * Math.PI) * .6} />;
      })}
      <ellipse cx={w / 2} cy={y + lw * .28} rx={lw * .57} ry={lw * .38} fill='none' stroke='#adff1850' strokeWidth='1' strokeDasharray='45 190 80 100' transform={`rotate(${f * .03} ${w / 2} ${y + lw * .28})`} />
      <ellipse cx={w / 2} cy={y + lw * .28} rx={lw * .63} ry={lw * .42} fill='none' stroke='#6fefff25' strokeWidth='1' strokeDasharray='15 140 200 100' transform={`rotate(${-f * .04} ${w / 2} ${y + lw * .28})`} />
    </svg>
    <Reveal delay={4} style={{position: 'absolute', left: (w - lw) / 2, top: y, filter: `drop-shadow(0 0 ${24 + Math.sin(f / 12) * 6}px #73f70055)`, transform: `scale(${clamp(f, 0, 160, .88, 1.04)})`}}><Logo width={lw} /></Reveal>
    <div style={{position: 'absolute', left: 0, right: 0, top: v ? 1140 : 747, textAlign: 'center'}}><Reveal delay={35}><Label style={{color: INK, fontSize: v ? 69 : 62, letterSpacing: 8}}>AMD DLSS MU</Label></Reveal><Reveal delay={52} style={{fontSize: 32, marginTop: 15, letterSpacing: 9, color: ACID}}>游戏配置，少点折腾。</Reveal></div>
    <Particles count={55} />
  </AbsoluteFill>;
};

const Ending = () => {
  const f = useCurrentFrame();
  const {width: w, height: h} = useVideoConfig();
  const v = h > w;
  return <AbsoluteFill>
    <Img src={staticFile('art/mu-home-banner-new.png')} style={{width: w, height: h, objectFit: 'cover', objectPosition: v ? '72% center' : 'center', transform: `scale(${clamp(f, 0, 225, 1.08, 1)})`}} />
    <AbsoluteFill style={{background: v ? 'linear-gradient(180deg,#05080dbb,transparent 45%,#05080dee 75%)' : 'linear-gradient(90deg,#05080def 0%,#05080d99 45%,#05080d20)'}} />
    <div style={{position: 'absolute', left: v ? 78 : 132, top: v ? 290 : 250}}>
      <Reveal><Label>READY FOR YOUR NEXT GAME</Label></Reveal>
      <Title first='少点折腾' second='即刻开局' size={v ? 108 : 112} style={{marginTop: 20}} />
      <Reveal delay={35} style={{fontFamily: tech, fontSize: 31, letterSpacing: 4, marginTop: 28}}>AMD DLSS MU <span style={{color: ACID}}> / WINDOWS x64</span></Reveal>
      {!v && <CallToAction />}
    </div>
    {v && <div style={{position: 'absolute', left: 78, right: 78, top: 1280}}><CallToAction /></div>}
    <div style={{position: 'absolute', left: v ? 78 : 132, right: v ? 78 : 132, bottom: v ? 223 : 120, fontSize: v ? 19 : 19, lineHeight: 1.8, color: '#a5b6ae'}}>
      <div>独立第三方工具 · 非 AMD / NVIDIA 官方产品</div>
      <div>实际效果取决于游戏、显卡与驱动；按支持条件使用。</div>
    </div>
    <Particles count={26} />
  </AbsoluteFill>;
};

const CallToAction = () => <>
  <Reveal delay={52} style={{display: 'inline-flex', padding: '18px 32px', background: ACID, color: BG, fontSize: 30, fontWeight: 900, marginTop: 40, gap: 46, alignItems: 'center'}}>立即体验 MU <span>↗</span></Reveal>
  <Reveal delay={62} style={{fontFamily: tech, fontSize: 29, letterSpacing: 1.3, marginTop: 22, color: INK}}>amd-dlss-mu.claude-api.cn</Reveal>
</>;

export const MuPromo = () => {
  const f = useCurrentFrame();
  const current = CUTS.findIndex((c, i) => f >= c && f < (CUTS[i + 1] ?? 1800));
  const local = f - CUTS[Math.max(0, current)];
  const scenes = [Opening, Play, Library, Paths, Restore, Power, Ending];
  const chapter = ['INITIALIZE', 'PLAY', 'LIBRARY', 'CONFIGURE', 'RESTORE', 'MU', 'READY'][current] ?? 'READY';
  return <AbsoluteFill style={{background: BG, color: INK, fontFamily: sans, overflow: 'hidden'}}>
    <FontGate />
    <svg width='0' height='0'><defs><radialGradient id='vignette'><stop offset='.4' stopColor='#000' stopOpacity='0' /><stop offset='1' stopColor='#000' stopOpacity='.65' /></radialGradient></defs></svg>
    {scenes.map((Scene, i) => <Sequence key={i} from={CUTS[i]} durationInFrames={CUTS[i + 1] - CUTS[i]}><Scene /></Sequence>)}
    <Chrome chapter={chapter} number={String(current + 1).padStart(2, '0')} />
    <AbsoluteFill style={{opacity: .1, pointerEvents: 'none', background: 'repeating-linear-gradient(0deg, transparent 0 3px, #aaffbb20 3px 4px)', mixBlendMode: 'soft-light'}} />
    {current > 0 && local < 13 && <AbsoluteFill style={{background: BG, opacity: clamp(local, 0, 13, .85, 0)}} />}
    {current > 0 && local < 18 && <AbsoluteFill style={{background: 'linear-gradient(110deg,transparent 45%,#adff1860 49%,#dfffb080 50%,transparent 54%)', transform: `translateX(${clamp(local, 0, 18, -120, 120)}%)`, pointerEvents: 'none'}} />}
    <AbsoluteFill style={{background: BG, opacity: clamp(f, 1778, 1799, 0, 1), pointerEvents: 'none'}} />
    <Html5Audio src={staticFile('soundtrack.wav')} volume={.85} />
  </AbsoluteFill>;
};
