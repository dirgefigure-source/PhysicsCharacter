# Animation Architecture Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Split the proven animation system into focused modules without changing the visible Walk, Run, Punch, FK, or grounding behavior.

**Architecture:** Keep `WalkCyclePdMotorDriver` as the temporary orchestrator. Extract immutable JSON parsing/sampling first, retain the reusable motion-layer player, then migrate rig/FK and grounding in later verified stages.

**Tech Stack:** Unity 6, C#, JsonUtility, Rigidbody2D, HingeJoint2D, Input System.

---

### Task 1: Extract motion JSON parsing and sampling

**Files:**
- Create: `Assets/Scripts/MotionJsonClip.cs`
- Modify: `Assets/Scripts/WalkCyclePdMotorDriver.cs`

1. Add a clip class that validates JSON, unwraps every joint once, and samples by joint name and normalized time.
2. Replace the controller's private JSON DTOs, frame indexing, and per-motion angle arrays with `MotionJsonClip` instances.
3. Verify Walk, Run, and Punch contain every required joint and preserve existing limit warnings.
4. Check Unity compilation and compare the three motions in Play Mode.

### Task 2: Retain generic limb-layer playback

**Files:**
- Verify: `Assets/Scripts/MotionLayerPlayer.cs`
- Modify: `Assets/Scripts/WalkCyclePdMotorDriver.cs`

1. Keep playback time, normalized time, blend weight, and limb mask outside the controller.
2. Let the controller consume only the layer's sampled pose and weight.
3. Verify multi-select limb masks and retriggered Punch playback.

### Task 3: Extract rig binding and FK (completed)

**Files:**
- Created: `Assets/Scripts/CharacterRig2D.cs`
- Modified: `Assets/Scripts/WalkCyclePdMotorDriver.cs`

1. Move body lookup, hinge references, static-axis offsets, and FK pose construction behind a rig API.
2. Compare joint targets at identical normalized times before and after migration.

### Task 4: Extract grounding and leg IK (completed)

**Files:**
- Created: `Assets/Scripts/LegGroundingSolver2D.cs`
- Modified: `Assets/Scripts/WalkCyclePdMotorDriver.cs`

1. Move ground probes, support selection, two-bone solving, virtual soles, and stop-foot locking together.
2. Preserve the current ordering: pose mixing, FK, grounding/IK, Rigidbody2D application.
3. Regression-test stopping at support-leg handoff frames and starting from Idle.

### Task 5: Reduce the controller to orchestration (completed)

**Files:**
- Created: `Assets/Scripts/LocomotionPlayer.cs`
- Created: `Assets/Scripts/CharacterMotorDriver2D.cs`
- Modified: `Assets/Scripts/WalkCyclePdMotorDriver.cs`

1. Input, action triggering, serialized compatibility fields, and high-level module ordering remain in the component.
2. Idle/Walk/Run playback and transition state now live in `LocomotionPlayer`.
3. Dynamic Rigidbody2D mode, PD joint motors, and upright balance now live in `CharacterMotorDriver2D`.
4. The scene-facing component retains its original name, fields, and public controls.
5. Settings are passed as value-type snapshots; no per-frame managed collections are allocated.
