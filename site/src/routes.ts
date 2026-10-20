import { BASE, languages, words, type Lang } from "./content";

// One page per language. The table is what the design system's prerender reads, and what
// scripts/prerender.mjs reads to set each page's <html lang> and its hreflang links.
// PB50: the Japode organization site's custom domain, which GitHub serves this project under.
export const SITE_ORIGIN = "https://japode.com";

// Absolute, because a platform fetching the social card is not on this origin.
export const OG_IMAGE = `${SITE_ORIGIN}${BASE}og.png`;

const order: Lang[] = ["en", "pt"];

export const ROUTES = order.map((lang) => ({
  path: languages[lang].path,
  lang,
  htmlLang: languages[lang].htmlLang,
  title: words[lang].meta.title,
  description: words[lang].meta.description,
}));

/** The language a route path is in; English for anything else. */
export function langFor(path: string): Lang {
  return path.replace(/\/+$/, "") === "/pt" ? "pt" : "en";
}

/** The canonical and og:url for a route, carrying the base. */
export function canonicalUrl(path: string): string {
  const rel = path === "/" ? "" : `${path.replace(/^\//, "")}/`;
  return `${SITE_ORIGIN}${BASE}${rel}`;
}

/** Where a route's HTML is written, relative to dist/: "" for the root. */
export function outputDir(path: string): string {
  return path === "/" ? "" : path.replace(/^\//, "");
}
