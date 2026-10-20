// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { SupportedGames } from './SupportedGames';
import { rpc } from './bridge';
import type { State } from './types';
const catalog={asOf:'2026-10-14',games:[
 {title:'赛博朋克 2077',canonicalName:'Cyberpunk 2077',steamAppId:'1091500',aliases:['Cyberpunk 2077','Cyberpunk2077']},
 ...Array.from({length:25},(_,i)=>({title:`测试游戏 ${i}`,canonicalName:`Fixture ${i}`,steamAppId:null,aliases:[`Fixture ${i}`]}))
]};
vi.mock('./bridge',()=>({rpc:vi.fn()}));
const pro={viewAvailable:true,account:{id:'test',membership:{tier:'pro',expiresAt:'2099-01-01T00:00:00Z'}}} as State;
afterEach(()=>{cleanup();vi.clearAllMocks();});
it('普通账户只看到锁定预览，不请求游戏名单；开通按钮进入会员入口',()=>{
 const onUpgrade=vi.fn();render(<SupportedGames state={{...pro,account:{...pro.account!,membership:{tier:'standard',expiresAt:null}}}} onUpgrade={onUpgrade}/>);
 expect(screen.getByRole('heading',{name:'开通高级会员查看100+实测游戏'})).toBeTruthy();
 expect(rpc).not.toHaveBeenCalled();expect(screen.queryByText('赛博朋克 2077')).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'开通高级会员'}));expect(onUpgrade).toHaveBeenCalledOnce();
});
it('有效高级会员从后端读取名单，支持别名搜索与分页',async()=>{
 vi.mocked(rpc).mockResolvedValue(catalog);
 render(<SupportedGames state={pro} onUpgrade={()=>{}}/>);
 const search=await screen.findByRole('searchbox',{name:'搜索支持游戏'});
 fireEvent.change(search,{target:{value:'Cyberpunk2077'}});
 expect(screen.getByText('赛博朋克 2077')).toBeTruthy();
 expect(screen.queryByRole('button',{name:'下一页'})).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:/赛博朋克 2077/}));expect(screen.getByText('Cyberpunk2077')).toBeTruthy();
 expect(screen.getByRole('dialog').textContent).toContain('Steam 假入库功能正在路上，敬请期待。');
 fireEvent.click(screen.getByRole('button',{name:'知道了'}));fireEvent.change(search,{target:{value:''}});
 expect(screen.getByRole('button',{name:'下一页'})).toBeTruthy();
});
it('会员失效后隐藏名单并丢弃迟到的请求结果',async()=>{
 let resolve!:(data:typeof catalog)=>void;vi.mocked(rpc).mockImplementation(()=>new Promise(r=>{resolve=r;}));
 const {rerender}=render(<SupportedGames state={pro} onUpgrade={()=>{}}/>);
 rerender(<SupportedGames state={{...pro,account:{...pro.account!,membership:{tier:'pro',expiresAt:'2000-01-01T00:00:00Z'}}}} onUpgrade={()=>{}}/>);
 await act(async()=>resolve(catalog));
 expect(screen.getByRole('heading',{name:'开通高级会员查看100+实测游戏'})).toBeTruthy();expect(screen.queryByText('赛博朋克 2077')).toBeNull();
});
