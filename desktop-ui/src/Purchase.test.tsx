// @vitest-environment jsdom
import {afterEach,expect,it,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {PurchaseDialog} from './AccountPage';
import {rpc,emptyState} from './bridge';
vi.mock('./bridge',()=>({rpc:vi.fn(),emptyState:{settings:{},downloads:[],busy:false}}));
afterEach(()=>{cleanup();vi.resetAllMocks();});
function setup(){
 vi.mocked(rpc).mockResolvedValue(null);
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
it.each([['终身会员','mu-pro-lifetime'],['高级 Plus 会员','mu-pro-assisted'],['远程指导服务','mu-remote-once']])('兑换%s提交所选套餐并刷新账户',async(label,id)=>{
 const notify=setup();fireEvent.click(await screen.findByRole('radio',{name:new RegExp(label)}));
 fireEvent.click(screen.getByRole('button',{name:'支付完成 校验CDK'}));
 fireEvent.change(screen.getByLabelText('会员 CDK'),{target:{value:' CODE-TEST '}});
 fireEvent.click(screen.getByRole('button',{name:'校验 CDK · 兑换权益'}));
 await screen.findByText('兑换成功，权益已更新');
 expect(rpc).toHaveBeenCalledWith('redeemMembership',{code:'CODE-TEST',planId:id});
 expect(notify).toHaveBeenCalledWith('CDK 校验成功，会员及服务权益已更新。');
 expect(vi.mocked(rpc).mock.calls.filter(([c])=>c==='refreshAccount').length).toBe(1);
});
it('弹窗直接展示本地套餐、权益和收款码，打开时不请求接口',()=>{
 setup();
 expect(rpc).not.toHaveBeenCalled();
 expect(screen.getAllByRole('radio')).toHaveLength(4);
 expect(screen.getByRole('img',{name:'微信支付收款码'}).getAttribute('src')).toBe('./payments/wechat.jpg');
 expect(screen.getAllByText(/100\+.*实测/).length).toBeGreaterThan(0);
 expect(screen.getByText(/高级图形方案/)).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'支付宝'}));
 expect(screen.getByRole('img',{name:'支付宝收款码'}).getAttribute('src')).toBe('./payments/alipay.jpg');
 expect(rpc).not.toHaveBeenCalled();
});
it('刷新状态只刷新账户，不获取套餐',async()=>{
 setup();fireEvent.click(screen.getByRole('button',{name:'刷新状态'}));
 await waitFor(()=>expect(rpc).toHaveBeenCalledWith('refreshAccount'));
 expect(vi.mocked(rpc).mock.calls).toHaveLength(1);
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
