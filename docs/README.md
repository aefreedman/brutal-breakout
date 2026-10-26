# Breakout guides

These maintained guides organize existing material by audience:

## Player

Controls and observable game rules, without implementation or test receipts:

- [Playing Breakout and Shmup](player/guide.md)

## Design

Designer-facing JSON, tuning workflow, presentation and layout rules:

- [Configuration and live tuning](design/configuration.md) — inspector layout, field effects, Save/Reload, window, gameplay and input settings.
- [Presentation and Shmup layout](design/presentation.md) — palettes, style, trails, compound shapes and spawn lattice.

## Engineering

Code ownership, dependencies, native lifecycle, rendering, validation and evidence limitations:

- [Development and repository guide](engineering/development.md) — packages, editor-free builds, source map and release requirements.
- [Simulation architecture and native ownership](engineering/architecture.md)
- [Rendering, media and diagnostics](engineering/media.md) — text/audio/font provenance, showcase and shared drawing API.
- [Validation and evidence limits](engineering/validation.md) — scoped commands, hidden GPU receipts and permission-gated visible checks.
- [Performance design and limits](engineering/performance.md)
- [Physics settings and quality budget](engineering/physics.md)
- [Execution settings and scheduling](engineering/execution.md)

Physics/execution tuning stays in engineering because its quality, ownership and scheduling contracts are inseparable from the settings. Designer guides link to it rather than duplicate it.

All command examples run from the **game repository root**, regardless of document location. [`Config/`](../Config/) is authoritative; examples describe shipped values, not hidden fallback defaults. See the [project README](../README.md) for requirements and quick start. Disposable experiments and execution reports do not belong here.
