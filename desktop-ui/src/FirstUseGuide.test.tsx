// @vitest-environment jsdom
import { useState } from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { FirstUseGuide } from "./FirstUseGuide";

afterEach(cleanup);

it("没有扫描到游戏时仍能逐步阅读并跳过引导", () => {
  const finish = vi.fn();
  function Example() {
    const [step, setStep] = useState(0);
    return <><button data-guide="add-game">添加游戏</button>
      <FirstUseGuide step={step} hasGames={false} onStep={setStep} onFinish={finish} /></>;
  }
  render(<Example />);
  expect(screen.getByRole("dialog", { name: "新手引导" }).textContent).toContain("先把游戏加进来");
  fireEvent.click(screen.getByRole("button", { name: "下一步" }));
  expect(screen.getByRole("dialog", { name: "新手引导" }).textContent).toContain("从智能推荐开始");
  fireEvent.click(screen.getByRole("button", { name: "跳过引导" }));
  expect(finish).toHaveBeenCalledOnce();
});
