import { createContext, useContext } from "react";
import { words, type Lang } from "./content";

// The page's language, set once at the top of the tree from the route, and read by every
// section. A context rather than a prop through each component, so a section added later
// cannot render in the wrong language by forgetting to pass it on.
export const LangContext = createContext<Lang>("en");

export function useLang(): Lang {
  return useContext(LangContext);
}

export function useWords() {
  return words[useLang()];
}
