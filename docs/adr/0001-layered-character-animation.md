# ADR-0001: Use a layered, incrementally extracted character-animation architecture

## Status
Accepted

## Context
The working controller combines input, state transitions, JSON parsing, sampling, pose mixing, FK, grounding, IK, and physics. The visual behavior is mature, so a full rewrite has high regression risk, while adding each new action directly to the controller will not scale.

## Decision
Use a base locomotion layer plus reusable masked action layers. Extract modules from the existing implementation in dependency order: motion clips, layer playback, rig/FK, then grounding/IK. Keep the existing controller as the orchestrator until the extracted APIs are stable.

## Consequences

### Positive
- New motion data no longer requires new parsing or timing code.
- Limb masks and layer priority can support future action composition.
- Each migration stage can be compared against the proven behavior.

### Negative
- The controller remains partially monolithic during migration.
- Temporary adapter fields exist until rig and grounding extraction are complete.

### Neutral
- JSON remains the interchange format and the existing exporter is unchanged.

## Alternatives Considered
- Full rewrite: rejected because it risks regressions in the mature grounding behavior.
- Unity Animator replacement: rejected because the project uses custom projected JSON angles and 2D rigidbody/FK/IK application.
- Continue adding action-specific fields: rejected because code size grows with every motion.
