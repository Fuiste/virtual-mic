# delay reference plugin

a small stereo feedback delay implemented through virtual mic's public c# plugin api. it runs as an external dll and exposes its controls through metadata; no app changes are needed to add it.

## build and install

with the .net 10 sdk, run this from the repo root:

```powershell
.\scripts\build-plugin.ps1
```

1. in virtual mic, open **plugins → open plugins folder**, then close the app.
2. copy the generated `artifacts/sample-plugins/delay` folder into `%LOCALAPPDATA%\VirtualMic\plugins`.
3. reopen virtual mic, choose **delay** in the effects menu, and click **+**.
4. choose **mic**, **sounds**, or **both** on the row. enable **listen** to hear sounds; also enable **hear microphone** to audition voice effects through headphones.

keep `plugin.json`, `VirtualMic.Delay.dll`, and `VirtualMic.Delay.deps.json` together. the dll is trusted code running inside the app. restarting reloads installed plugins; rebuilding does not install or enable them automatically.

## controls

| control | range | default | behavior |
| --- | --- | --- | --- |
| time | 30–1000 ms | 240 ms | spacing between repeats |
| feedback | 0–85% | 35% | how much of each repeat is fed back; 0% gives one repeat |
| mix | 0–100% | 30% | linear dry/wet balance; 0% dry, 100% delayed only |

time is rounded down to whole sample frames. feedback and mix edits are smoothed; the initial settings apply immediately. time changes move the read position immediately and can click. this deliberately compact reference has no tempo sync, filtering, fractional delay, or time-change crossfade. keep feedback and source levels modest when first listening: repeated audio can build up.

left and right channels have separate histories. every row/source gets a fresh processor, so **both** cannot leak mic echoes into sounds-only monitoring. a natural pad ending lets repeats decay. **stop sounds**, bypass, and removal of a source target clear its delay history. parameter edits and reordering retain history. mix 0% keeps the delay running internally so raising mix can reveal existing tails.

## how to extend it

- `DelayPlugin.Definition` declares the stable `example.delay` id and numeric controls.
- `Create` allocates a private ring buffer off the render thread.
- `Process` reads delayed frames, feeds them back into the same channel, and blends dry/wet audio in place. it performs no i/o, locks, or buffer allocation.
- `Reset` clears history; `Dispose` owns no unmanaged resources.

copy the project, use your own assembly/effect ids, update the manifest, and add parameters or processing. the only project reference is `VirtualMic.PluginApi`. see the repo's **docs/plugins.md** for the full contract and standalone sdk setup.

the build runs impulse timing, dry/wet, feedback/wraparound, parameter-edit/reset, independent-instance, and extreme-setting tests against the compiled dll. publishing repeats those checks inside the self-contained host. real listening and call-client qualification remain separate.

licensed under the mit license included as `LICENSE`; keep it with distributed copies.
