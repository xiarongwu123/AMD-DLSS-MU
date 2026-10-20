// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { SmartRecommendationPanel } from "./SmartRecommendationPanel";
import { rpc } from "./bridge";
import type { State } from "./types";
vi.mock("./bridge",()=>({rpc:vi.fn()}));
const state={account:{id:"fixture",membership:{tier:"pro",expiresAt:null},features:[{key:"schemes.game.view",allowed:true},{key:"schemes.premium.configure",allowed:true}]}} as State;
const candidate={id:"fixture:v1",scheme:"compat",name:"兼容版",state:"ELIGIBLE",score:95,level:"优先推荐",automatic:true,premium:true,external:false,
 evidence:"verified",reasons:["测试专属构建匹配"],issues:[],configuration:{backend:"daniel",componentVersion:"1.12.1",proxy:"dxgi.dll",passes:1,graphicsApi:"dx12",ini:{DlssNr:{Enabled:"true"}}}};
const result={planId:"test-plan",gameName:"fixture game",goal:"quality",ruleRevision:"r1",ruleSource:"remote",expiresUtc:"2099-01-01",environment:{fields:{gpuModel:{value:"fixture GPU",confidence:"CONFIRMED",source:"unit fixture"}}},recommended:candidate,freeAlternative:null,candidates:[candidate],notices:[]};
function view(){return render(<SmartRecommendationPanel gameId="g" gameTitle="fixture" state={state} disabled={false} selected="fps" onUpgrade={()=>{}}/>);}
afterEach(()=>{cleanup();vi.resetAllMocks();});
it("应用提交不可伪造的已签发计划ID和候选ID，不由前端直接拼接安装参数",async()=>{
 vi.mocked(rpc).mockResolvedValueOnce(result).mockResolvedValueOnce({external:false,verification:{state:"INSTALLED",installed:true,loaded:false,active:false,evidence:[],failureCodes:[]}});
 view();fireEvent.click(screen.getByRole("button",{name:"查看游戏专属方案"}));await screen.findByText("已找到适合当前设备的配置");
 fireEvent.click(screen.getByRole("button",{name:"一键应用"}));await waitFor(()=>expect(rpc).toHaveBeenCalledWith("recommendationApply",expect.objectContaining({planId:"test-plan",candidateId:"fixture:v1"})));
 expect(await screen.findByText(/文件已部署，运行效果未验证/)).toBeTruthy();expect(rpc).not.toHaveBeenCalledWith("configure",expect.anything());
});
it("手动方案范围单独评估；未知检测结果不冒充成功",async()=>{
 vi.mocked(rpc).mockResolvedValue(null);view();fireEvent.click(screen.getByRole("button",{name:"仅评估当前方案"}));
 await screen.findByRole("alert");expect(rpc).toHaveBeenCalledWith("gameRecommendation",expect.objectContaining({scope:"fps"}));expect(screen.queryByText("已找到适合当前设备的配置")).toBeNull();
});
it("修改实际API后丢弃迟到的旧检测结果",async()=>{
 let resolve!:(v:unknown)=>void;vi.mocked(rpc).mockImplementation(()=>new Promise(r=>{resolve=r;}));view();
 fireEvent.click(screen.getByRole("button",{name:"查看游戏专属方案"}));fireEvent.click(screen.getByText(/确认游戏实际设置/));
 fireEvent.change(screen.getByRole("combobox",{name:"确认实际图形接口"}),{target:{value:"dx12"}});
 await act(async()=>resolve(result));expect(screen.queryByText("已找到适合当前设备的配置")).toBeNull();
});
it("未勾选隐私同意不会上传，实验候选也不会默认允许应用",async()=>{
 vi.mocked(rpc).mockResolvedValue({...result,recommended:null,candidates:[{...candidate,automatic:false,state:"UNVERIFIED",level:"证据不足"}]});view();
 fireEvent.click(screen.getByRole("button",{name:"查看游戏专属方案"}));await screen.findByText("当前游戏暂无已验证的自动配置");
 fireEvent.click(screen.getByRole("button",{name:"查看其他方案与需要处理的条件"}));
 expect((screen.getByRole("button",{name:"确认尝试实验配置"}) as HTMLButtonElement).disabled).toBe(true);
 fireEvent.click(screen.getByText("反馈实际体验与匿名验证数据"));expect((screen.getByRole("button",{name:"上传本次验证"}) as HTMLButtonElement).disabled).toBe(true);
 expect(rpc).not.toHaveBeenCalledWith("recommendationFeedback",expect.anything());
});
