import {Composition} from 'remotion';
import {MuPromo} from './MuPromo';

export const Root = () => <>
  <Composition id="MU-Landscape" component={MuPromo} width={1920} height={1080} fps={60} durationInFrames={1800} />
  <Composition id="MU-Portrait" component={MuPromo} width={1080} height={1920} fps={60} durationInFrames={1800} />
</>;
