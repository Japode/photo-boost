import { SiteButton } from "@viglet/viglet-design-system/site";
import { heroFor } from "../content";
import { useLang } from "../i18n";
import { Compare } from "./Compare";

// The opening is the product working, not a description of it: the first thing a reader can do
// is drag the divider over a real 4x result. The words sit beside it and stay short.
export function Hero() {
  const hero = heroFor(useLang());
  return (
    <header className="hero">
      <div className="hero-glow" aria-hidden="true" />
      <div className="wrap hero-grid">
        <div className="hero-copy">
          <p className="eyebrow">{hero.eyebrow}</p>
          <h1>{hero.title}</h1>
          <p className="hero-sub">{hero.sub}</p>
          <div className="hero-actions">
            <SiteButton href="#try">{hero.primary}</SiteButton>
            <SiteButton variant="ghost" href="#download">
              {hero.secondary}
            </SiteButton>
          </div>
        </div>
        <figure className="hero-stage">
          <Compare
            before={hero.before}
            after={hero.after}
            beforeLabel={hero.beforeLabel}
            afterLabel={hero.afterLabel}
            width={hero.width}
            height={hero.height}
            label={hero.compareLabel}
            hint={hero.hint}
            eager
          />
          <figcaption>{hero.caption}</figcaption>
        </figure>
      </div>
    </header>
  );
}
