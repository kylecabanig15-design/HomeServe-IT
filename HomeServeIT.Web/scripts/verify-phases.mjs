import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { createWriteStream, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { createServer } from 'node:net';

const base = process.env.HOMESERVE_TEST_CONNECTION;
if (!base) throw new Error('Export HOMESERVE_TEST_CONNECTION for a local disposable MySQL server.');
const database = 'homeserve_test_browser_' + randomBytes(8).toString('hex');
const connection = base.replace(/(?:^|;)\s*database\s*=[^;]*/i, '') + ';Database=' + database;
const password = randomBytes(24).toString('base64') + 'Aa1!';
const portProbe = createServer();
await new Promise((resolvePort, reject) => { portProbe.on('error', reject); portProbe.listen(0, '127.0.0.1', resolvePort); });
const port = portProbe.address().port;
await new Promise(r => portProbe.close(r));
const env = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development',
  ConnectionStrings__DefaultConnection: connection, TestData__Password: password,
  TestData__UseCurrentModel: 'true',
  HOMESERVE_BASE_URL: 'http://127.0.0.1:' + port, HOMESERVE_ISOLATED_FIXTURE: '1',
  HOMESERVE_FIXTURE_PASSWORD: password, HOMESERVE_ADMIN_EMAIL: 'admin@test.invalid',
  HOMESERVE_ADMIN_PASSWORD: password };
mkdirSync('App_Data', { recursive: true });
const logs = createWriteStream('App_Data/phase-verification.log');
function run(command, args) {
  return new Promise((resolveRun, reject) => {
    const child = spawn(command, args, { env, stdio: ['ignore', 'pipe', 'pipe'] });
    child.stdout.pipe(logs, { end: false });
    child.stderr.pipe(logs, { end: false });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? resolveRun() : reject(new Error(command + ' failed; see App_Data/phase-verification.log')));
  });
}
let server;
try {
  await run('dotnet', ['run', '--no-build', '--no-launch-profile', '--', '--reset-test-data']);
  await run('dotnet', ['run', '--no-build', '--no-launch-profile', '--', '--check-completion-dates']);
  server = spawn('dotnet', ['run', '--no-build', '--no-launch-profile', '--urls', env.HOMESERVE_BASE_URL], { env, stdio: ['ignore', 'pipe', 'pipe'] });
  server.stdout.pipe(logs, { end: false });
  server.stderr.pipe(logs, { end: false });
  let ready = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { if ((await fetch(env.HOMESERVE_BASE_URL)).ok) { ready = true; break; } } catch {}
    await new Promise(r => setTimeout(r, 200));
  }
  if (!ready) throw new Error('Test application did not start.');
  await run(process.execPath, [resolve('node_modules/@playwright/test/cli.js'), 'test', '--project=chromium-desktop', '--project=webkit-desktop', '--workers=1', ...process.argv.slice(2)]);
  console.log('Isolated browser checks passed. Server and browser results: App_Data/phase-verification.log');
} finally {
  if (server && server.exitCode === null) {
    const stopped = new Promise(r => server.once('exit', r));
    server.kill('SIGTERM');
    await stopped;
  }
  await run('dotnet', ['run', '--no-build', '--no-launch-profile', '--', '--delete-test-data']);
  logs.end();
}
