import type { Game } from "./types";

const normalize = (text: string) => text.normalize("NFKC").toLowerCase().replace(/[^\p{L}\p{N}]/gu, "");

export function matchesGameSearch(game: Game, query: string): boolean {
  const search = normalize(query);
  return !search || [game.title, game.steamAppId || "", ...(game.searchTerms || [])]
    .some(term => normalize(term).includes(search));
}
