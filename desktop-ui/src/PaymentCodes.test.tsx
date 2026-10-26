// @vitest-environment jsdom
import {afterEach,expect,it} from 'vitest';
import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {PaymentCodes} from './PaymentCodes';
afterEach(cleanup);
it('切换两种收款码时保留订单金额，缺少码时给出联系入口',()=>{
 const payment={instructions:'测试',qrImageUrl:null,channels:[{id:'wechat',name:'微信支付',qrImageUrl:'data:image/jpeg;base64,AAAA'},{id:'alipay',name:'支付宝',qrImageUrl:'data:image/jpeg;base64,BBBB'}]};
 const {rerender}=render(<PaymentCodes payment={payment} amountMinor={2990}/>);
 expect(screen.getByAltText('微信支付收款码').getAttribute('src')).toBe(payment.channels[0].qrImageUrl);
 fireEvent.click(screen.getByRole('button',{name:'支付宝'}));
 expect(screen.getByAltText('支付宝收款码').getAttribute('src')).toBe(payment.channels[1].qrImageUrl);
 expect(screen.getByText('扫码支付 ¥29.90')).toBeTruthy();
 fireEvent.error(screen.getByAltText('支付宝收款码'));expect(screen.getByRole('alert')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'微信支付'}));expect(screen.getByAltText('微信支付收款码')).toBeTruthy();
 rerender(<PaymentCodes payment={{instructions:'测试',qrImageUrl:null}} amountMinor={990}/>);
 expect(screen.getByText(/收款码暂未提供/)).toBeTruthy();
});
it('兼容原来的单张收款码配置',()=>{
 render(<PaymentCodes payment={{instructions:'测试',qrImageUrl:'https://example.com/qr.jpg'}} amountMinor={4990}/>);
 expect(screen.getByAltText('扫码付款收款码').getAttribute('src')).toBe('https://example.com/qr.jpg');
 expect(screen.getByText('扫码支付 ¥49.90')).toBeTruthy();
});
