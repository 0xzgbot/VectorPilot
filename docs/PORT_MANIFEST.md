# VectorPilot — Port Manifest

**Generated:** 2026-08-06 · repo `/Users/zgbot/Desktop/ShopPilot` @ `ShopPilot` · total Swift LOC: **44,846**

**Portability split:** 23,168 LOC portable (Foundation-only, incl. all engine code) · 21,678 LOC platform-bound (Metal/AppKit/SwiftUI/CoreText/CoreGraphics/IOKit).

## 1. ShopPilot source targets — file-by-file classification (translation input)

| Target | File | LOC | Class |
|---|---|---|---|
| ShopPilotCore | ArrayCopyAndMerge.swift | 245 | PORTABLE (Foundation-only) |
| ShopPilotCore | ArrayCopyToolpath.swift | 198 | PLATFORM: SwiftUI |
| ShopPilotCore | Autosaver.swift | 90 | PLATFORM: SwiftUI |
| ShopPilotCore | BitmapComponent.swift | 216 | PORTABLE (Foundation-only) |
| ShopPilotCore | BitmapHeightfield.swift | 249 | PLATFORM: CoreGraphics, ImageIO |
| ShopPilotCore | BoundaryFromComponents.swift | 200 | PORTABLE (Foundation-only) |
| ShopPilotCore | CoachCopy.swift | 27 | PORTABLE (Foundation-only) |
| ShopPilotCore | ColorPalette.swift | 61 | PLATFORM: SwiftUI |
| ShopPilotCore | CombineModeTeacher.swift | 157 | PORTABLE (Foundation-only) |
| ShopPilotCore | CombineModes.swift | 216 | PORTABLE (Foundation-only) |
| ShopPilotCore | CombineStatus.swift | 56 | PORTABLE (Foundation-only) |
| ShopPilotCore | Component.swift | 238 | PORTABLE (Foundation-only) |
| ShopPilotCore | ConsoleLog.swift | 73 | PORTABLE (Foundation-only) |
| ShopPilotCore | Constants.swift | 40 | PORTABLE (Foundation-only) |
| ShopPilotCore | Core.swift | 7 | PORTABLE (Foundation-only) |
| ShopPilotCore | Date+Extensions.swift | 68 | PORTABLE (Foundation-only) |
| ShopPilotCore | DemoableGoldenPath.swift | 100 | PORTABLE (Foundation-only) |
| ShopPilotCore | DimensionFormatter.swift | 56 | PORTABLE (Foundation-only) |
| ShopPilotCore | DirtyRegion.swift | 138 | PLATFORM: SwiftUI |
| ShopPilotCore | DocumentLoader.swift | 155 | PLATFORM: SwiftUI |
| ShopPilotCore | DocumentSaver.swift | 113 | PLATFORM: SwiftUI |
| ShopPilotCore | DocumentVariable.swift | 35 | PORTABLE (Foundation-only) |
| ShopPilotCore | DocumentVariableBindings.swift | 173 | PORTABLE (Foundation-only) |
| ShopPilotCore | DocumentVariableUI.swift | 216 | PLATFORM: SwiftUI |
| ShopPilotCore | DocumentVariablesModel.swift | 158 | PORTABLE (Foundation-only) |
| ShopPilotCore | DoubleSidedJob.swift | 223 | PORTABLE (Foundation-only) |
| ShopPilotCore | DrillToolpath.swift | 509 | PLATFORM: SwiftUI |
| ShopPilotCore | DrivenDimensions.swift | 227 | PORTABLE (Foundation-only) |
| ShopPilotCore | DynamicHeightModifier.swift | 172 | PORTABLE (Foundation-only) |
| ShopPilotCore | ExportBlocker.swift | 96 | PLATFORM: SwiftUI |
| ShopPilotCore | FeatureFlag.swift | 106 | PORTABLE (Foundation-only) |
| ShopPilotCore | FileUtilities.swift | 136 | PORTABLE (Foundation-only) |
| ShopPilotCore | FinishToolpath.swift | 263 | PORTABLE (Foundation-only) |
| ShopPilotCore | GCodeStreamer.swift | 260 | PORTABLE (Foundation-only) |
| ShopPilotCore | GRBLPostProcessor.swift | 268 | PLATFORM: SwiftUI |
| ShopPilotCore | GadgetPreview.swift | 136 | PORTABLE (Foundation-only) |
| ShopPilotCore | GadgetToolpaths.swift | 278 | PORTABLE (Foundation-only) |
| ShopPilotCore | GoldenFixtures.swift | 322 | PLATFORM: SwiftUI |
| ShopPilotCore | GoldenJob.swift | 404 | PORTABLE (Foundation-only) |
| ShopPilotCore | HeightfieldSTLExporter.swift | 129 | PORTABLE (Foundation-only) |
| ShopPilotCore | HeightfieldToolpath.swift | 325 | PORTABLE (Foundation-only) |
| ShopPilotCore | HeightfieldVisualizer.swift | 115 | PORTABLE (Foundation-only) |
| ShopPilotCore | InlayToolpath.swift | 344 | PORTABLE (Foundation-only) |
| ShopPilotCore | Job+Extensions.swift | 26 | PORTABLE (Foundation-only) |
| ShopPilotCore | Job.swift | 137 | PLATFORM: SwiftUI |
| ShopPilotCore | JobRecipe.swift | 85 | PORTABLE (Foundation-only) |
| ShopPilotCore | JobSheetGenerator.swift | 242 | PORTABLE (Foundation-only) |
| ShopPilotCore | KeepOutZones.swift | 204 | PLATFORM: SwiftUI |
| ShopPilotCore | Layer.swift | 180 | PLATFORM: SwiftUI |
| ShopPilotCore | LayerVisibility.swift | 85 | PORTABLE (Foundation-only) |
| ShopPilotCore | LevelManager.swift | 100 | PORTABLE (Foundation-only) |
| ShopPilotCore | Logging.swift | 103 | PORTABLE (Foundation-only) |
| ShopPilotCore | MachineSession.swift | 272 | PORTABLE (Foundation-only) |
| ShopPilotCore | MachineStartPreflight.swift | 38 | PORTABLE (Foundation-only) |
| ShopPilotCore | MachineTransport.swift | 374 | PORTABLE (Foundation-only) |
| ShopPilotCore | MaterialDatabase.swift | 90 | PORTABLE (Foundation-only) |
| ShopPilotCore | MaterialSetup.swift | 188 | PORTABLE (Foundation-only) |
| ShopPilotCore | MaterialStore.swift | 41 | PORTABLE (Foundation-only) |
| ShopPilotCore | MergedToolpath.swift | 80 | PLATFORM: SwiftUI |
| ShopPilotCore | MetalCompositeRender.swift | 203 | PORTABLE (Foundation-only) |
| ShopPilotCore | MetalPreview.swift | 227 | PLATFORM: SwiftUI |
| ShopPilotCore | MockTransport.swift | 110 | PORTABLE (Foundation-only) |
| ShopPilotCore | ModelOperations.swift | 346 | PORTABLE (Foundation-only) |
| ShopPilotCore | MultiSidedView.swift | 141 | PLATFORM: SwiftUI |
| ShopPilotCore | Nesting.swift | 446 | PORTABLE (Foundation-only) |
| ShopPilotCore | PathDiff.swift | 213 | PLATFORM: SwiftUI |
| ShopPilotCore | PhotoVCarveToolpath.swift | 127 | PORTABLE (Foundation-only) |
| ShopPilotCore | PocketToolpath.swift | 464 | PLATFORM: SwiftUI |
| ShopPilotCore | PowerUser.swift | 435 | PORTABLE (Foundation-only) |
| ShopPilotCore | PreflightGate.swift | 169 | PORTABLE (Foundation-only) |
| ShopPilotCore | PreviewEmptyState.swift | 26 | PORTABLE (Foundation-only) |
| ShopPilotCore | PreviewManager.swift | 240 | PLATFORM: SwiftUI |
| ShopPilotCore | ProductionGoldenJobs.swift | 213 | PORTABLE (Foundation-only) |
| ShopPilotCore | ProfileToolpath.swift | 518 | PLATFORM: SwiftUI |
| ShopPilotCore | QuickEngraveEngine.swift | 211 | PORTABLE (Foundation-only) |
| ShopPilotCore | ReliefComponent.swift | 103 | PORTABLE (Foundation-only) |
| ShopPilotCore | RotaryLaser.swift | 509 | PORTABLE (Foundation-only) |
| ShopPilotCore | RotaryWrapToolpath.swift | 127 | PORTABLE (Foundation-only) |
| ShopPilotCore | RoughToolpath.swift | 210 | PORTABLE (Foundation-only) |
| ShopPilotCore | STLHeightfield.swift | 310 | PORTABLE (Foundation-only) |
| ShopPilotCore | STLManager.swift | 257 | PORTABLE (Foundation-only) |
| ShopPilotCore | SculptEngine.swift | 243 | PORTABLE (Foundation-only) |
| ShopPilotCore | SculptMode.swift | 260 | PORTABLE (Foundation-only) |
| ShopPilotCore | SerialPortEnumerator.swift | 130 | PORTABLE (Foundation-only) |
| ShopPilotCore | ShapeHandles.swift | 220 | PORTABLE (Foundation-only) |
| ShopPilotCore | ShapeTools.swift | 175 | PORTABLE (Foundation-only) |
| ShopPilotCore | Sheet.swift | 101 | PLATFORM: SwiftUI |
| ShopPilotCore | ShopPilotPackagePayload.swift | 79 | PORTABLE (Foundation-only) |
| ShopPilotCore | SketchCarveToolpath.swift | 161 | PORTABLE (Foundation-only) |
| ShopPilotCore | SpecialtyToolpaths.swift | 1081 | PORTABLE (Foundation-only) |
| ShopPilotCore | StageGate.swift | 28 | PORTABLE (Foundation-only) |
| ShopPilotCore | StatusParser.swift | 189 | PORTABLE (Foundation-only) |
| ShopPilotCore | StockSheetPresets.swift | 143 | PORTABLE (Foundation-only) |
| ShopPilotCore | String+Extensions.swift | 52 | PORTABLE (Foundation-only) |
| ShopPilotCore | SweepExtrudeWeave.swift | 384 | PORTABLE (Foundation-only) |
| ShopPilotCore | TilingManager.swift | 393 | PORTABLE (Foundation-only) |
| ShopPilotCore | TimeEstimator.swift | 180 | PLATFORM: SwiftUI |
| ShopPilotCore | ToolDatabase.swift | 444 | PORTABLE (Foundation-only) |
| ShopPilotCore | ToolpathLinkManager.swift | 227 | PORTABLE (Foundation-only) |
| ShopPilotCore | ToolpathPreflight.swift | 307 | PORTABLE (Foundation-only) |
| ShopPilotCore | ToolpathRecalculator.swift | 132 | PLATFORM: SwiftUI |
| ShopPilotCore | ToolpathSimulator.swift | 425 | PLATFORM: SwiftUI |
| ShopPilotCore | ToolpathTemplates.swift | 152 | PORTABLE (Foundation-only) |
| ShopPilotCore | ToolpathTree.swift | 842 | PLATFORM: SwiftUI |
| ShopPilotCore | TransportFactory.swift | 125 | PLATFORM: SwiftUI |
| ShopPilotCore | VCarveEngine.swift | 479 | PORTABLE (Foundation-only) |
| ShopPilotCore | Validation.swift | 99 | PORTABLE (Foundation-only) |
| ShopPilotCore | VectorSelector.swift | 215 | PLATFORM: SwiftUI |
| ShopPilotCore | VectorValidator.swift | 631 | PORTABLE (Foundation-only) |
| ShopPilotCore | ZeroPlane.swift | 130 | PORTABLE (Foundation-only) |
| ShopPilotCore | ZeroPlaneAndBoundary.swift | 225 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | ArrayCopy.swift | 228 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | BitmapTracer.swift | 535 | PLATFORM: CoreGraphics, ImageIO |
| ShopPilotGeometry | BooleanOperations.swift | 208 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | BooleanOps.swift | 170 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | CreateShapes.swift | 41 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | DXFParser.swift | 234 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | EngravingFontPack.swift | 247 | PLATFORM: CoreText |
| ShopPilotGeometry | ExpressionParser.swift | 181 | PLATFORM: SwiftUI |
| ShopPilotGeometry | FilletExtend.swift | 243 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | Geometry.swift | 7 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | GeometryBridge.swift | 167 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | JoinCloseTrim.swift | 408 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | Kernel.swift | 406 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | KeyholeGadget.swift | 69 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | LayerManager.swift | 198 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | MeasurementTool.swift | 89 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | NestingEngine.swift | 370 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | NodeEditor.swift | 221 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | SVGImporter.swift | 770 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | SignRecipeManager.swift | 225 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | TextObject.swift | 64 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | TextRenderer.swift | 498 | PLATFORM: CoreText |
| ShopPilotGeometry | TextTool.swift | 636 | PLATFORM: CoreText |
| ShopPilotGeometry | Transform.swift | 308 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | VectorDXFExporter.swift | 101 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | VectorOffset.swift | 457 | PORTABLE (Foundation-only) |
| ShopPilotGeometry | VectorPreflight.swift | 364 | PORTABLE (Foundation-only) |
| ShopPilotSerial | MachineProfile.swift | 292 | PORTABLE (Foundation-only) |
| ShopPilotSerial | RealSerialTransport.swift | 302 | PORTABLE (Foundation-only) |
| ShopPilotSerial | Serial.swift | 7 | PORTABLE (Foundation-only) |
| ShopPilotSerial | SerialPortEnumerator.swift | 118 | PORTABLE (Foundation-only) |
| ShopPilot (UI) | App.swift | 79 | PLATFORM: SwiftUI |
| ShopPilot (UI) | AppSession.swift | 2838 | PLATFORM: SwiftUI, UniformTypeIdentifiers |
| ShopPilot (UI) | AppSettings.swift | 50 | PLATFORM: SwiftUI |
| ShopPilot (UI) | BrowserPanels.swift | 360 | PLATFORM: SwiftUI |
| ShopPilot (UI) | CoachPanelView.swift | 112 | PLATFORM: SwiftUI |
| ShopPilot (UI) | CommandPaletteView.swift | 284 | PLATFORM: SwiftUI |
| ShopPilot (UI) | Commands.swift | 233 | PORTABLE (Foundation-only) |
| ShopPilot (UI) | ContentView.swift | 1548 | PLATFORM: AppKit, SwiftUI, UniformTypeIdentifiers |
| ShopPilot (UI) | CutToMachineBridge.swift | 160 | PLATFORM: SwiftUI |
| ShopPilot (UI) | DesignCanvasView.swift | 682 | PLATFORM: SwiftUI |
| ShopPilot (UI) | DesignSystem.swift | 306 | PLATFORM: SwiftUI |
| ShopPilot (UI) | DocumentVariablesPanel.swift | 332 | PLATFORM: SwiftUI |
| ShopPilot (UI) | FileOperations.swift | 145 | PLATFORM: SwiftUI |
| ShopPilot (UI) | IconEnforcement.swift | 312 | PLATFORM: SwiftUI |
| ShopPilot (UI) | ImportHubView.swift | 479 | PLATFORM: SwiftUI, UniformTypeIdentifiers |
| ShopPilot (UI) | InspectorShell.swift | 308 | PLATFORM: SwiftUI |
| ShopPilot (UI) | KeepOutZonesPanel.swift | 205 | PLATFORM: SwiftUI |
| ShopPilot (UI) | MachineConnection.swift | 959 | PLATFORM: SwiftUI |
| ShopPilot (UI) | MachineController.swift | 412 | PLATFORM: SwiftUI |
| ShopPilot (UI) | MaterialSetupView.swift | 241 | PLATFORM: SwiftUI |
| ShopPilot (UI) | ModelStageView.swift | 393 | PLATFORM: SwiftUI, UniformTypeIdentifiers |
| ShopPilot (UI) | NewJobView.swift | 193 | PLATFORM: SwiftUI |
| ShopPilot (UI) | PreferencesView.swift | 49 | PLATFORM: SwiftUI |
| ShopPilot (UI) | PreflightDoctorView.swift | 136 | PLATFORM: SwiftUI |
| ShopPilot (UI) | RecipePicker.swift | 201 | PLATFORM: SwiftUI |
| ShopPilot (UI) | SafetyDisclaimerView.swift | 41 | PLATFORM: SwiftUI |
| ShopPilot (UI) | SessionDocument.swift | 7 | PORTABLE (Foundation-only) |
| ShopPilot (UI) | SheetListView.swift | 215 | PLATFORM: SwiftUI |
| ShopPilot (UI) | SpecialtyParamsForms.swift | 442 | PLATFORM: SwiftUI |
| ShopPilot (UI) | StageEnum.swift | 110 | PLATFORM: SwiftUI |
| ShopPilot (UI) | StageRailView.swift | 116 | PLATFORM: SwiftUI |
| ShopPilot (UI) | ToolBrowserView.swift | 316 | PLATFORM: SwiftUI |
| ShopPilot (UI) | ToolPickerMenu.swift | 116 | PLATFORM: SwiftUI |
| ShopPilot (UI) | ToolpathPreviewView.swift | 322 | PLATFORM: SwiftUI |
| ShopPilot (UI) | ToolpathTreeView.swift | 280 | PLATFORM: SwiftUI |

