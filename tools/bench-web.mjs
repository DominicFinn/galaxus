// Runs the web build's benchmark in headless Chrome and prints its console output.
//   node tools/bench-web.mjs http://127.0.0.1:8766/index.html?bench [--all] [--timeout=600]
// Headless, so it never takes focus and Chrome doesn't throttle it. Needs Node 22 or later (for
// the built-in WebSocket); the one emsdk installs will do. Exits when the page prints "BENCH DONE".
import { spawn } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const args = process.argv.slice(2);
const url = args.find(a => !a.startsWith("--")) ?? "http://127.0.0.1:8766/index.html?bench";
const all = args.includes("--all");
const timeoutS = Number((args.find(a => a.startsWith("--timeout=")) ?? "--timeout=600").split("=")[1]);
const port = 9300 + Math.floor(Math.random() * 500);
const chrome = process.env.CHROME ?? "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
const profile = mkdtempSync(join(tmpdir(), "bench-chrome-"));

const proc = spawn(chrome, [
  "--headless=new", `--user-data-dir=${profile}`, `--remote-debugging-port=${port}`,
  "--no-first-run", "--no-default-browser-check", "--use-angle=swiftshader", "--enable-unsafe-swiftshader",
  "about:blank",
], { stdio: "ignore" });

const started = Date.now();
function finish(code) {
  proc.kill();
  setTimeout(() => { try { rmSync(profile, { recursive: true, force: true }); } catch {} process.exit(code); }, 500);
}
setTimeout(() => { console.log(`TIMEOUT after ${timeoutS} s`); finish(2); }, timeoutS * 1000);

async function target() {
  for (let i = 0; i < 100; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" });
      return await res.json();
    } catch { await new Promise(r => setTimeout(r, 200)); }
  }
  throw new Error("Chrome didn't start");
}

const t = await target();
const ws = new WebSocket(t.webSocketDebuggerUrl);
let id = 0;
const send = (method, params = {}) => ws.send(JSON.stringify({ id: ++id, method, params }));
ws.onopen = () => {
  send("Runtime.enable");
  send("Log.enable");
  send("Page.enable");
  send("Page.navigate", { url });
};
ws.onmessage = ev => {
  const msg = JSON.parse(ev.data);
  let text = null;
  if (msg.method === "Runtime.consoleAPICalled")
    text = msg.params.args.map(a => a.value ?? a.description ?? "").join(" ");
  else if (msg.method === "Runtime.exceptionThrown")
    text = "EXCEPTION " + (msg.params.exceptionDetails.exception?.description ?? msg.params.exceptionDetails.text);
  else if (msg.method === "Log.entryAdded")
    text = "LOG " + msg.params.entry.text;
  if (text == null) return;
  const secs = ((Date.now() - started) / 1000).toFixed(1);
  if (all || text.includes("BENCH") || text.startsWith("EXCEPTION")) console.log(`[${secs}s] ${text}`);
  if (text.includes("BENCH DONE")) finish(0);
};
