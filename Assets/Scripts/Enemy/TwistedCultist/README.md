# Twisted Cultist FSM

Boss-style enemy FSM for the Twisted Cultist, implemented with:

- `TwistedCultistController` (`BossController` base)
- `TwistedCultistStates` (`IBossState` implementations)

This implementation is intentionally pure heuristic FSM behavior.
It does not use adaptation profiles, weighted strategy, or ML decisions.

## Components Needed

Attach these to the Twisted Cultist GameObject:

- `Rigidbody2D`
- `Animator`
- `BoxCollider2D`
- `Health`
- `TwistedCultistController`

## Required Animator States

Default state names expected by `TwistedCultistController`:

- `Twisted` (spawn intro)
- `Idle`
- `Walk`
- `Attack`
- `Jump`
- `Fall`
- `Hurt`

You can rename these in the controller inspector if your animator uses different names.

## Health Animation Note

`Health` currently triggers animator parameters `hurt` and `die` on damage/death.
If your Twisted animator controller does not define these trigger parameters,
either add them or use an animator controller that already includes them.

## Attack Setup

- Set `rangedAttackPoint` to the hand/arm extension origin if available.
- If empty, the controller uses `rangedAttackOffset` from the enemy position.
- Damage is applied via overlap circle against `playerDamageLayer`.

## Behavior Loop

The heuristic flow is:

- `Spawn -> Idle -> Press -> React -> RangedAttack`
- Context-dependent transitions to `Evade` and `JumpReposition`
- `Hurt` interrupt on non-fatal damage

The state design intentionally mirrors the style of player FSM logic:
short reaction windows, probabilistic re-engage, and spacing-aware evasions.
