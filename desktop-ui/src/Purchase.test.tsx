// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup,fireEvent,render,screen,waitFor } from '@testing-library/react';
import { AccountPage } from './AccountPage';
import { rpc,emptyState } from './bridge';
vi.mock('./bridge',()=>({rpc:vi.fn(),emptyState:{settings:{},downloads:[],busy:false}}));
const plans=[{id:'monthly',name:'高级会员',durationDays:30,priceMinor:990,currency:'CNY'},
{id:'lifetime',name:'终身会员',durationDays:0,priceMinor:8880,currency:'CNY',configurationSessions:3},
{id:'assisted',name:'高级 Plus 会员',durationDays:30,priceMinor:2990,currency:'CNY',configurationSessions:1},
{id:'remote',name:'远程指导服务',durationDays:0,priceMinor:3000,currency:'CNY',configurationSessions:1,grantsMembership:false}];
const account={id:'test',email:'test@example.com',membership:{tier:'standard',expiresAt:null},paymentsEnabled:true,features:[
{key:'games.supported.view',enabled:true,minimumTier:'pro',allowed:false},
{key:'dlss.configure',enabled:true,minimumTier:'standard',allowed:true},
{key:'diagnostics.use',enabled:false,minimumTier:'pro',allowed:false}]};
afterEach(()=>{cleanup();vi.resetAllMocks();});
function setup(available=true){vi.mocked(rpc).mockImplementation(async (command,args)=>{
 if(command==='plans')return {items:plans,paymentsEnabled:available};
 if(command==='orders')return {items:[]};
 if(command==='createOrder'){const p=plans.find(p=>p.id===args?.planId)!;return {id:'order-1',planName:p.name,durationDays:p.durationDays,grantsMembership:p.grantsMembership,configurationSessions:p.configurationSessions,configurationSessionsUsed:0,amountMinor:p.priceMinor,status:'pending',payment:{instructions:'测试收款说明'}};}
 return null;
});render(<AccountPage state={{...emptyState,account,online:true}} notify={()=>{}} requestPurchase/>);}
it('订阅墙展示四档价格，保留原Plus并展示终身3次指导',async()=>{
 setup();const assisted=await screen.findByRole('radio',{name:/高级 Plus 会员/});
 expect(screen.queryByRole('combobox')).toBeNull();expect(screen.getByRole('radio',{name:/终身会员/}).textContent).toContain('88.8');
 expect(screen.getAllByRole('radio')).toHaveLength(4);
 expect(screen.getByRole('radio',{name:/终身会员/}).textContent).toContain('含 3 次远程指导');
 expect(screen.getByText('100+ 实测支持游戏库')).toBeTruthy();expect(screen.queryByText('一键排查')).toBeNull();
 const monthly=screen.getByRole('radio',{name:/按月体验/});
 await waitFor(()=>expect((monthly as HTMLButtonElement).disabled).toBe(false));
 fireEvent.keyDown(monthly,{key:'ArrowRight'});
 const lifetime=screen.getByRole('radio',{name:/终身通行证/});
 expect(lifetime.getAttribute('aria-checked')).toBe('true');expect(document.activeElement).toBe(lifetime);
 expect(screen.getByText('终身享用所有付费软件功能')).toBeTruthy();
 fireEvent.click(assisted);expect(assisted.getAttribute('aria-checked')).toBe('true');
 fireEvent.click(screen.getByRole('button',{name:'确认套餐 · 创建付款申请'}));
 await waitFor(()=>expect(rpc).toHaveBeenCalledWith('createOrder',{planId:'assisted'}));
 expect(await screen.findByText('核对到账后，添加微信 xrwCoder 预约指导。')).toBeTruthy();
});
it('单次指导显示30元每次，不把0天服务当成终身会员或展示会员权益',async()=>{
 setup();const remote=await screen.findByRole('radio',{name:/远程指导服务/});
 expect(remote.textContent).toContain('30');expect(remote.textContent).toContain('/ 次');
 expect(remote.textContent).toContain('不附带会员');expect(remote.textContent).not.toContain('享用已开放的高级会员功能');
 fireEvent.click(remote);fireEvent.click(screen.getByRole('button',{name:'确认套餐 · 创建付款申请'}));
 await screen.findByText('单次远程指导 · 不附带会员');
 expect(rpc).toHaveBeenCalledWith('createOrder',{planId:'remote'});
 expect(screen.queryByText('终身专属权益')).toBeNull();expect(screen.getByText('1 次远程指导')).toBeTruthy();
});
it('终身订单展示3次指导与剩余次数',async()=>{
 setup();fireEvent.click(await screen.findByRole('radio',{name:/终身会员/}));
 fireEvent.click(screen.getByRole('button',{name:'确认套餐 · 创建付款申请'}));
 await screen.findByText('3 次远程指导');expect(screen.getByText('已用 0 次 · 剩余 3 次')).toBeTruthy();
 expect(screen.getByText('终身会员 · 终身有效')).toBeTruthy();
});
it('收款未开放仍可查看套餐，但不能提交付款申请',async()=>{
 setup(false);await screen.findByRole('radio',{name:/终身会员/});
 expect((screen.getByRole('button',{name:'确认套餐 · 创建付款申请'}) as HTMLButtonElement).disabled).toBe(true);
 expect(rpc).not.toHaveBeenCalledWith('createOrder',expect.anything());
});
