# Byte

A quiet, local-only terminal robot that lives on the Windows desktop. Original
procedural vector art, mint phosphor eyes, tiny boots, and a floating antenna.
No account, cloud, LLM, telemetry, ads, or downloaded art.

## Meet the friends

There are now three original vector companions: **Byte**, the mint terminal
robot; **Pip**, a peach-colored terminal cat with ears, whiskers and a curling
tail; and **Orbit**, a lavender space explorer with an orbiting satellite and
little flying-saucer rim.

Use the tray's **Character** submenu to choose your solo character, or click a
character and choose **Switch character** to cycle through them. **Friends
together (all three)** toggles between one selected character and all three on
the desktop at once. The same toggle is in each interaction bubble and
Preferences. Choices persist locally; existing installations remain in solo
mode until you enable friends.

In together mode, friends roam independently and occasionally approach one
another, wave, high-five, or build code blocks while a friend cheers them on.
**Automatic friend interactions** can be disabled without hiding the friends.
These scenes are silent, bounded to one monitor, and cancel immediately for
busy pointer activity, dragging, focus, pausing, reduced motion, Quiet mode, or
fullscreen hiding. They never open speech bubbles automatically. Each friend
has its own clickable, draggable, click-through overlay.

Hide/show and summon affect the whole group, with room between friends when
gathering. Focus applies to everyone; friends added midway inherit the remaining
time. Turning group mode off keeps your selected character.

## Personalities that remember

No LLM is involved: a small, local decision model gives each friend preferences
and remembers only its own interactions. **Byte** gravitates toward cables and
building; **Pip** likes bugs and warming up by a CPU; **Orbit** prefers signs
and code blocks. Recent celebrations, jokes, focus sessions, naps and gentle
versus energetic tosses influence subsequent choices. Gentle landings get a
character-specific reaction; energetic tossing earns a quiet recovery.

Idle decisions exclude the character's last three autonomous activities
(Quiet/reduced-motion mode alternates still poses instead). Requested jokes
use a separate eight-joke deck per character, avoiding the previous seven
even across restarts. A direct request still works regardless of that
character's preferences.

Nearby friends may silently acknowledge a celebration, notice a stumble, or
settle down when a friend naps. Reactions are rate-limited, cannot trigger
chains of reactions, and do not appear across different monitors. Shared
encounters begin with getting acquainted; completed encounters grow into
friendships and unlock high-fives, collaborative builds and quiet shared
rests. Interrupted encounters earn no friendship, consecutive shared scenes
do not repeat, and there is no penalty for being away.

Click a character and choose **About this friend** to see its personality,
most recent interaction and friendship levels. This is always on demand,
never an unsolicited notification. **Preferences → Forget character memories**
resets every character's recent activity/joke memory and friendships after
confirmation, without changing settings or the running focus timer.

## Run

Windows 11 x64. Extract **Byte-win-x64.zip** and run **Byte.exe**, or run
`dist\Byte\Byte.exe` directly. The self-contained portable build includes its
.NET runtime; neither an SDK nor an installer is required on the destination PC.
Keep the executable in a stable location before enabling launch at login.
This is an unsigned local build, so Windows may show a reputation warning.

- Click Byte for its small menu; double-click to celebrate.
- Drag the character and release to gently toss it. It lands above the taskbar.
  A release watchdog also checks the physical mouse-button state, so a missed
  Windows mouse-up message cannot leave a character attached to the pointer.
- **Ctrl+Alt+B** summons it to the monitor containing the pointer.
- **Ctrl+Alt+H** immediately hides/shows it for presentations.
- Right-click the mint robot tray icon for Quiet / Balanced / Playful, pause,
  hide, mute, focus, Preferences, and quit. Double-click the tray icon to summon.
- Preferences offers size, reduced motion, opt-in login launch, custom hotkeys,
  pointer play, fullscreen hiding, and a 1–180 minute focus timer.

Byte starts silent. Unmuting only enables a user-triggered celebration sound;
idle behavior and focus completion remain silent. It never initiates a chat or
tells an unsolicited joke. Focus puts on headphones, suppresses roaming, and
ends with a stretch. Pausing freezes roaming, not the focus clock.

Transparent pixels pass through to other applications. Only the character and
an open bubble accept mouse input. Bubbles do not take keyboard focus, expire
automatically, and dismiss when you click elsewhere without swallowing that
click. Preferences is a conventional keyboard-accessible window, opened only
by an explicit user action. Fullscreen foreground applications automatically
hide Byte by default; the quick-hide shortcut is the reliable manual override.

Balanced is intentionally restrained. Pointer motion backs off playful activity
immediately; no typing is observed, so use Focus or Quiet for sustained work.
Occasional bounded pointer inspection/chasing, cable untangling, bug chasing,
CPU warming, falling code blocks, napping, corner sitting, and little trips are
local state-machine behaviors, not AI prompts.

## Monitor behavior

Positions and gravity use **virtual-desktop physical pixels**, not coordinates
relative to the primary monitor. Each display has its actual bounds, work area,
and DPI scale. Byte approaches an edge before winding up, leaping, and landing
on another display. Large gaps or vertical offsets use a fade/portal rather
than pretending an invisible desktop exists. During a leap, it intentionally
crosses the gap; ordinary roaming and toss landings are clamped to a real work
area. Very small work areas can necessarily clip a character larger than them.

