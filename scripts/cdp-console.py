#!/usr/bin/env python3
"""Capture browser console logs via CDP."""
import json, asyncio, websockets, urllib.request, time

async def get_console():
    # Get page target
    resp = urllib.request.urlopen("http://127.0.0.1:9222/json/list")
    targets = json.loads(resp.read())
    page_ws = None
    for t in targets:
        if t['type'] == 'page':
            page_ws = t['webSocketDebuggerUrl']
            break
    if not page_ws:
        print("No page target found")
        return
    print(f"Connecting to: {page_ws}", flush=True)
    async with websockets.connect(page_ws, max_size=10*1024*1024) as ws:
        # Enable console and runtime
        await ws.send(json.dumps({"id":1, "method":"Console.enable"}))
        await ws.send(json.dumps({"id":2, "method":"Runtime.enable"}))
        await ws.send(json.dumps({"id":3, "method":"Log.enable"}))
        await ws.send(json.dumps({"id":4, "method":"Runtime.evaluate", "params":{"expression":"location.reload()"}}))
        # Collect messages for 15 seconds
        start = time.time()
        while time.time() - start < 15:
            try:
                msg = await asyncio.wait_for(ws.recv(), timeout=1.0)
                d = json.loads(msg)
                method = d.get('method', '')
                if method == 'Runtime.consoleAPICalled':
                    args = d['params'].get('args', [])
                    text = ' '.join(a.get('description', a.get('value', str(a))) for a in args)
                    print(f"[console.{d['params']['type']}] {text}", flush=True)
                elif method == 'Log.entryAdded':
                    entry = d['params']['entry']
                    print(f"[log.{entry['level']}] {entry.get('text','')}", flush=True)
                elif method == 'Runtime.exceptionThrown':
                    exc = d['params']['exceptionDetails']
                    print(f"[exception] {exc.get('text','')}: {exc.get('exception',{}).get('description','')}", flush=True)
                elif method == 'Console.messageAdded':
                    entry = d['params']['message']
                    print(f"[console.{entry['level']}] {entry.get('text','')}", flush=True)
            except asyncio.TimeoutError:
                pass
            except Exception as e:
                print(f"[error] {e}", flush=True)

asyncio.run(get_console())
