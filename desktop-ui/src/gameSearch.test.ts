import { describe, expect, it } from "vitest";
import { matchesGameSearch } from "./gameSearch";
import type { Game } from "./types";

const assetto: Game = { id: "assetto", title: "assettocorsa", cover: null, state: "Unconfigured", mode: null,
  steamAppId: "244210", searchTerms: ["assettocorsa", "244210", "Assetto Corsa", "神力科莎"] };

describe("本地游戏名称搜索", () => {
  it.each(["神力科莎", "Assetto Corsa", "ASSETTO CORSA", "assetto-corsa", " Ａｓｓｅｔｔｏ　Ｃｏｒｓａ ", "244210"])("通过 %s 找到 Steam 神力科莎", query => {
    expect(matchesGameSearch(assetto, query)).toBe(true);
  });
  it("兼容没有搜索别名的旧数据并忽略空格差异", () => {
    expect(matchesGameSearch({ ...assetto, searchTerms: undefined }, "Assetto Corsa")).toBe(true);
    expect(matchesGameSearch(assetto, "   ")).toBe(true);
  });
  it("不把其他游戏或竞速续作误判成原版", () => {
    expect(matchesGameSearch(assetto, "竞速")).toBe(false);
    expect(matchesGameSearch({ ...assetto, title: "Fixture Game", steamAppId: null, searchTerms: [] }, "神力科莎")).toBe(false);
  });
});
