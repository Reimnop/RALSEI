import SwagFactory from "./wasm/Swag.Wasm";
import { Module } from "./Module";

export default async function createSwag(canvas: HTMLCanvasElement): Promise<Module> {
  const module = await SwagFactory();
  return new Module(module, canvas);
}