# Swag.Wasm

> Web-friendly module for SWAG, an animation player for `.lsb` and `.vgd` files

## Usage

```sh
npm install swag-wasm
```

If you're using Vite, add this to your `vite.config.js`:

```js
optimizeDeps: {
  exclude: ['swag-wasm']
}
```

## Example

```js
import createSwag from "swag-wasm";
import { BeatmapFormat } from "swag-wasm/data/BeatmapFormat";

const swag = await createSwag(canvas);
swag.start(true, true); // enable post processing and text rendering

const app = swag.app;
if (!app) {
  console.error("failed to initialize SWAG");
  return;
}

// fetch animation data
const beatmapDataResponse = await fetch("/path/to/level.vgd");
const beatmapData = await beatmapDataResponse.text();

// load it into SWAG
const beatmapService = app.beatmapService;
beatmapService.loadBeatmap(beatmapData, BeatmapFormat.Vgd);

// enter main render loop
const update = (time) => {
  app.processFrame(time / 1000); // convert time from millsecond to second
  requestAnimationFrame(update);
};

requestAnimationFrame(update);
```