## 2. Verify CLT harness — 97 executables (the executable spec)

Each is a plain-Swift CLI (no XCTest) in `Sources/ShopPilotVerifyXXXX/`, registered in Package.swift. Port each as an xUnit/NUnit test with identical assertions + golden files. PASS line convention:

| Verify target | PASS line |
|---|---|
| ShopPilotVerify0201b | `SPK-0201b verification: PASS` |
| ShopPilotVerify0203c | `SPK-0203c verification: PASS` |
| ShopPilotVerify0210 | `ShopPilotVerify0210: PASS — hand-derived offset goldens (mit` |
| ShopPilotVerify0211 | `PASS` |
| ShopPilotVerify0214 | `ShopPilotVerify0214: PASS - grid layout, circular centers + ` |
| ShopPilotVerify0215 | `ShopPilotVerify0215: PASS - 90° fillet math, rectangle round` |
| ShopPilotVerify0308 | `ShopPilotVerify0308: PASS — zone geometry (rect/circle/polyg` |
| ShopPilotVerify0310a | `PASS` |
| ShopPilotVerify0312 | `ShopPilotVerify0312: PASS — TimeEstimator math (exact cuttin` |
| ShopPilotVerify0314a | `ShopPilotVerify0314a PASS — selectAll populates all vector i` |
| ShopPilotVerify0318 | `ShopPilotVerify0318: PASS — OFF copy warns toolpaths don't f` |
| ShopPilotVerify0319 | `PASS` |
| ShopPilotVerify0404a | `SPK-0404a verification: PASS` |
| ShopPilotVerify0404c | `  PASS — \(streamer.currentLine)/\(streamer.totalLines) line` |
| ShopPilotVerify0412a | `SPK-0412a verification: PASS` |
| ShopPilotVerify0415 | `PASS` |
| ShopPilotVerify0417a | `  PASS: \(streamer.currentLine)/\(streamer.totalLines) lines` |
| ShopPilotVerify0418 | `ShopPilotVerify0418: PASS — 10k-line stream on SimulatorTran` |
| ShopPilotVerify0500 | `SPK-0500 verification: PASS` |
| ShopPilotVerify0600 | `ShopPilotVerify0600: PASS — design→cut→dirty/recalc→preview(` |
| ShopPilotVerify0601 | `ShopPilotVerify0601: PASS — recipe→glyphs→border→V-Carve nod` |
| ShopPilotVerify0603 | `PASS` |
| ShopPilotVerify0604 | `PASS` |
| ShopPilotVerify1100 | `SPK-1100 verification: PASS` |
| ShopPilotVerify1101 | `SPK-1101 feed verification: PASS` |
| ShopPilotVerify1101FlipH | `SPK-1101 flip-horizontal verification: PASS` |
| ShopPilotVerify1101b | `PASS` |
| ShopPilotVerify1101d | `ShopPilotVerify1101d: PASS — join/close/weld/subtract/inters` |
| ShopPilotVerify1101e | `ShopPilotVerify1101e: PASS — fixture parse, viewBox transfor` |
| ShopPilotVerify1101f | `ShopPilotVerify1101f: PASS — nudge/flip/rotate-90/scale sema` |
| ShopPilotVerify1101g | `ShopPilotVerify1101g: PASS — LINE/LWPOLYLINE/CIRCLE/ARC pars` |
| ShopPilotVerify1101h | `SPK-1101h verification: PASS` |
| ShopPilotVerify1101i | `PASS` |
| ShopPilotVerify1101j | `SPK-1101j verification: PASS` |
| ShopPilotVerify1101k | `SPK-1101k verification: PASS` |
| ShopPilotVerify1102c | `ShopPilotVerify1102c: PASS — dirty→recalc→clean cycle; all f` |
| ShopPilotVerify1102d | `ShopPilotVerify1102d: PASS — pocket/drill/v-carve engines + ` |
| ShopPilotVerify1102e | `SPK-1102e verification: PASS` |
| ShopPilotVerify1102f | `SPK-1102f verification: PASS` |
| ShopPilotVerify1102g | `ShopPilotVerify1102g: PASS — full-tree export (move parity),` |
| ShopPilotVerify1102h | `PASS: \(message)` |
| ShopPilotVerify1102i | `PASS: \(message)` |
| ShopPilotVerify1103 | `ShopPilotVerify1103 PASS — empty-state copy shown when no gc` |
| ShopPilotVerify1103a | `ShopPilotVerify1103a PASS — segments=\(segments.count) sampl` |
| ShopPilotVerify1103c | `ShopPilotVerify1103c PASS — segments=\(segments.count) selec` |
| ShopPilotVerify1103d | `ShopPilotVerify1103d: PASS — full-tree wireframe spans both ` |
| ShopPilotVerify1103e | `PASS — material sim sheet-aware (200x100x20), removal along ` |
| ShopPilotVerify1104 | `ShopPilotVerify1104 PASS — reset (0x18/Ctrl-X) clears alarm/` |
| ShopPilotVerify1104a | `SPK-1104a verification: PASS` |
| ShopPilotVerify1104b | `ShopPilotVerify1104b: PASS — full-tree handoff, zero-bytes o` |
| ShopPilotVerify1104c | `SPK-1104c verification: PASS` |
| ShopPilotVerify1104d | `ShopPilotVerify1104d: PASS — connect→load→preflight→start→ho` |
| ShopPilotVerify1106a | `ShopPilotVerify1106a: PASS — text→curves→V-Carve in one flow` |
| ShopPilotVerify1106b | `ShopPilotVerify1106b: PASS — recipe→text→curves→V-Carve node` |
| ShopPilotVerify1120 | `SPK-1120 verification: PASS` |
| ShopPilotVerify1123 | `SPK-1123 verification: PASS` |
| ShopPilotVerify1125 | `SPK-1125 verification: PASS` |
| ShopPilotVerify1130 | `SPK-1130 verification: PASS` |
| ShopPilotVerify1131 | `SPK-1131 verification: PASS` |
| ShopPilotVerify1132 | `PASS` |
| ShopPilotVerify1133 | `ShopPilotVerify1133: PASS — 13 classes, 17 catalog entries /` |
| ShopPilotVerify1133b | `PASS` |
| ShopPilotVerify1136a | `ShopPilotVerify1136a: PASS — §R2 key presence, round-trip, l` |
| ShopPilotVerify1136b | `ShopPilotVerify1136b: PASS — §M key presence, round-trip, le` |
| ShopPilotVerify1136c | `ShopPilotVerify1136c: PASS — §N key presence, round-trip, le` |
| ShopPilotVerify1136d | `ShopPilotVerify1136d: PASS — §O key presence, round-trip, le` |
| ShopPilotVerify1137 | `ShopPilotVerify1137: PASS — layer lock/editability, layer-id` |
| ShopPilotVerify3DGolden | `ShopPilotVerify3DGolden: PASS — hand-checked goldens: 3D rou` |
| ShopPilotVerify3DRest | `PASS` |
| ShopPilotVerify3DUI | `PASS` |
| ShopPilotVerify3Da | `ShopPilotVerify3Da: PASS — box footprint+top, pyramid apex, ` |
| ShopPilotVerify3Db | `ShopPilotVerify3Db: PASS — z-level rough (5 passes, peak ski` |
| ShopPilotVerifyBitmapHF | `ShopPilotVerifyBitmapHF: PASS - pixel build, 2D smoothing, d` |
| ShopPilotVerifyCombine | `ShopPilotVerifyCombine: PASS — Add cap/Subtract clamp/Merge/` |
| ShopPilotVerifyDragKnife | `ShopPilotVerifyDragKnife: PASS — offset center path, CCW/CW ` |
| ShopPilotVerifyFMR013 | `ShopPilotVerifyFMR013: PASS — gap math, wide-gap trigger (er` |
| ShopPilotVerifyFMR014 | `ShopPilotVerifyFMR014: PASS — through-cut trigger (warning, ` |
| ShopPilotVerifyFMR016 | `ShopPilotVerifyFMR016: PASS — R016 datum-z0 gate item (block` |
| ShopPilotVerifyFMR019 | `ShopPilotVerifyFMR019: PASS — two-tool block (error, split C` |
| ShopPilotVerifyGadget | `ShopPilotVerifyGadget: PASS - keyhole loop, slot width, arc ` |
| ShopPilotVerifyGolden25D | `PASS` |
| ShopPilotVerifyInlayRecipe | `ShopPilotVerifyInlayRecipe: PASS — 4 presets, recipe→params,` |
| ShopPilotVerifyPhotoVCarve | `ShopPilotVerifyPhotoVCarve: PASS — luminance→depth (black −1` |
| ShopPilotVerifyProfileToolpath | `PASS: \(message)` |
| ShopPilotVerifyRotaryWrap | `ShopPilotVerifyRotaryWrap: PASS — X→A mapping (quarter=90°, ` |
| ShopPilotVerifySHAKEd | `\nRESULT: SPK-SHAKEd \(total) checks — PASS` |
| ShopPilotVerifySHAKEe | `\nRESULT: SPK-SHAKEe \(total) checks — PASS` |
| ShopPilotVerifySHAKEf | `\nRESULT: SPK-SHAKEf \(total) checks — PASS` |
| ShopPilotVerifySHAKEg | `\nRESULT: SPK-SHAKEg \(total) checks — PASS` |
| ShopPilotVerifySculpt | `PASS — sculpt: falloff curve, brush raise/lower, inflate/def` |
| ShopPilotVerifySketchCarve | `ShopPilotVerifySketchCarve: PASS — Sobel edge gating (step −` |
| ShopPilotVerifySpecialty | `ShopPilotVerifySpecialty: PASS - prism grooves+depths, fluti` |
| ShopPilotVerifyStudio | `ShopPilotVerifyStudio: PASS - text glyphs, bitmap trace, DXF` |
| ShopPilotVerifyTexture | `ShopPilotVerifyTexture: PASS — parallel 4 grooves @−2.5, cap` |
| ShopPilotVerifyUI601 | `  [1/5] re-entrant @Published append returns (no deadlock) P` |
| ShopPilotVerifyUI609 | `  [1/6] Hold / Resume / Reset work after leaving the Machine` |
| ShopPilotVerifyVCarveClear | `ShopPilotVerifyVCarveClear: PASS — default off, clearance-fi` |

