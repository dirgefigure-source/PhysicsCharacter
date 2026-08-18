# Cylindrical World Visual Repeater

## Decision

Keep one canonical gameplay world and generate presentation-only copies of static
`SpriteRenderer` content at `-world.Width` and `+world.Width`.

## Why

- Gameplay objects, colliders, scripts, and save identities remain unique.
- The camera can see across the cylindrical seam without exposing empty space.
- Adding static scenery only requires assigning its root to the repeater.
- Dynamic actors continue to use `CylindricalRigidbodyGroup2D` instead of copies.

## Constraints

- Sources are static scenery. Moving scenery needs a future synchronized proxy type.
- Only `SpriteRenderer` is supported in V1. Tilemaps and particle systems are future work.
- Gameplay queries across the seam must use `CylindricalWorld2D.ShortestDelta`.

## Validation

Use differently colored landmarks near both boundaries. At either seam the opposite
landmark must remain visible, while the Hierarchy contains no duplicated collider,
rigidbody, or gameplay script.
