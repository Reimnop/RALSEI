import RalseiFactory from "./wasm/Ralsei.Wasm";
import { Module } from "./Module";

export default async function createRalsei(canvas: HTMLCanvasElement): Promise<Module> {
  const module = await RalseiFactory();
  return new Module(module, canvas);
}