## 3. Data assets to export (platform-neutral)

| Asset | Where | Port action |
|---|---|---|
| `.shoppilot` document format | Job.swift / DocumentSaver.swift (JSON Codable) | **The interop contract.** Keep byte-compatible: JSON schema spec exported from the Codable models |
| 72 stock sheet presets | StockSheetPresets.swift | Export as JSON (already Codable) |
| Tool DB (13 classes, 17 defaults) | ToolDatabase.swift (JSON via UserDefaults key) | Export seed as JSON; 3-part linkage per SPK-1133 |
| Golden G-code fixtures | fixtures/ + golden verify CLTs | Copy verbatim — the parity gate |
| GRBL post | GRBLPostProcessor.swift (hardcoded) | Replace with template engine (SPK-1134) — grammar mirrors the reference `.pp` `[X|C|X|1.3]` specifiers |
| Job sheet | JobSheetGenerator.swift | HTML template + PDF (reference uses HTML/A4) |

## 4. Platform-bound surface (rewrite per platform)

| Surface | Files | Windows replacement |
|---|---|---|
| 3D preview (Metal) | MetalPreview.swift, MetalCompositeRender.swift, PreviewManager.swift, ToolpathSimulator.swift, HeightfieldVisualizer.swift, DirtyRegion.swift | DirectX 11 via HelixToolkit.SharpDX (WPF) or Vortice; heightfield + toolpath sim + playback 2x–16x |
| Text → curves | TextRenderer.swift, TextTool.swift, EngravingFontPack.swift (CoreText) | DirectWrite (SharpDX/DirectWrite or SkiaSharp) |
| Bitmap trace | BitmapTracer.swift (CoreGraphics/ImageIO) | SkiaSharp / WPF Imaging (TiffBitmapDecoder etc.) |
| Serial transport | RealSerialTransport.swift, SerialPortEnumerator.swift (IOKit/ORSSerial) | System.IO.Ports (Win32 COM) behind the same MachineTransport protocol |
| File panels / app shell | DocumentLoader/Saver, Autosaver, ExportBlocker, FileOperations (AppKit/SwiftUI) | WPF dialogs (Microsoft.Win32.OpenFileDialog / SaveFileDialog) |
| All UI | Sources/ShopPilot (35 files) | WPF re-implementation; UX spec = UX_STAGE_SYSTEM.md (stage rail, progressive disclosure), not pixel-for-pixel |

