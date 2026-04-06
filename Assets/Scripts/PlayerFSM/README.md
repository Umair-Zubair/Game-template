# PlayerFSM

FSM bots that fight the boss during training. Attach one script the Warrior or Hunter prefab, enable it for training, disable it for human play.

---

## Architecture

Same pattern as the VoidbornGoddess boss FSM.

- `IPlayerFSMState` — interface (OnEnter, OnUpdate, OnExit)
- `PlayerFSMStateMachine` — holds current state, drives transitions
- `PlayerFSMController` — abstract base MonoBehaviour. Auto-finds player components, exposes movement/attack helpers. Enabling it sets the player to AI control; disabling hands control back to the keyboard.
- The 6 FSM scripts — each inherits the base, defines its own states as inner classes

---

## The 6 Profiles

### Warrior

`Aggressive`
Rushes the boss constantly. No retreat at all. Calls RequestAttack every frame so the combo always chains. Jumps to close height gaps. Will attack even nearly out of stamina.

`Defensive`
Slow approach (60% speed) → one hit, no combo → full retreat to 5.5f → wait until stamina is back to 70% → repeat. Never jumps. The whole thing is very deliberate and methodical.

`Balanced`
Normal approach → attacks with a 60% chance of chaining the combo → backs off slightly for 0.4s → 0.5s pause → re-engage. Jumps occasionally if the boss is much higher up. Middle ground in every way.

---

### Hunter

`Aggressive`
Prefers to be at 3f — close for a ranged fighter. Fires one shot, repositions, fires again in a loop. Keeps advancing while reloading instead of stopping. 50% chance of jumping for an aerial shot each time.

`Defensive`
Stays at 7f at all times. Fires one shot, repositions. Strongly prefers aerial shots (70%). If the boss gets within 4f, immediately jumps and sprints away. Won't re-engage until fully reloaded and at 55% stamina.

`Balanced`
Sweet spot at 4.5f. Fires 2-shot bursts, then slides back 0.4s after each burst. 30% aerial shot chance. Retreats if boss closes to 2f. Re-engages once ammo is back to 50% and stamina is at 35%.

---

## How the Code Works

Each FSM script has a set of `states` (inner classes implementing `IPlayerFSMState`). Every frame, the state machine calls `OnUpdate` on whichever state is currently active. States don't run in parallel — only one is active at a time.

Inside `OnUpdate`, the state checks a set of conditions (distance to boss, stamina level, ammo, whether grounded, timers, etc.) and decides what to do — either keep doing the current behavior, or call `FSM.ChangeState(...)` to hand off to a different state. That's the whole rule system: conditions checked every frame, transitions triggered when thresholds are crossed.

Movement and attacking go through the base controller's helpers. For example, calling `MoveToward()` sets the player's horizontal input toward the boss. `RequestAttack()` tells the attack script to fire on the next frame — calling it every frame simulates holding the button (which is how combo chaining works), calling it once fires a single hit. `RequestJump(duration)` triggers a jump and controls height through how long the jump button is held.

The base controller also handles the enable/disable behavior. When the FSM component is enabled, it sets `IsAIControlled = true` on `PlayerController`, which makes the player ignore keyboard input. When disabled, it resets everything and the player responds to the keyboard again. On respawn, `OnEnable` fires and the FSM restarts from its initial state.

---

## How to Setup

Add one FSM script to the same GameObject as the player prefab. Set `Target Tag` in the Inspector (default `Enemy`). Enable for training, disable for human play. Only one FSM should be active per player at a time.
