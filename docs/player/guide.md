# Playing Breakout and Shmup

## Pause and panel controls (both modes)

- **Select/Share** (the standardized GLFW Back button) or **F1** toggles the ImGui panel, including while it captures input. Held buttons do not repeat; focus regain and controller reconnect/replacement baseline held controls.
- **Start** toggles simulation pause/resume; it no longer restarts. The panel also has **Pause/Resume** controls and status. Input polling, rendering and panel interaction continue while physics, HP, difficulty, simulation time and trail aging stop. Opening the panel alone does not pause.
- Panel **Restart** starts a fresh run, including while paused. Restart and mode switching preserve manual pause. Pause transitions discard accumulated time, pending actions and sound tails; release fire/action before using it again after resuming. **R** remains a restart shortcut while running.
- Builds without the inspector retain Start pause and keyboard R restart. Physical Select/Share labels depend on the controller's standardized mapping.

## Breakout rules and controls

- Aim with **WASD / arrows / left stick / mouse movement**. Active keyboard direction wins over stick, then moved/clicked mouse; a stationary unused mouse does not override direction input. Centered stick preserves the last aim. Initial aim comes from `Config/gameplay.json` (the shipped file points up).
- **Space / left mouse button / gamepad south (A)** launches or catches. Shot speed and ball radius come from `Config/gameplay.json` (the shipped values are 600 logical units/second and radius 12), with no gravity or drag; collision response determines subsequent flight. Top and side walls reflect; the bottom is open. Single-hit bricks reflect the ball and disappear.
- After launch, the ball must **leave the lower catch zone and hit a wall or brick**, then return below/on the catch line before it can be caught. The restriction is on the ball center, not the cursor. Crossing the line does not catch automatically. The ball retains its configured disc color when eligible; eligibility does not add a flight halo or recolor.
- Once eligible, press from any cursor position to catch the ball below the line. Catch holds the ball at its actual position, not a spawn point. **Release and press separately** to relaunch; a catch never fires on the same action.
- The shipped game has **40 bricks and three balls**. Missing the bottom consumes a ball; destroyed bricks remain destroyed. More than the configured maximum wall bounces without a brick also consumes a ball. Clear all bricks to win; exhaust balls to lose. The title bar reports status/counts; progress bars and terminal markers provide scene feedback.
- **R** or panel **Restart** restores all bricks, balls and initial pose, taking priority over pending actions. Aim previews include live bricks and exclude destroyed ones; uncertain hits show a short direction indicator.
- Unfocused or minimized windows pause the game without catching up missed time on return. Release and press held actions again after returning.
- The play area fits the window with centered letterboxing. Mouse aim is measured from the ball center; targets in the letterbox margins are ignored.

## Shmup-breakout mode

The optional F1 / Select/Share inspector provides a mode selector for **Breakout** and **Shmup**, restart and pause/resume buttons, plus a widening-only play-area control. Mode changes start a fresh isolated run and preserve manual pause; the original Breakout catch/relaunch rules are unchanged. `--shmup` starts directly in the hybrid mode, including editor-free builds.

In Shmup, move the circular avatar with the **left stick**, aim with the **right stick**, and hold **right trigger** to fire a stream of small balls. Balls bounce off side walls and, by default, bricks; brick bouncing is configurable. Crossing the top or bottom screen edge despawns balls. Twenty consecutive side-wall contacts without a brick interaction expire a ball; brick interactions reset that configurable counter.

Keyboard/mouse fallback: **WASD/arrows** move, **mouse movement** aims, and hold **Space/left click** to fire; **R** restarts a running session, or use panel **Restart** while paused. **Start** pauses/resumes. Neutral aim retains the last direction. After focus loss, input reset or controller replacement, release fire before pressing again.

Mixed square, **2×1**, **2×2**, **L** and **T** bricks descend with gaps. Each compound shape is one brick with shared HP; its cutouts remain open to balls, the avatar and aim previews.

Spawn frequency and descent speed increase over gameplay time to configured caps. Bricks have 100 HP and balls deal 10 damage by default. The player has 100 HP; escaped bricks cost 10 HP each, while avatar contact causes damage followed by brief invulnerability. At zero HP the mode stops until explicitly restarted. The top/bottom screen edges have no rendered border.

Shmup aim previews show the current walls and live bricks, not future moving-brick positions. Piercing mode does not preview brick bounces. Open exits show a line to the screen edge; uncertain hits show a short direction indicator.

Shmup draws balls and compound bricks with **Shared meshes** by default. In **F1 / Select / Share → Gameplay → Configuration... → RENDERING**, choose **CPU-built triangles** for the explicit alternate path, or switch back live—even paused, without restarting or saving. Save changed persists the choice; Reload/Discard restore source choices. The shapes, colors, trails and controls are the same. WINDOW retains the independent VSync and frame-cap controls. See [configuration](../design/configuration.md#shmup-rendering-path) for startup-only mesh resource budgets.

Ball trails fade and taper behind live balls. Pause freezes their aging; restart and mode changes clear them. Shmup has firing, brick-hit, brick-destruction and player-damage sounds, with a loss cue at game over. Mute, focus loss and restart stop stale feedback.
