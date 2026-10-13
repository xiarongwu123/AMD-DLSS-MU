import { useState } from "react";
import { ArrowDown, ArrowUpRight, Gamepad2, Layers, Sparkles, UsersRound, Zap } from "lucide-react";
import Carousel from "./reactbits/Carousel";
import { Button } from "./components";
import { InteractiveSurface, Shine } from "./VisualEffects";
import type { Game } from "./types";

export function HomeLanding({games, downloads, active, dark, busy, onLibrary, onMagpie, onGame, onCommunity}: {
  games: Game[]; downloads: number; active: boolean; dark: boolean; busy: boolean;
  onLibrary: () => void; onMagpie: () => void; onGame: (game: Game) => void; onCommunity: () => void;
}) {
  const [index, setIndex] = useState(0);
  const cards = [
    ...games.map(game => <article key={game.id} data-game-id={game.id} className="hub-game" aria-label={game.title}>
      {game.cover ? <img src={game.cover} alt={game.title} draggable={false} /> : <div className="hub-cover-empty"><Gamepad2 size={64} /></div>}
      <div className="hub-game-shade" />
      <span className="hub-game-state">{game.state === "Configured" ? "已配置 · 准备开玩" : "本地游戏"}</span>
      <div className="hub-game-copy"><h3>{game.title}</h3>
        <Button primary charge disabled={busy} onClick={() => onGame(game)}>
          <Zap size={18} />{game.state === "Configured" ? "启动游戏" : "开启 DLSS 5"}
        </Button>
      </div>
    </article>),
    <article key="community" className="hub-tool-card hub-tool-community">
      <UsersRound size={42} /><span>玩家方案</span><h3>好配置，<br />一起用。</h3>
      <p>看看其他玩家怎么配，找到合适的直接复用。</p>
      <Button primary onClick={onCommunity}>看看玩家方案 <ArrowUpRight size={17} /></Button>
    </article>,
    <article key="magpie" className="hub-tool-card hub-tool-magpie">
      <Layers size={42} /><span>大力喜鹊</span><h3>多一条<br />帧生成路线。</h3>
      <p>进入大力喜鹊，配置对应的帧生成方案。</p>
      <Button onClick={onMagpie}>开启大力喜鹊 <ArrowUpRight size={17} /></Button>
    </article>,
  ];
  return <div className="home-hub">
    <section className="hub-hero">
      {(games[index]?.cover || games[0]?.cover) && <img className="hub-atmosphere" src={(games[index]?.cover || games[0]?.cover)!} alt="" aria-hidden="true" />}
      <div className="hub-pitch">
        <span className="hub-eyebrow"><Gamepad2 size={17} /> AMD 游戏图形配置工具</span>
        <h1>选好游戏。<br />一键开启 <span className="hub-dlss"><Shine active={active}>DLSS 5</Shine></span>。</h1>
        <div className="hub-routes"><span>画质优先</span><span>帧率优先</span><span>自定义</span></div>
        <div className="actions">
          <Button primary charge onClick={onLibrary}><Sparkles size={19} />智能推荐 <ArrowUpRight size={17} /></Button>
          <Button onClick={onMagpie}>开启大力喜鹊</Button>
          <Button className="hub-community-action" onClick={onCommunity}><UsersRound size={20} />大神方案</Button>
        </div>
      </div>
      <div className="hub-deck" onWheel={event => event.stopPropagation()}>
        <div className="hub-deck-heading"><h2>我的游戏</h2></div>
        {active ? <Carousel items={cards} baseWidth="100%" aspectRatio={.88} effect="scale" peek={24} gap={16}
          frame={false} arrows indicator="counter" loop autoplay={false} onChange={setIndex} theme={dark ? "dark" : "light"} /> :
          <div className="hub-static-card">{cards[0]}<Button onClick={onCommunity}>玩家方案 <ArrowUpRight size={16} /></Button></div>}
      </div>
    </section>
    <div className="hub-bottom">
      <InteractiveSurface active={active} dark={dark} className="hub-summary">
        <span><b>{games.length}</b> 本地游戏</span><span><b>{games.filter(g => g.state === "Configured").length}</b> 已配置</span><span><b>{downloads}</b> 下载任务</span>
      </InteractiveSurface>
      <button className="hub-library-link" onClick={onLibrary}>下滑进入游戏库 <ArrowDown size={18} /></button>
    </div>
  </div>;
}
