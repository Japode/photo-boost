import { StrictMode } from "react";
import { hydrateRoot } from "react-dom/client";
import { App } from "./App";
import { BASE } from "./content";
import { langFor } from "./routes";
import "./index.css";

const root = document.getElementById("root");
if (!root) {
  throw new Error("#root is missing from index.html");
}

// The prerender wrote this language's markup; the client hydrates it in place, so it has to
// read the same language off the same path.
const path = `/${window.location.pathname.slice(BASE.length)}`;
hydrateRoot(
  root,
  <StrictMode>
    <App lang={langFor(path)} />
  </StrictMode>,
);
