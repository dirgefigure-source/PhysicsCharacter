# Configuring action motion layers

`WalkCyclePdMotorDriver` exposes an **Action Motion Layers** list. Each entry is a reusable one-shot action that can be mixed over Idle, Walk, or Run.

## Add an action

1. Export the source animation to a motion JSON file.
2. Add an element to **Action Motion Layers** on Player.
3. Set **Action Name** to a unique descriptive name.
4. Configure **Input Action** and its binding.
5. Assign the JSON under **Motion > Motion Json**.
6. Select one or more values under **Motion > Target Limbs**.
7. Tune playback speed, blend-in duration, blend-out duration, and priority.

No FK, grounding, IK, locomotion, or motor code needs to change.

## Conflict rules

- Actions affecting different limbs play simultaneously.
- If active actions affect the same limb, the higher priority action wins.
- If their priorities match, the most recently triggered action wins.
- Idle, Walk, or Run remains the base pose beneath the winning action.
- Leg action results still pass through the existing grounding and anti-penetration solver.

## Example priorities

- Cosmetic gesture: `0`
- Normal attack or cast: `10`
- Block: `20`
- Hit reaction: `50`
- Forced full-body response: use a dedicated high-level state instead of an action layer.