## 5. The reference surface to match (installer-verified, V12.5.1.0)

- **App stack:** native Win32 C++ x64 (MSVC140), OpenSceneGraph/OpenGL 3D, pstill PDF, BugSplat crash, NSIS installer. No machine-control UI — control = posts + machine DB (ShopPilot's differentiator).
- **Data formats:** postp.ppdb SQLite (800 posts + 935 machine configs), .vtdb SQLite tool DBs (GUID 3-part linkage: db_geom_id / db_cut_data_id / db_mach_cut_data_id), 17 binary `.default` toolpath defaults, 72 stock sheets, 91 Lua gadgets.
- **`.pp` grammar:** `VAR X_POSITION = [X|C|X|1.3]`, UNITS, LINE_ENDING, block numbering, `begin REVISION_COMMENT` blocks; GRBL posts shipped: Grbl (inch/mm), Grbl WrapY2A (inch/mm), Easel-Grbl, OpenBuilds GRBL, Shapeoko.
- **Strategies (17 + variants):** Profile, Pocket, V-Carve, Drilling, Chamfer, Fluting, 3D Rough, 3D Finish, Swept Profile/Moulding, Texture, Quick Engrave, Bevel Carving, Thread Milling, Laser family, Photo V-Carve, V-Carve Inlay, Prism Carving, Plasma Profile.
- **Shared subsystems:** tabs (2D/3D/auto), ramps (5 types), leads (arc/line), ordering/sorting/merge, boundaries + offsets, tolerances, climb/conventional, keep-out zones, tiling, nesting, toolpath templates, 2x–16x simulation.

Full inventory: `docs/planning/INSTALLER_BREAKDOWN.md` + `FEATURE_PARITY_MATRIX.md` §R in the ShopPilot repo; raw evidence re-unpackable from `AspireTrialEdition_Setup.exe` (see PC_SETUP.md §4).