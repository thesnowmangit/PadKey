# PadKey

A tiny Windows background app that turns a gamepad's **back paddles into real keyboard
keys**. Built for the Beitong / Betop KP40D (VID `20BC`, PID `5127`), whose paddles games
cannot see at all — but the trigger mechanism is generic and works with other HID gamepads.

Defaults: **left paddle → F12** (Steam screenshot), **right paddle → F5**.

It never touches Steam Input and never sits in the input chain, so it cannot cause stick
drift or lower the pad's polling rate. Nothing is ever written to the pad's memory.

*[Türkçe README](README.tr.md)*

---

## Why this is needed

The pad reports exactly two HID collections to Windows:

| Collection | What | Paddles |
|---|---|---|
| `01:05` (`IG_01`) | Gamepad, 15-byte report, 10 buttons | no |
| `FF:03` (`MI_01`) | Vendor-defined, 64-byte report | **yes** |

There is **no keyboard collection** (`01:06`), so the pad physically cannot type, no matter
what its own software is told. And the paddles do not appear on the gamepad collection at
all — they exist only on the vendor pipe. So something on the PC has to read that pipe and
press the key. That is PadKey.

Leave the paddles **unassigned** in the vendor tool. If you also map them to a gamepad
button there, that button reaches the game as well.

## The protocol

The vendor pipe stays silent until it is greeted. The sequence was recovered by capturing
the vendor tool's USB traffic (USBPcap, filtered to the pad's device address only). The
tool does not poll — it subscribes:

| Direction | Packet | Meaning |
|---|---|---|
| host → pad | `02 CD` + `08` padding | hello / connect |
| pad → host | `02 CD 09 0A 08 09 ...` | reply |
| host → pad | `02 A9 08 A8` + `08` padding | **start the state stream** |
| pad → host | `02 6D ...` | state reports, ~24 Hz |

Byte **10** of a state report carries the paddles:

| Value | Meaning |
|---|---|
| `0x08` | idle |
| `0x09` | right paddle (bit `0x01`) |
| `0x0A` | left paddle (bit `0x02`) |

Two details matter and both cost hours if you get them wrong:

- The padding is **`0x08`, not zero**. That is what the vendor tool sends; confirmed on the wire.
- Commands must go out the **interrupt OUT endpoint** (`ep 0x04`). `HidD_SetOutputReport`
  uses the control pipe instead; the pad accepts those and ignores them.

PadKey sends this sequence itself, so the vendor tool does not need to be running.

### The pad has two connection modes

| VID | Mode | Notes |
|---|---|---|
| `20BC` | Normal. Enumerates as an Xbox 360 controller plus the vendor pipe. | Byte 10 layout as above. |
| `20DD` | Charging / alternate. A single vendor interface, **no gamepad interface at all**. | Byte 10 means something else — values like `0x02` and `0xA9` show up there. |

Rules therefore pin `vid = 0x20BC` on purpose. Matching both modes makes the keys fire on
their own. The pad is not usable as a gamepad in `20DD` mode anyway.

### Traps worth knowing about

- **One handle, serialised I/O.** Windows serialises I/O on a synchronous file handle. With
  a single handle the poll write blocks forever behind the pending `ReadFile` — exactly one
  request gets through and then nothing. PadKey opens separate read and write handles.
- **Keepalives pollute the stream.** The pad answers requests on the same pipe the state
  stream uses, and those answers are not state reports. Re-sending the wake-up every few
  seconds made a held button look briefly released, which fired a second time. PadKey now
  sends the wake-up **only while the pad is silent**, so in steady state it writes nothing.
- **Single-frame blips.** Other report types occasionally satisfy a rule by coincidence.
  Measured: every false trigger lasted a single frame; every real press lasted 48 ms or
  more. A rule therefore has to read active across two consecutive frames (`arm_ms`) before
  it fires.

## Using it

1. Run `PadKey.exe`. It puts an icon by the clock; double-click it for settings.
2. To change a key, click the **KEYBOARD KEY** box and press the key you want.
   Ctrl/Shift/Alt combinations work. Esc cancels.
3. To bind a different gamepad button, click **Learn gamepad button**, take your hands off
   the pad for two seconds, then press and release the button. If it picks the wrong
   signal, use **Try another trigger** and watch the lamp.

Changes apply immediately and save themselves; *Save and close* is just a shortcut. No keys
are sent while a capture is in progress.

**Modes** — *Tap* presses and releases once (use this for screenshots). *Hold* keeps the key
down for as long as you hold the pad button.

**Profiles** — rules live in `profiles\<name>.ini`; `padkey.ini` only records which profile
is active. Both are under `%APPDATA%\PadKey`, so the exe can sit anywhere on its own.

**Autostart** — the *Start with Windows* checkbox. Launched by Windows it goes straight to
the tray; launched by hand it opens the settings window. Running it again while it is
already running just brings that window up.

## Building

```
build.cmd
```

No .NET SDK required — it compiles with the `csc.exe` that ships with Windows
(.NET Framework 4.x). Output is a single ~110 KB exe with no dependencies.

`tools\make-icon.ps1` regenerates the icon.

## Diagnostics

| Command | What it does |
|---|---|
| `padkey.exe list` | Every HID device, its usages, button ranges and report sizes |
| `padkey.exe learn [VID]` | Live report stream; shows which byte/bit changes |
| `padkey.exe hold` | **Definitive button finder.** Compares the set of values a byte takes at rest against the set while a button is held, so jittering telemetry bytes drop out |
| `padkey.exe probe [s]` | Read-only feature/input report queries against the vendor pipe |
| `padkey.exe poke <hex> [s]` | Sends one status-family request (`cmd` nibble `0x5` only) and prints the replies |
| `padkey.exe session [s]` | Ping + key-event request, prints the replies |
| `padkey.exe keytest` | Sends the profile's keys through `SendInput` while watching a low-level keyboard hook — proves injection reaches the layer Steam listens on |

Everything is also written to `%APPDATA%\PadKey\padkey-log.txt`, which appends across runs
and is capped at 512 KB. In normal operation the log records each trigger with millisecond
timestamps, the raw byte value and how long the button was held — enough to tell a real
double tap from a spurious repeat.

## Measured cost

| | |
|---|---|
| CPU at rest | ~0.2–0.4 % of one core |
| Private memory | ~24 MB |
| USB writes | ~0 in steady state (wake-up only when the pad goes silent) |
| USB reads | ~20 reports/s on the vendor pipe, against ~740/s the pad already sends on its gamepad endpoint |
| Detection latency | ~60 ms average — set by the pad's ~42 ms stream cadence plus the two-frame arming, not by PadKey |

## Limits

- If a game runs **as administrator**, PadKey has to as well, or Windows (UIPI) blocks the
  injected key from reaching that window.
- Steam only takes a screenshot with F12 **inside a game with the overlay enabled**.
  Pressing F12 on the desktop does nothing, even on a real keyboard.
- Games with aggressive anti-cheat may ignore injected keyboard input; Steam's own hook
  generally is not affected.

## License

MIT — see [LICENSE](LICENSE).
