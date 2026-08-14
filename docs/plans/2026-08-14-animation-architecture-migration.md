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

### Task 3: Extract rig binding and FK

**Files:**
- Create later: `Assets/Scripts/CharacterRig2D.cs`
- Modify later: `Assets/Scripts/WalkCyclePdMotorDriver.cs`

1. Move body lookup, hinge references, static-axis offsets, and FK pose construction behind a rig API.
2. Compare joint targets at identical normalized times before and after migration.

### Task 4: Extract grounding and leg IK

**Files:**
- Create later: `Assets/Scripts/LegGroundingSolver2D.cs`
- Modify later: `Assets/Scripts/WalkCyclePdMotorDriver.cs`

1. Move ground probes, support selection, two-bone solving, virtual soles, and stop-foot locking together.
2. Preserve the current ordering: pose mixing, FK, grounding/IK, Rigidbody2D application.
3. Regression-test stopping at support-leg handoff frames and starting from Idle.

### Task 5: Reduce the controller to orchestration

1. Keep input and high-level state transitions in a thin component.
2. Rename only after scene serialization is stable; retain a compatibility wrapper if required.
3. Verify no per-frame allocations are introduced in `FixedUpdate`.

