# Feature notes

Details that do not belong in the README.

## Algorithms worth knowing about

**Pocket** — Contour-offset loops plus a raster clipped to the inset boundary in both axes. The raster can re-cover ground the loops already cleared. A redundant pass is preferred over leaving floor uncut.

**V-carve** — A discrete clearance field on a grid; ridge cells chain into polylines; depth comes from local channel width. Wide bulbs cut deeper than a narrow neck. The skeleton is a grid approximation, not an exact medial axis.

**Weave** — `WeaveReliefGenerator` builds an interlaced heightfield (plain / twill / satin). Time/cost still come from the estimator.

**Gadgets** — Lua (MoonSharp) with a sandbox and timeout, plus a script editor. HTML gadget dialogs are not implemented.

**Import stubs** — V3M, SketchUp, and Rhino 3DM report honest “not implemented” status rather than a fake parser.

**Machine** — Simulator loopback is what CI covers. A physical controller is not part of the automated gate.
