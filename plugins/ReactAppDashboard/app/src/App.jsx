import { useCallback, useEffect, useState } from "react";
import { request, onEvent } from "./abiotic.js";

function Stat({ label, value }) {
  return (
    <div className="stat">
      <div className="v">{value ?? "-"}</div>
      <div className="l">{label}</div>
    </div>
  );
}

export default function App() {
  const [data, setData] = useState(null);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState("");

  const load = useCallback(() => {
    request({ type: "playerSummary" })
      .then((d) => setData(d))
      .catch((e) => setNote("Error: " + e));
  }, []);

  // Load on mount, and refresh whenever the host pushes a save change event.
  useEffect(() => {
    load();
    onEvent(load);
  }, [load]);

  // Drive the APP: ask the host (via the plugin) to run a save operation, then refresh.
  const maxSkills = useCallback(async () => {
    setBusy(true);
    setNote("");
    try {
      const res = await request({ type: "runOperation", operationId: "react-max-skills" });
      setNote(res && res.wrote ? "Skills maxed and saved." : "No change was needed.");
      load();
    } finally {
      setBusy(false);
    }
  }, [load]);

  // Drive the APP: show a native toast / alert from the web UI.
  const greet = useCallback(() => request({ type: "toast", message: "Hello from React!" }), []);

  if (!data) return <div className="card">Loading…</div>;

  const hasPlayer = data.skillCount !== undefined;
  if (!hasPlayer) {
    return (
      <div className="card">
        <h1>React App Dashboard</h1>
        <p className="sub">A Vite + React app running as a plugin. Open a player save, then refresh.</p>
        <div className="row">
          <button onClick={load}>Refresh</button>
          <button onClick={greet}>Toast in app</button>
        </div>
      </div>
    );
  }

  return (
    <div className="card">
      <h1>{data.file || "Player save"}</h1>
      <p className="sub">Built with Vite + React · runs as a plugin · talks to the editor over the host bridge.</p>

      <div className="stats">
        <Stat label="Money" value={data.money} />
        <Stat label="Skills" value={data.skillCount} />
        <Stat label="Top level" value={data.topLevel} />
        <Stat label="Recipes" value={data.recipeCount} />
      </div>

      <div className="row">
        <button onClick={load} disabled={busy}>Refresh</button>
        <button onClick={maxSkills} disabled={busy} className="primary">{busy ? "Working…" : "Max skills (edit the save)"}</button>
        <button onClick={greet} disabled={busy}>Toast in app</button>
      </div>
      {note && <p className="note">{note}</p>}

      <h3>Skills</h3>
      <ul>
        {(data.skills || []).map((s, i) => (
          <li key={i}>
            <span>{s.name}</span>
            <span className="lv">Lv {s.level}</span>
            <span className="xp">{Math.round(s.xp)} XP</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
