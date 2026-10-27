// @vitest-environment jsdom
import {afterEach,expect,it,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {PurchaseDialog} from './AccountPage';
import {rpc,emptyState} from './bridge';
vi.mock('./bridge',()=>({rpc:vi.fn(),emptyState:{settings:{},downloads:[],busy:false}}));
const plans=[{id:'monthly',name:'高级会员',durationDays:30,priceMinor:990,currency:'CNY'},
{id:'lifetime',name:'终身会员',durationDays:0,priceMinor:8880,currency:'CNY',configurationSessions:3},
{id:'assisted',name:'高级 Plus 会员',durationDays:30,priceMinor:2990,currency:'CNY',configurationSessions:1},
{id:'remote',name:'远程指导服务',durationDays:0,priceMinor:3000,currency:'CNY',configurationSessions:1,grantsMembership:false}];
afterEach(()=>{cleanup();vi.resetAllMocks();});
function setup(matching=true){
 vi.mocked(rpc).mockImplementation(async command=>command==='plans'?{items:plans,cdkPlanMatching:matching,paymentsEnabled:true,payment:{channels:[{id:'wechat',name:'微信支付',qrImageUrl:'data:image/png;base64,AA=='}]}}:null);
 const notify=vi.fn();render(<PurchaseDialog state={{...emptyState,account:null}} onClose={()=>{}} notify={notify}/>);return notify;
}
it('展示四档套餐及付款后获取CDK说明，不再创建人工订单',async()=>{
 setup();await screen.findByRole('radio',{name:/终身会员/});
 expect(screen.getAllByRole('radio')).toHaveLength(4);
 expect(screen.getByRole('radio',{name:/终身会员/}).textContent).toContain('含 3 次远程指导');
 expect(screen.getAllByText(/支付成功后添加微信/).length).toBeGreaterThan(0);
 fireEvent.click(screen.getByRole('button',{name:'支付完成 校验CDK'}));
 expect(await screen.findByLabelText('会员 CDK')).toBeTruthy();
 expect(vi.mocked(rpc).mock.calls.some(([c])=>c==='createOrder'||c==='reviewOrder')).toBe(false);
});
it.each([['终身会员','lifetime'],['高级 Plus 会员','assisted'],['远程指导服务','remote']])('兑换%s提交所选套餐并刷新账户',async(label,id)=>{
 const notify=setup();fireEvent.click(await screen.findByRole('radio',{name:new RegExp(label)}));
 fireEvent.click(screen.getByRole('button',{name:'支付完成 校验CDK'}));
 fireEvent.change(screen.getByLabelText('会员 CDK'),{target:{value:' CODE-TEST '}});
 fireEvent.click(screen.getByRole('button',{name:'校验 CDK · 兑换权益'}));
 await screen.findByText('兑换成功，权益已更新');
 expect(rpc).toHaveBeenCalledWith('redeemMembership',{code:'CODE-TEST',planId:id});
 expect(notify).toHaveBeenCalledWith('CDK 校验成功，会员及服务权益已更新。');
 expect(vi.mocked(rpc).mock.calls.filter(([c])=>c==='refreshAccount').length).toBeGreaterThan(1);
});
it('后台不支持套餐匹配时阻止兑换，避免错误开通',async()=>{
 setup(false);await screen.findByRole('radio',{name:/终身会员/});
 expect((screen.getByRole('button',{name:'支付完成 校验CDK'}) as HTMLButtonElement).disabled).toBe(true);
 expect(screen.getByRole('alert').textContent).toContain('后台尚不支持');
});
it('兑换失败保留输入，不显示成功状态',async()=>{
 setup();await screen.findByRole('radio',{name:/终身会员/});fireEvent.click(screen.getByRole('button',{name:'支付完成 校验CDK'}));
 fireEvent.change(screen.getByLabelText('会员 CDK'),{target:{value:'WRONG'}});
 vi.mocked(rpc).mockRejectedValueOnce(new Error('CDK 与所选套餐不匹配'));
 fireEvent.click(screen.getByRole('button',{name:'校验 CDK · 兑换权益'}));
 await screen.findByText('CDK 与所选套餐不匹配');expect((screen.getByLabelText('会员 CDK') as HTMLInputElement).value).toBe('WRONG');
 expect(screen.queryByText('兑换成功，权益已更新')).toBeNull();
});
it('兑换成功后的刷新失败不允许再次消费兑换码',async()=>{
 setup();await screen.findByRole('radio',{name:/终身会员/});fireEvent.click(screen.getByRole('button',{name:'支付完成 校验CDK'}));
 fireEvent.change(screen.getByLabelText('会员 CDK'),{target:{value:'OK'}});
 vi.mocked(rpc).mockImplementation(async command=>{if(command==='refreshAccount')throw new Error('timeout');return null;});
 fireEvent.click(screen.getByRole('button',{name:'校验 CDK · 兑换权益'}));
 await screen.findByText('兑换成功，权益已更新');expect(screen.queryByRole('button',{name:'校验 CDK · 兑换权益'})).toBeNull();
 await waitFor(()=>expect(screen.getByRole('alert').textContent).toContain('无需再次兑换'));
});
