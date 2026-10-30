import type { Vector } from "./Vector";
import type { PlayerDepthOfFieldMode } from "./PlayerDepthOfFieldMode";

export interface PlayerData {
  force: Vector<2>;
  depthOfFieldMode: PlayerDepthOfFieldMode;
  // NaN when the keyframe does not say, which is the depth of field of the beatmap's parallax settings
  depthOfFieldFocus: number;
}
