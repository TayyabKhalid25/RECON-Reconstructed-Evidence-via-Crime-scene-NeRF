---
name: simulating-ballistics-and-spatter
description: Use when implementing the ballistics overlay, blood spatter proxies, or impact detection, or when projectiles pass through walls, curve wrong, or spatter looks arbitrary.
---

# Simulating ballistics and spatter

## Overview

At muzzle velocity a projectile crosses metres per physics step, so a plain rigidbody tunnels straight through colliders. The fix is fixed substeps with a swept raycast. Correctness is checkable for free: with drag off, the trajectory must match the analytic parabola.

## Ballistics: substep + sweep

```csharp
// semi-implicit Euler, swept; dt small enough that v*dt << collider thickness
Vector3 pos = origin, vel = muzzleVelocity;
for (int i = 0; i < maxSubsteps; i++) {
    vel += Physics.gravity * dt;
    if (useDrag) vel -= 0.5f * airDensity * dragCoef * area / mass * vel.magnitude * vel * dt;
    Vector3 next = pos + vel * dt;
    Vector3 step = next - pos;
    if (Physics.Raycast(pos, step.normalized, out var hit, step.magnitude, mask)) {
        OnImpact(hit, vel);                    // impact point, normal, velocity at impact
        break;
    }
    pos = next;                                // record pos for the trail renderer
}
```

- Sweep **every** substep from previous to new position; the raycast is the tunneling guard.
- **The free unit test**: drag off, compare against `y = x·tanθ − g·x²/(2·v²·cos²θ)` at several x. Divergence beyond epsilon means dt is too big or integration is wrong. This is TC material for Chapter 7.
- Run identical shots into both collider types (AR planes vs Poisson mesh) and record impact-point divergence vs decimation level; that comparison is the project's most interesting result.

## Spatter: the BPA ellipse, a directional proxy, not fluids

Standard bloodstain-pattern relationship: stain width over length approximates the sine of the impact angle (angle between trajectory and the surface plane).

```csharp
void OnImpact(RaycastHit hit, Vector3 vel) {
    Vector3 dir = vel.normalized;
    float sinAlpha = Mathf.Abs(Vector3.Dot(dir, hit.normal));   // sin of angle from surface
    Vector3 major = Vector3.ProjectOnPlane(dir, hit.normal).normalized; // travel dir on surface
    var decal = SpawnDecal(hit.point, hit.normal, major);
    decal.localScale = new Vector3(baseSize * sinAlpha, baseSize, 1f);  // minor/major = sin α
}
```

- Droplets: emit N directions in a cone around the source direction (parameters: impact velocity, volume, angle), raycast each, place an ellipse decal per hit.
- Major axis aligns to the incoming direction projected onto the surface; elongation points away from the source.
- Scope discipline: this is explicitly a **directional proxy**. No fluid dynamics, no rheology; the report says so and cites the BPA relationship, and never claims forensic validity beyond direction indication.

## Gotchas

| Symptom | Cause |
|---|---|
| Shots pass through walls anyway | Sweeping only some substeps, or layer mask excludes the collider |
| Trajectory bends oddly with drag on | Drag applied after position update, or dt too coarse; halve dt and compare |
| Ellipses all circles | sinAlpha computed against the wrong vector (use trajectory·normal, not view) |
| Decals z-fight on the splat scene | Offset decal by a few mm along the normal; splats have no depth-tested surface |