Display/work-area changes are debounced. Removed destinations cancel travel
and recover to the nearest available display. DPI changes invalidate physical
placement; a viewbox fits the vector character to the physical sprite rectangle.
Session locking and sleep hide the overlay; unlock/resume refresh the layout.
Windows can temporarily deny pointer access during a secure-desktop switch,
even before a lock notification arrives. Byte treats that as unavailable input:
it cancels dragging, hides the friends, and waits instead of exiting. Locked
and suspended ticks do not query desktop input. Monitor refresh waits until
input returns; focus clocks, pause state, and a manually hidden preference are
preserved. A denied pointer read is logged once per unavailable interval.
The overlay is never an all-desktop transparent window.

## Privacy and local files

The application has **no networking code** and does not capture the screen,
read files from your projects, inspect typing, install keyboard hooks, access
audio input, or request elevated permissions. It reads monitor geometry, system
animation preferences, the pointer position and left mouse-button state, and
the foreground window's bounds/class solely for fullscreen hiding. Two hotkeys
are registered through Windows `RegisterHotKey`; there is no key-event stream.
Future app-aware integrations would need separate opt-in permission UI.

Settings: `%LOCALAPPDATA%\Byte\settings.json`.
Character memory: `%LOCALAPPDATA%\Byte\personality.json` (three bounded
profiles, the most recent interaction type, three recent activities, seven
joke indices, and three pairwise friendship counters/last-scene types).
No work content, interaction timestamps, pointer positions, event logs,
personal identifiers or text entered by the user is stored in that file.
Memories save only when changed, at most once every 20 seconds and on a normal
quit. Forced termination can lose up to 20 seconds of recent play.
An unreadable memory file is reported and preserved; fresh in-session
memories are not saved over it until you explicitly forget/reset it.
Errors: `%LOCALAPPDATA%\Byte\errors.log` (bounded, one rotated backup).
Invalid settings are reported, not silently overwritten at startup. Changes
are written atomically. Login launch is opt-in through the current-user
`Software\Microsoft\Windows\CurrentVersion\Run\ByteDesktopCompanion` entry.
To uninstall, disable login launch, quit from the tray, delete the portable
folder, and optionally delete `%LOCALAPPDATA%\Byte`.

## Stack and source

**C# / .NET 8 WPF + small Win32 interop**, targeting Windows 11 first:
native per-pixel layered windows and `WS_EX_NOACTIVATE`, per-monitor V2 DPI,
`EnumDisplayMonitors`, physical `SetWindowPos`, global hotkeys, native display /
power / session notifications, and a WinForms tray icon. WPF draws only the small
vector sprite; the timer adapts from roughly 60 Hz during movement to 10 Hz idle
and 4 Hz while hidden, with no continuous background render loop. This avoids
shipping a web runtime and lets the idle character use a small native overlay.

- `Byte.Core`: platform-independent geometry, settings, physics, behavior and
  bounded pairwise social choreography (`SocialBehavior.cs`); identity memory,
  personality preferences and nonrepetition live in `PersonalityMemory.cs`.
- `Byte.App\Character.cs`: original artwork and pose animation only.
- `Byte.App\Native.cs`, `Overlay.cs`: native integration and pointer interaction.
- `Byte.App\CompanionHost.cs`: app lifecycle, tray, timing, and orchestration.
- `Byte.Tests`: dependency-free executable regression suite.

## Build and verify

Install the .NET 8 SDK, then in PowerShell:

```powershell
.\build.ps1
```

This runs the core checks, publishes a self-contained Windows x64 executable,
runs native smoke checks, and creates `dist\Byte-win-x64.zip`. For a build
machine without an interactive desktop, use `.\build.ps1 -SkipNativeSmoke`.
That skips native verification, not core tests.

**Native smoke tests temporarily move the pointer and inject clicks** into
Byte and a dedicated test window in a separate process; don't use the pointer
during the approximately 30-second run. The pointer is restored and the test
window is closed afterward. The test does not capture the desktop. Its
`artifacts\Byte-character.png` contact sheet renders Byte's own vector artwork
directly into an offscreen bitmap.

Reports live in `artifacts\core-tests.txt`, `artifacts\native-smoke.json`,
and `artifacts\package-sha256.txt`. `artifacts\Byte-friends.png` is an offscreen
render of all three characters. Native checks cover cross-process click
pass-through, real character clicks / double-clicks / dragging / tossing,
bubble hit testing and outside dismissal, no activation, quick-hide/summon
message routing, lost-mouse-up recovery, solo/group switching, every friend's
interaction menu, group focus/hide/summon, actual monitor placement and a monitor leap when two displays
are attached. Synthetic layouts cover negative coordinates, mixed DPI, gaps,
vertical stacking, disconnected destinations, focus timing, and safe tossing.
Session regression checks inject pointer unavailability before/after simulated
native lock/unlock and suspend/resume notifications, repeat recovery, and test
a real captured drag whose release occurs with pointer access unavailable.
These checks do not actually lock the user's workstation.
Personality regressions cover measurable preferences, memory/history bounds,
joke-deck persistence, friendship progression and scene cancellation, quiet
contextual reactions, on-demand status, atomic local storage, and explicit
reset/corrupt-file recovery. Native smoke uses isolated memory and temporary
storage; it never changes your characters' real saved memories.

**Hardware validation still required:** physically plug/unplug monitors during
travel; change scaling while dragging; sleep/resume; lock/unlock; exercise
fullscreen games and presentation software. Synthetic tests and injected
native messages do not prove those physical transitions. This MVP supports
Windows x64 only, not macOS/Linux, and does not include signing or an installer.
