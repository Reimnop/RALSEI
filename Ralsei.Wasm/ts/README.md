# Ralsei.Wasm

> Web-friendly module for RALSEI, an animation player for `.lsb` and `.vgd` files

## Usage

```sh
npm install ralsei-wasm
```

If you're using Vite, add this to your `vite.config.js`:

```js
optimizeDeps: {
  exclude: ['ralsei-wasm']
}
```

## Example

```js
import createRalsei from "ralsei-wasm";
import { BeatmapFormat } from "ralsei-wasm/data/BeatmapFormat";

const ralsei = await createRalsei(canvas);
ralsei.start(true, true); // enable post processing and text rendering

const app = ralsei.app;
if (!app) {
  console.error("failed to initialize RALSEI");
  return;
}

// fetch animation data
const beatmapDataResponse = await fetch("/path/to/level.vgd");
const beatmapData = await beatmapDataResponse.text();

// load it into RALSEI
const beatmapService = app.beatmapService;
beatmapService.loadBeatmap(beatmapData, BeatmapFormat.Vgd);

// enter main render loop
const update = (time) => {
  app.processFrame(time / 1000); // convert time from millsecond to second
  requestAnimationFrame(update);
};

requestAnimationFrame(update);
```
