# VisionPlayground roadmap

VisionPlayground grows through useful applications and reusable, independently testable vision components. Each project owns its dependencies, data contracts, and evidence. The six new projects begin at a runnable foundation milestone; real-model and application milestones remain open.

## Current portfolio

| Project | Foundation | Real-model work | Application milestone |
| --- | --- | --- | --- |
| [EyePause](Eyedetection/README.md) | Existing implementation | Landmark inference implemented; [manual validation](Eyedetection/docs/VALIDATION.md) still matters | Windows desktop/tray application and portable build implemented |
| [CoDriverSim](CoDriverSim/README.md) | MetaDrive launcher, simulator-truth following warnings, typed observations and JSON demo | Camera/model adapters and Comma runtime compatibility remain unimplemented | Verify Windows rendering, then add controlled hazards, event recording and companion |
| [SceneFlow](SceneFlow/docs/PLAN.md) | Directed segment crossings from supplied tracks | Recorded-video detector/tracker adapters and independent crossing labels | Zones, dwell-time analytics, live sources, exports, UI |
| [DocLens](DocLens/docs/PLAN.md) | Receipt fields from supplied labeled OCR lines | OCR, preprocessing, line grouping, broader labeled layouts | Review/correction workflow and document exports |
| [InspectLab](InspectLab/docs/PLAN.md) | Thresholded maps and four-connected regions | Normal-image baseline, held-out defect labels, threshold calibration | Image inspection and reviewed outcomes |
| [MeasureLab](MeasureLab/docs/PLAN.md) | Reference-plane homography and quadrilateral measurement | Camera calibration, reference detection, physical measurements | Measurement review, uncertainty, positioning integrations |
| [FrameFind](FrameFind/docs/PLAN.md) | Stable cosine ranking of supplied embeddings | Real image/text encoder and relevance judgments | Persistent media index and searchable timestamps |
| [RedactLab](RedactLab/docs/PLAN.md) | Masks and redacted copies from supplied regions | Region detection, OCR integration, video tracking, missed-region labels | Human review and image/video export |

## Development order

1. Complete and verify all six foundations: API, typed observations, CLI, labeled synthetic fixtures, domain tests, packaged resources, and independent wheel installation.
2. Develop **SceneFlow → DocLens → InspectLab** with real adapters and datasets. These add tracking, document understanding, and anomaly detection to EyePause's landmark/temporal work.
3. Develop **MeasureLab → FrameFind → RedactLab**. Their foundations are already useful for geometry, indexing, and masking; their real-input stages need independent evidence.
4. Add each application interface after its processing and error behavior have been evaluated. Choose the interface around the user workflow at that milestone.

## Integration opportunities

- **SceneFlow → RedactLab:** translate per-frame track boxes into redaction regions, preserving timestamps and categories. A track ID is continuity metadata, not a person's identity.
- **DocLens → RedactLab:** use reviewed OCR source regions as sensitive_text masks. Field provenance allows users to inspect the exact pixels being removed.
- **DocLens / SceneFlow → FrameFind:** attach extracted text and events as searchable metadata alongside media embeddings.
- **MeasureLab → InspectLab:** normalize a known inspection plane before scoring, retaining image/score-map transformations and physical units.
- **EyePause → future live applications:** evaluate its capture/mailbox and replay patterns for reuse once another working consumer needs them. Do not couple these foundations to EyePause's Windows dependencies.

Integration initially uses explicit adapters between independent records. Shared code becomes a separate package only when two real consumers establish the same semantics and an extraction has compatibility tests.

## Completion gates

**Foundation:** independent installation, useful deterministic processing, synthetic provenance, typed API, module/console commands, input validation, source preservation, tests, and installed-wheel demo checks.

**Real-model integration:** versioned model/adapter configuration; documented data provenance and splits; a simple baseline; independently labeled held-out evaluation; error examples; measured runtime and quality with reproducible commands. Synthetic examples do not satisfy this gate.

**Application:** a usable workflow, correction/recovery paths, appropriate exports, supported-platform validation, and explicit deployment/packaging instructions. Model and dataset redistribution requirements must be resolved before bundling them.

**Reusable component:** a documented public contract, independent tests, no UI coupling, and at least two proven consumers before shared-runtime extraction.

## Verification evidence

Run `python scripts/check_scaffolds.py` at the root with Python 3.12 or 3.13. It creates fresh environments and builds each project's wheel, then verifies tests, installed commands, packaged resources, demo output, and local documentation links. Logs and reports are generated under `build/scaffolds` and excluded from version control.

The CI workflow defines Windows/Linux and Python 3.12/3.13 coverage. Consult its actual run results rather than treating configured coverage as completed validation. [Local implementation checks](VALIDATION.md) record what was executed during scaffolding.
