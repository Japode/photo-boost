import { SiteButton } from "@viglet/viglet-design-system/site";
import { releasesUrl, speedRowsFor } from "../content";
import { useLang, useWords } from "../i18n";

// The sections after the experience: what the reader asks once they have seen it work. Is my
// photo uploaded? How fast is it? What else does it get right? Can I have it?

export function Privacy() {
  const privacy = useWords().privacy;
  return (
    <section id="privacy" className="privacy">
      <div className="wrap privacy-grid reveal">
        <div>
          <p className="eyebrow">{privacy.eyebrow}</p>
          <h2>{privacy.title}</h2>
          <p className="privacy-body">{privacy.body}</p>
        </div>
        <ul className="privacy-facts">
          {privacy.facts.map((f) => (
            <li key={f.v}>
              <b>{f.k}</b>
              <span>{f.v}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

export function Speed() {
  const speed = useWords().speed;
  const rows = speedRowsFor(useLang());
  return (
    <section id="speed" className="speed">
      <div className="wrap speed-grid">
        <div className="sec-head sec-head-left reveal">
          <p className="eyebrow">{speed.eyebrow}</p>
          <h2>{speed.title}</h2>
          <p>{speed.lead}</p>
        </div>
        <div className="speed-table reveal">
          <table>
            <thead>
              <tr>
                {speed.head.map((h) => (
                  <th key={h} scope="col">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.op}>
                  <th scope="row">{r.op}</th>
                  <td>{r.size}</td>
                  <td className="speed-time">{r.time}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="speed-note">{speed.note}</p>
        </div>
      </div>
    </section>
  );
}

export function Details() {
  const details = useWords().details;
  return (
    <section className="details">
      <div className="wrap">
        <div className="sec-head reveal">
          <p className="eyebrow">{details.eyebrow}</p>
          <h2>{details.title}</h2>
        </div>
        <ul className="details-grid">
          {details.items.map((d, i) => (
            <li key={d.title} className="detail reveal" style={{ transitionDelay: `${(i % 4) * 60}ms` }}>
              <span className="detail-ico" aria-hidden="true">
                {d.icon}
              </span>
              <h3>{d.title}</h3>
              <p>{d.body}</p>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

export function Download() {
  const download = useWords().download;
  return (
    <section id="download" className="download">
      <div className="wrap">
        <div className="download-card reveal">
          <div className="download-main">
            <p className="eyebrow">{download.eyebrow}</p>
            <h2>
              {download.title} <span className="download-status">{download.status}</span>
            </h2>
            <p>{download.body}</p>
            <SiteButton href={releasesUrl} rel="noopener">
              {download.cta}
            </SiteButton>
          </div>
          <div className="download-side">
            <ul className="download-reqs">
              {download.requirements.map((r) => (
                <li key={r}>{r}</li>
              ))}
            </ul>
            <h3>{download.licensesTitle}</h3>
            <p>{download.licenses}</p>
          </div>
        </div>
      </div>
    </section>
  );
}
