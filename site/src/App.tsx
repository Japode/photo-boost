import { useEffect } from "react";
import type { Lang } from "./content";
import { LangContext } from "./i18n";
import { Footer, Nav } from "./components/Chrome";
import { Hero } from "./components/Hero";
import { Journey } from "./components/Journey";
import { Details, Download, Privacy, Speed } from "./components/Sections";
import { Studio } from "./components/Studio";

// One page per language, ordered by what a reader wants next rather than by feature: see it
// work (hero), try it on each kind of photo (studio), learn the four moves (journey), then the
// questions that come after wanting it: privacy, speed, details, and where to get it.
export function App({ lang }: Readonly<{ lang: Lang }>) {
  useEffect(() => {
    // What is already on screen is shown at once, without waiting for the observer's first
    // callback; only what lies below the fold waits to be scrolled to.
    const all = Array.from(document.querySelectorAll<HTMLElement>(".reveal"));
    const visible = (el: HTMLElement) => el.getBoundingClientRect().top < window.innerHeight;
    all.filter(visible).forEach((el) => el.classList.add("in"));
    const els = all.filter((el) => !el.classList.contains("in"));
    if (!("IntersectionObserver" in window)) {
      els.forEach((el) => el.classList.add("in"));
      return;
    }
    // Only toggles each element's own class; it never scrolls anything.
    const io = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("in");
            io.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.12 },
    );
    els.forEach((el) => io.observe(el));
    return () => io.disconnect();
  }, []);

  return (
    <LangContext.Provider value={lang}>
      <Nav />
      <main>
        <Hero />
        <Studio />
        <Journey />
        <Privacy />
        <Speed />
        <Details />
        <Download />
      </main>
      <Footer />
    </LangContext.Provider>
  );
}
