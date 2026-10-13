React Bits components copied from https://github.com/DavidHDev/react-bits
at commit `496968c4117e21278f13917201b7d615bc5e7450`.

Included: ClickSpark, SpotlightCard, ShinyText, ElectricBorder, Carousel, GridScan, Lanyard (TypeScript + CSS variants).
See [LICENSE.md](./LICENSE.md) for the original license and attribution.

Local adaptations are limited to compatibility with MU's desktop WebView and
its reduced-motion setting. Carousel icons use the existing Lucide icon set and controls have Chinese labels.

Lanyard adds an onUnsupported callback so MU can show a static account pass when WebGL is unavailable. Badge graphics are generated locally from account display fields; no tokens or credentials are used. GridScan camera and gyro options remain disabled.
