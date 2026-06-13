// React Dashboard — a JavaScript plugin whose UI is a React app.
//
// This demonstrates HTML/React rendering for JS plugins: the plugin registers a *web tool*
// whose page is a self-contained React app (React + Babel loaded from a CDN, so there is no
// build step). The page talks to the plugin over the host bridge: `abiotic.request(obj)`
// returns a Promise that the plugin's `handleMessage` resolves — here, with a JSON summary of
// the open player save read through the host. `abiotic.onEvent` lets the page refresh when the
// host pushes an event.

var HTML = `<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<style>
  :root { color-scheme: dark; }
  body { font-family: system-ui, sans-serif; background: #0c1a24; color: #dceff9; margin: 0; padding: 18px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  .sub { color: #8fb8d0; font-size: 12px; margin-bottom: 14px; }
  .stats { display: flex; gap: 12px; flex-wrap: wrap; margin-bottom: 14px; }
  .stat { background: #132736; border: 1px solid #224158; border-radius: 8px; padding: 10px 16px; min-width: 90px; }
  .stat .v { font-size: 22px; font-weight: 700; color: #71c5f6; }
  .stat .l { font-size: 11px; color: #8fb8d0; text-transform: uppercase; letter-spacing: 1px; }
  button { background: #f89a4f; color: #07121a; border: 0; border-radius: 6px; padding: 8px 16px; font-weight: 700; cursor: pointer; }
  ul { list-style: none; padding: 0; } li { padding: 4px 0; border-bottom: 1px solid #1b3648; }
  .lv { color: #7fe9e2; font-weight: 700; } .xp { color: #587c93; font-size: 12px; }
</style>
<!-- Loaded from a CDN for a zero-build sample. For a SHIPPING plugin, prefer bundling these
     assets in the plugin folder (a directory-served web tool) or pin Subresource Integrity
     hashes: <script integrity="sha384-..." crossorigin="anonymous" ...>. CDN loading also
     needs internet at runtime. -->
<script crossorigin="anonymous" src="https://unpkg.com/react@18/umd/react.production.min.js"></script>
<script crossorigin="anonymous" src="https://unpkg.com/react-dom@18/umd/react-dom.production.min.js"></script>
<script crossorigin="anonymous" src="https://unpkg.com/@babel/standalone/babel.min.js"></script>
</head>
<body>
<div id="root">Loading…</div>
<script type="text/babel">
const { useState, useEffect, useCallback } = React;

function Stat({ label, value }) {
  return <div className="stat"><div className="v">{value}</div><div className="l">{label}</div></div>;
}

function Dashboard() {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);

  const load = useCallback(() => {
    abiotic.request({ type: "playerSummary" })
      .then(d => { setData(d); setError(null); })
      .catch(e => setError(String(e)));
  }, []);

  useEffect(() => { load(); abiotic.onEvent(load); }, [load]);

  if (error) return <div><h1>Error</h1><p>{error}</p><button onClick={load}>Retry</button></div>;
  if (!data) return <div>Loading…</div>;

  const hasPlayer = data.skillCount !== undefined;
  if (!hasPlayer) {
    return (
      <div>
        <h1>React Dashboard</h1>
        <div className="sub">Open a player save in the editor, then refresh.</div>
        <button onClick={load}>Refresh</button>
      </div>
    );
  }

  return (
    <div>
      <h1>{data.file || "Player save"}</h1>
      <div className="sub">Rendered with React, inside a plugin, reading the save via the host bridge.</div>
      <div className="stats">
        <Stat label="Money" value={data.money} />
        <Stat label="Skills" value={data.skillCount} />
        <Stat label="Top level" value={data.topLevel} />
        <Stat label="Recipes" value={data.recipeCount} />
      </div>
      <button onClick={load}>Refresh</button>
      <h3>Skills</h3>
      <ul>
        {(data.skills || []).map((s, i) => (
          <li key={i}>{s.name} <span className="lv">Lv {s.level}</span> <span className="xp">{Math.round(s.xp)} XP</span></li>
        ))}
      </ul>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<Dashboard />);
</script>
</body>
</html>`;

abiotic.registerWebTool({
    id: "react-dashboard",
    title: "React Dashboard",
    glyph: "⚛️",
    html: HTML,
    // The bridge handler: the React page calls abiotic.request({type:'playerSummary'}) and this
    // resolves it with the host's player-save summary JSON.
    handleMessage: function (message, ctx) {
        var req;
        try { req = JSON.parse(message); } catch (e) { req = {}; }
        if (req.type === "playerSummary") {
            return ctx.playerSummaryJson();
        }
        return JSON.stringify({ error: "unknown request: " + (req.type || "") });
    }
});

abiotic.on("save.opened", function (e) {
    abiotic.log.info("react-dashboard: save opened " + e.savePath);
});
