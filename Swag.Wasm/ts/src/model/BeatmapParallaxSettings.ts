import { NativeObject } from "../NativeObject";

export class BeatmapParallaxSettings extends NativeObject {
  get layerCount(): number {
    return this.wasm._beatmapParallaxSettings_getLayerCount(this.ptr);
  }

  get isDepthOfFieldEnabled(): boolean {
    return this.wasm._beatmapParallaxSettings_getIsDepthOfFieldEnabled(this.ptr) !== 0;
  }

  set isDepthOfFieldEnabled(value: boolean) {
    this.wasm._beatmapParallaxSettings_setIsDepthOfFieldEnabled(this.ptr, value ? 1 : 0);
  }

  get depthOfField(): number {
    return this.wasm._beatmapParallaxSettings_getDepthOfField(this.ptr);
  }

  set depthOfField(value: number) {
    this.wasm._beatmapParallaxSettings_setDepthOfField(this.ptr, value);
  }

  getLayerDepth(layerIndex: number): number {
    return this.wasm._beatmapParallaxSettings_getLayerDepth(this.ptr, layerIndex);
  }

  setLayerDepth(layerIndex: number, depth: number): void {
    this.wasm._beatmapParallaxSettings_setLayerDepth(this.ptr, layerIndex, depth);
  }
}
