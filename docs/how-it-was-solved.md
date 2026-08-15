# How I got the back paddles to type

My Beitong gamepad has two extra buttons on the back. Its own software lets me assign them
— but only to another gamepad button. I wanted one of them to take a Steam screenshot,
which means pressing F12, a keyboard key. The software simply does not offer that.

Here is how I got there, mistakes included.

> Two versions below. This first one is the plain-language story. If you want the actual
> packets, byte offsets and API calls, skip to [the technical version](#the-technical-version).

## The two obvious ideas, and why they failed

**Steam Input.** Steam can rebind almost anything, so I expected this to take five minutes.
It worked, but the pad got worse: the left stick started drifting on its own and the pad's
reporting rate dropped by half. Not a fair trade for a screenshot button.

**The pad's own app.** It can put a paddle onto an existing gamepad button, say the left
stick click. But then the game sees that button too. Every screenshot would also crouch my
character. Useless.

## Why the pad simply cannot type

When a USB device is plugged in, it tells Windows what kind of device it is. My pad
announces exactly two things: a gamepad with ten buttons, and a private channel the
manufacturer uses for its own app.

It never announces "I am also a keyboard".

That single fact explains everything. A device can only send keystrokes if it declares
itself a keyboard, and this one does not. No setting can change that. It is why the app
does not offer keyboard mapping — not laziness, the hardware just cannot do it. Pads that
advertise keyboard mapping announce an extra keyboard identity for exactly this reason.

So if the pad cannot type, something on the PC has to type for it. That is the whole idea
behind PadKey.

There was a second surprise: the back paddles are not part of the ten buttons either. As
far as any game is concerned, those paddles do not exist. Whatever reports them, it is
that private manufacturer channel.

## Finding the paddles in the data

The private channel sends 64 bytes at a time, and I had no idea what any of them meant.

Rather than guess, I wrote a small measuring tool. It watches every byte while the pad
sits untouched and records what values are normal. Then it watches again while I hold a
paddle down, and reports any byte showing a value that never appears when idle.

That approach ignores timing completely, which mattered: the channel chatters constantly,
so simply asking "what changed just now?" produced pure noise.

One press was enough:

```
--- RIGHT BACK BUTTON ---
  byte 10: idle [00 08 0A] -> pressed [09]
```

Byte number 10 was the answer. `08` means nothing pressed, `09` means right paddle, `0A`
means left paddle.

## The catch: the channel goes quiet

Then I hit the real problem. That channel only sends data while the manufacturer's app is
running. Close the app and it goes completely silent. Not slow — nothing at all, even
though my program had the device open and was listening.

I tried the obvious things. Listening harder does not help; the device is not talking.
Opening the device does not help either. Asking it politely for its status does not help,
because it does not answer questions.

So the app must be *sending* something that starts the flow.

## Reading the manufacturer's app

Here I got lucky. Their app is built with the same technology as many desktop apps, which
means its logic is stored as readable JavaScript rather than compiled code. I unpacked it
and found a file named after my exact pad model.

The commands were written out in plain sight, with comments. It even described the layout
of the data — and that description matched my measurement exactly. Byte 10, the paddles.
Nice to have the guess confirmed by the source.

Then it stopped being lucky. I sent the command from their own code and got no reply.
None. I tried every read-only command in that family, alone and in combination. Silence.

## Watching the actual cable

The last resort was recording the real USB traffic while their app was running, so I could
see exactly what goes down the wire instead of what the source code implies. I limited the
recording to the pad alone, so no keyboard typing was captured.

One recording answered everything, with two surprises.

**The JavaScript commands are never actually sent.** Not one of them appears in the
recording. Those packets were all mine. The real conversation comes from a separate
compiled part of the app, and it works differently than I assumed: the app does not keep
asking for the paddle state, it asks **once** to be subscribed, and then the pad reports
on its own.

The real exchange is two packets:

1. A hello.
2. A "start sending me state" request.

After that the pad streams continuously with nothing further from the PC.

**The filler bytes were wrong.** Their packets pad the unused space with `08`, not zeroes.
I had been sending the right command with the wrong filler, which the pad ignored.

## Two traps that cost me hours

Neither had anything to do with this pad specifically.

**Windows was queueing my messages behind each other.** My program had one connection to
the device and used it for both listening and sending. Windows handles one operation at a
time on a connection, and my listener was permanently waiting for data — so every message
I tried to send got stuck behind it forever. The symptom was bizarre: exactly one message
would go through, then nothing. The fix was to open a second, separate connection just for
sending.

**I was speaking down the wrong wire.** USB devices have several channels. I was sending
on the one used for setup and configuration; the pad accepts messages there and ignores
them. Their app uses the fast channel meant for live data. Once I switched, it worked.

At that point the paddles worked with the manufacturer's app closed. Which was the goal.

## The bugs that only showed up in real use

Working on my desk is not the same as working through a five-hour gaming session.

**One press, two screenshots.** My own fault. I was re-sending the wake-up message every
three seconds just to be safe. The pad replies to those, and the replies travel on the same
channel as the paddle data — but they are a different kind of message, where byte 10 means
something else. If one arrived while I was holding a paddle, it looked like I had let go,
and the next real message looked like a fresh press.

My first fix made things much worse: I told the program to only trust one specific kind of
message, and it turned out the paddle data arrives under several. Most messages got thrown
away and the key started firing nonstop.

The correct fix was smaller and more obvious in hindsight: **only send the wake-up when the
pad has gone quiet.** While data is flowing there is nothing to fix, so the program stays
silent and no confusing replies are ever produced.

**Keys pressing themselves.** The pad appears under two different identities: a normal one,
and a second one while charging where it is not a gamepad at all. I had loosened the rules
to accept both. In charging mode byte 10 holds something completely different, and some of
those values happened to match what my rules were looking for. Locking the rule back to the
normal identity fixed it, and the settings file now records *why*, so I do not undo it
later by accident.

**Occasional random triggers.** Adding millisecond timestamps to the log settled this in
one session. Every false trigger lasted a single frame of data. Every real press lasted at
least 48 milliseconds:

```
FIRED  byte=0xA5   held 0 ms      <- not even a paddle value
FIRED  byte=0x0A   held 94 ms     <- a real press
```

So now a signal has to be present in two frames in a row before anything happens. This
assumes nothing about the protocol — only that a human finger is slower than a glitch.
Five hours of play afterwards: 15 triggers, all correct, none stray.

**Sometimes dead after startup.** This one I caused while optimising. I had stopped
subscribing to devices no rule cares about, to save CPU. But "tell me when a device is
plugged in" comes with that same subscription — so if the pad was not connected when the
program started, it never found out when it arrived. Now it keeps a notification-only
subscription, which costs nothing, and retries a failed connection every few seconds.

## What it costs to run

| | |
|---|---|
| CPU when idle | 0.39% of one core |
| Memory | 24 MB |
| Program size | 111 KB, no installer |
| Messages sent to the pad | about one every three seconds |
| Delay from press to keystroke | roughly 60 ms |

That delay is not my program being slow — the pad reports its state about every 42
milliseconds, so that is simply how fresh the information can be. For a screenshot key it
is unnoticeable.

I also measured whether PadKey slows the pad down, since that was the reason I abandoned
Steam Input. Reading rates with and without it running overlap completely, so there is no
measurable effect. PadKey never sits between the pad and the game; it listens on that
separate manufacturer channel, which is why it cannot cause the drift Steam Input did.

## What I did not solve

PadKey has to stay running in the background. That is not a shortcoming of the approach —
it follows directly from the pad not being a keyboard. Something has to type, and it cannot
be the pad.

Nothing is ever written to the pad's memory. The wake-up sequence is a copy of the
manufacturer app's own greeting, recorded byte for byte rather than guessed, and the
paddles stay unassigned in their software so nothing leaks into the game.

---

# The technical version

Same story with the packets, byte offsets and API calls left in.

## What I tried first

**Steam Input.** It can bind almost anything to almost anything, so this looked like a
five-minute job. But with Steam Input active the left stick started drifting on its own,
and the pad's polling rate dropped to 500 Hz. A screenshot key is not worth losing half
your polling rate over.

**The vendor app's own mapping.** It maps a paddle onto an existing gamepad button. That
does not help: the game then receives that button too. Mapping the paddle to LS means
every screenshot is also a crouch.

## Why the pad cannot simply type

The first useful thing I learned came from enumerating the pad's HID collections:

```
usagePage=0x01 usage=0x05   gamepad, 10 buttons
usagePage=0xFF usage=0x03   vendor pipe, 64-byte reports
```

That is the whole list. A USB device can only send keystrokes if it declares a **keyboard
collection** (`usagePage 0x01, usage 0x06`) in its descriptor. This pad does not. No
firmware setting can change that — the pad is physically incapable of typing, which is
exactly why its software does not offer the option. Pads advertising "keyboard mapping"
enumerate an extra HID keyboard interface for this purpose.

So something on the PC has to read the paddle and press the key. That is what PadKey does.

Worse: the paddles do not appear on the gamepad collection at all. Ten buttons, and none
of them is a paddle. Whatever carries them, it is the vendor pipe.

## Finding the paddle bits

Guessing was not going to work, so I wrote a measurement instead of a theory. The tool
records every byte's value while the pad sits still, then records it again while a paddle
is held down, and reports which bytes have a value during the press that never appears at
rest. That ignores timing entirely, so it is immune to the constant telemetry chatter that
made the naive "what changed just now?" approach useless.

```
--- RIGHT BACK BUTTON ---
  20BC:5127 MI_01|FF:03   byte 10: idle [00 08 0A] -> pressed [09]
```

Byte 10 of the vendor report:

| Value | Meaning |
|---|---|
| `0x08` | idle |
| `0x09` | right paddle |
| `0x0A` | left paddle |

## The catch: the pipe is silent

The vendor pipe only produced data while the manufacturer's app was running. Close the
app and it went completely quiet — not slow, not intermittent: zero bytes, even with the
device handle open.

Things that did not work:

- Listening with Raw Input. The device does not broadcast on its own.
- Opening it with `CreateFile`. It opens; opening alone starts nothing.
- `HidD_GetFeature` / `HidD_GetInputReport`. The device declares no feature report and
  answers no report id.

So something the app *sends* starts the flow.

## Reading the vendor app instead of guessing

The app turned out to be an Electron application, which means its logic is JavaScript.
Unpacking `resources/app/00000000.asar` produced a per-model file — `kp40d.js`, matching
the pad's internal name `BTP-KP40D`. The protocol was written out plainly:

```js
function getKeyEvent() {
    let data = chartoHex(CONFIG_REPORT_ID);      // 0x02
    data += chartoHex(0x5 | (0x2 << 4));         // 0x25
    sendPack(0, "", data);
}
```

The same file described the report layout, and it matched the measurement exactly:

```
LX LY RX RY LT RT                            6 bytes
byte 6:  Up Down Left Right Start Back LS RS
byte 7:  LB RB Home -- A B X Y
byte 8:  M1 M2 M3 M4 M5 M6 AI Turbo          <- the paddles
```

The payload starts at byte 2 of the report, so `2 + 8 = 10`. The byte I had found by
measuring was confirmed by the vendor's own source.

Then it stopped working. Sending `02 25` got no reply at all. Repeatedly. With and without
the tool's `Ping` keepalive, with every read-only command in that family.

## Watching the wire

USBPcap, filtered to the pad's device address only so no keyboard traffic was recorded,
answered it in one capture. Two surprises:

**The JavaScript protocol is not what the app actually sends.** Not one `02 25` packet
appears in the capture — those were all mine. The real conversation is generated by the
native layer (`BetopJoyCommon.dll`), and it is a subscription, not a poll:

| Direction | Packet | Meaning |
|---|---|---|
| host → pad | `02 CD` + `08` padding | hello |
| pad → host | `02 CD 09 0A 08 09 ...` | reply |
| host → pad | `02 A9 08 A8` + `08` padding | start the state stream |
| pad → host | `02 6D ...` | state, continuously |

**The padding is `0x08`, not zeroes.** I had been sending well-formed commands with the
wrong filler.

## Two Windows traps

Both cost hours and neither is specific to this device.

**One handle serialises I/O.** Windows serialises operations on a synchronous file handle.
My read thread sat blocked in `ReadFile` holding the handle, so every write queued behind
it forever. The symptom was maddening: exactly one write succeeded, then silence. The fix
is a second handle opened purely for writing.

**Control pipe versus interrupt endpoint.** `HidD_SetOutputReport` sends over the control
pipe. The pad accepts those and ignores them. The vendor tool writes to the interrupt OUT
endpoint (`ep 0x04`), which is what `WriteFile` uses.

After both: the pad streams with its own software closed.

## The bugs that only appear in real use

Working on the bench is not the same as working for five hours.

**Firing twice on one press.** My own keepalive caused it. I was resending the wake-up
every 3 seconds, and the pad answers those on the same pipe — but a `02 CD` reply is not a
state report, and byte 10 means something else in it. Holding a paddle while one arrived
looked like a release, and the next real report looked like a fresh press.

My first fix was to pin the rule to one report type. That was worse: the state stream uses
several header bytes (`0x6D`, `0x69`, `0xAE`), so most reports got discarded and the key
fired continuously. The right fix was smaller — **only send the wake-up while the pad is
silent.** Once the stream runs, PadKey stays off the wire entirely, so there are no replies
to confuse it. USB writes in steady state: about one every three seconds, then none.

**Firing on its own.** The pad enumerates under two vendor ids: `20BC` in normal mode, and
`20DD` while charging, where it exposes no gamepad interface at all. I had loosened the
rules to match both. In `20DD` mode byte 10 carries something else, and values like `0x02`
and `0xA9` happened to satisfy the masks. Pinning the vid back fixed it — and the profile
now records *why* it is pinned.

**Occasional stray triggers.** Millisecond logging settled this one. Every false trigger
lasted a single frame; every real press lasted 48 ms or more:

```
FIRED  byte=0xA5   held 0 ms      <- not a paddle value, one frame
FIRED  byte=0x0A   held 94 ms     <- real
```

So a rule now has to see its signal in two consecutive frames before firing. This assumes
nothing about the protocol — only about how long a finger takes. Five hours of play
afterwards: 15 triggers, every one with a correct paddle byte, no strays.

**Sometimes not detected at startup.** This one was self-inflicted. To cut CPU I had
stopped subscribing to devices no rule reads. But device-arrival notifications come with
that subscription, so when the pad was not plugged in at startup nothing was registered and
its later arrival was never noticed. Now the gamepad usages keep a notify-only subscription
— no input traffic, no CPU cost — and a failed open is retried every three seconds.

## What it costs

| | |
|---|---|
| CPU, idle, 60 s sample | 0.39% of one core |
| Private memory | 24 MB |
| Executable | 111 KB, no installer |
| USB writes | ~0.5/s (keepalive only when silent) |
| USB reads | 20/s, against the pad's own 737/s gamepad traffic |
| Detection latency | ~60 ms average, set by the pad's 42 ms stream cadence |

Polling rate, measured with and without PadKey running: 591 and 739 reports/s with it,
814 and 646 without. The spread within each condition is larger than the difference
between them — no measurable effect. PadKey never enters the input chain; it reads a
separate pipe, which is why it cannot cause the drift Steam Input did.

## What is left

PadKey has to keep running. That is not a shortcoming of the approach — it follows from
the descriptor. The pad cannot type, so something must type for it.

Nothing is ever written to the pad's memory. The wake-up sequence is a copy of the vendor
tool's own connect handshake, captured byte for byte rather than guessed, and the paddles
stay unassigned in the vendor software so nothing leaks into the game.
