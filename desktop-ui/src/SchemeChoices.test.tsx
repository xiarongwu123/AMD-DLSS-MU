// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { SchemeChoices } from "./SchemeChoices";
import { rpc, emptyState } from "./bridge";
import type { State } from "./types";
vi.mock("./bridge", () => ({ rpc: vi.fn(), emptyState: { downloads: [], account: null } }));
const member: State = { ...emptyState, account: { id: "a", email: "a@test", membership: { tier: "pro", expiresAt: null }, paymentsEnabled: true,
  features: [{ key: "schemes.premium.configure", allowed: true }, { key: "schemes.game.view", allowed: true }] } };
const props = { gameId: "g", gameTitle: "当前游戏", selected: "base" as const, state: emptyState, disabled: false, onSelect: vi.fn(), onUpgrade: vi.fn(), onUseRecommendation: vi.fn() };
afterEach(() => { cleanup(); vi.clearAllMocks(); });
describe("八个直接选择的方案", () => {
  it("展示两个免费、六个付费，主推作者调校且 AMD 喜鹊不重复", () => {
    render(<SchemeChoices {...props}/>);
    expect(screen.getAllByRole("button", { pressed: false })).toHaveLength(7);
    expect(screen.getByRole("button", { name: /作者调校版/ }).className).toContain("scheme-author-featured");
    expect(screen.getAllByRole("button", { name: /AMD 大力喜鹊/ })).toHaveLength(1);
    expect(screen.queryByText("喜鹊外置版")).toBeNull();
    expect(screen.getByRole("button", { name: /60系显卡专用/ }).textContent).toContain("高级会员");
    expect(screen.getByRole("button", { name: /老游戏专用版/ })).toBeTruthy();
    expect(rpc).not.toHaveBeenCalled();
  });
  it("非会员查看推荐只打开开通入口，不请求受保护的数据", () => {
    render(<SchemeChoices {...props}/>);
    fireEvent.click(screen.getByRole("button", { name: /开通会员/ }));
    expect(props.onUpgrade).toHaveBeenCalledOnce(); expect(rpc).not.toHaveBeenCalled();
  });
  it("会员主动查看后才读取，没有规则显示无自动推荐", async () => {
    vi.mocked(rpc).mockResolvedValue({planId:"fixture",gameName:"当前游戏",ruleRevision:"unconfigured",ruleSource:"remote",environment:{fields:{}},recommended:null,freeAlternative:null,candidates:[],notices:[]});
    render(<SchemeChoices {...props} state={member}/>);
    expect(rpc).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: /查看游戏专属方案/ }));
    await screen.findByText("当前游戏暂无已验证的自动配置");
    expect(rpc).toHaveBeenCalledWith("gameRecommendation", expect.objectContaining({gameId:"g",goal:"stability",scope:""}));
    expect(screen.queryByRole("button", { name: "一键应用" })).toBeNull();
  });
  it("旧后台缺少新增权限字段时，高级会员仍可查看专属方案", async () => {
    vi.mocked(rpc).mockResolvedValue({planId:"fixture",gameName:"当前游戏",ruleRevision:"unconfigured",ruleSource:"remote",environment:{fields:{}},recommended:null,freeAlternative:null,candidates:[],notices:[]});
    render(<SchemeChoices {...props} state={{...member,account:{...member.account!,features:[]}}}/>);
    fireEvent.click(screen.getByRole("button", { name: /查看游戏专属方案/ }));
    await screen.findByText("当前游戏暂无已验证的自动配置");
    expect(props.onUpgrade).not.toHaveBeenCalled();
    expect(document.querySelectorAll('.scheme-access svg.lucide-lock-keyhole')).toHaveLength(0);
  });
  it("会员到期立即隐藏已经读取的私有候选", async () => {
    vi.mocked(rpc).mockResolvedValue({planId:"fixture",gameName:"当前游戏",ruleRevision:"r1",ruleSource:"remote",environment:{fields:{}},recommended:{
      id:"private",name:"私有预设",scheme:"author",state:"ELIGIBLE",score:95,level:"优先推荐",automatic:true,premium:true,external:false,
      configuration:{backend:"mochizuki",passes:1,proxy:"dxgi.dll",componentVersion:"1.12.1",ini:{}},reasons:["私有参数说明"],issues:[]},freeAlternative:null,candidates:[],notices:[]});
    const view=render(<SchemeChoices {...props} state={member}/>);
    fireEvent.click(screen.getByRole("button", { name: /查看游戏专属方案/ }));await screen.findByText("私有预设");
    view.rerender(<SchemeChoices {...props} state={{...member,account:{...member.account!,membership:{tier:"standard",expiresAt:null}}}}/>);
    await waitFor(()=>expect(screen.queryByText("私有参数说明")).toBeNull());
  });
});
