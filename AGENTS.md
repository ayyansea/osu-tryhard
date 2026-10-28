# AGENTS.md

Guide for agents working in this repo. High level first, then deep dives into the systems that were added on top of upstream osu!(lazer). You don't need to read all of it: use the map to find the part you need.

## 1. What this is

`osu!tryhard editor`: a fork of ppy/osu (lazer) stripped down to **only the beatmap editor**, osu!standard only, with extra editor features.

- Upstream history is kept (77k commits); everything fork-specific sits on top of upstream commit `1a84c00929`. `git log 54de37ddb2^..` is the fork's history (strip-down, ruleset removal, theme, rhythm blueprint, hitsounds, lazer test, ...).
- No gameplay, no online features, no song select (it has its own simple list screen). The client never talks to osu! servers (`DummyAPIAccess`, permanently logged out).
- It doesn't register itself as osu!: own storage, own IPC name, own executable name, so it runs next to the official client.
- Theme: Catppuccin Mocha (`osu.Game/Overlays/CatppuccinMochaColourProvider.cs`).
- Remote: `origin` = `github.com/ayyansea/osu-tryhard`, branch `master`.

User-facing summary of the fork is in `README.md`; the upstream readme follows it.

## 2. Build, run, test

- Target framework: `net10.0`. SDK pinned by `global.json`.
- Run: `dotnet run --project osu.Desktop`. Release binary: `osu.Desktop/bin/Release/net10.0/osu!tryhard`.
- Build only the game library (fast check, ~1 min): `dotnet build osu.Game/osu.Game.csproj --no-restore -v q 2>&1 | grep -E " error |rror\(s\)"`.
- Unit tests for osu!standard + hitsound logic: `dotnet test osu.Game.Rulesets.Osu.Tests/osu.Game.Rulesets.Osu.Tests.csproj --filter <name>`.
- Game/visual tests (headless, real `Game`/editor): `dotnet test osu.Game.Tests/osu.Game.Tests.csproj --filter "FullyQualifiedName~<name>"`.
- If `dotnet` isn't on PATH, the SDK isn't installed (Arch: `sudo pacman -S dotnet-sdk`). In Claude Code's sandbox, `dotnet build/test` needs the sandbox disabled (it writes to `~/.nuget`, `~/.dotnet`).
- Test builds take minutes. Run them in the background and read the output afterwards instead of blocking a shell on them.
- BASS (audio decoding) exists in visual tests (they run a real host) but **not** in plain NUnit unit tests: a test that decodes audio must be a visual test, or be allowed to be inconclusive.
- The ruleset test projects other than Osu are untouched upstream code. Taiko / catch / mania projects still exist in the repo (the Game tests reference catch and mania) but `osu.Desktop` only references `osu.Game.Rulesets.Osu`.

### Analyzer rules that bite

Upstream's analyzers are errors in the build. Ones met so far:

- `Task.Result` is banned: use `GetResultSafely()` (`osu.Framework.Extensions`).
- `OsuSpriteText.Truncate` throws at runtime ("Use TruncatingSpriteText instead"): use `TruncatingSpriteText`.
- Don't use `Bindable<T>` fields of someone else's bindable on a drawable that gets disposed (see section 6).

## 3. Repo map

| Path | What |
| --- | --- |
| `osu.Desktop/` | Desktop entry (`Program.cs`, `OsuGameDesktop.cs`). Game name `osu-tryhard` (`-development` in debug), friendly name `osu!tryhard editor`. |
| `osu.Game/OsuEditorGame.cs` | The stripped game host. Start here. |
| `osu.Game/Screens/EditorSelect/` | The beatmap/difficulty list screen shown on launch. |
| `osu.Game/Screens/Edit/` | The editor (upstream) plus fork features: `Rhythm/`, `Hitsounds/`, `ComboColours/`, `MapCleaner/`, `LazerTestLauncher.cs`, `Components/EditorToolOverlay.cs` + `EditorOverlayHost.cs`. |
| `osu.Game/Overlays/Volume/GlobalVolumeControl.cs` | Always visible volume sliders. |
| `osu.Game/Input/Bindings/GlobalActionContainer.cs` | Key bindings and the `GlobalAction` enum. |
| `osu.Game/Configuration/OsuConfigManager.cs` | `OsuSetting` enum and defaults (`game.ini`). |
| `osu.Game/Tests/Visual/` | Test base classes living in the main assembly (`EditorTestScene`, `EditorSavingTestScene`). |
| `osu.Game.Tests/` | Game tests. Fork additions: `Visual/Editing/TestSceneHitsoundsScreen.cs`, `TestSceneHitsoundSampleFiles.cs`, `TestSceneRhythmScreen.cs`, `TestSceneVerifyScreen.cs`, `TestSceneComboColourSections.cs`, `TestSceneMapCleaner.cs`, `Database/LazerTestImportTest.cs`, `Editing/RhythmSerializerTest.cs`, `Editing/ComboColourEditingTest.cs`, `Editing/MapCleaningTest.cs`. |
| `osu.Game.Rulesets.Osu(.Tests)/` | osu!standard. Fork additions: slider section angles (`Edit/Blueprints/Sliders/Components/SliderSectionAngleOverlay.cs`), `HitsoundEditingTest`, `HitsoundSampleProcessorTest`, `LazerTestLauncherTest`. |

Outside the repo, handy for reading (not editable from inside this repo's tooling; use plain file reads). They aren't on every machine; without them, the framework's XML docs are in `~/.nuget/packages/ppy.osu.framework/<version>/lib/net10.0/osu.Framework.xml`:

- `~/Github/osu-framework`: osu-framework source (`MouseHandler`, `DirectorySelector`, `Game.cs` key handling, ...).
- `~/Github/osu-local`: a lazer checkout; read it to check what lazer itself does (importer, metadata lookup).

## 4. Architecture overview

### 4.1 Startup flow

`Program.Main` -> `OsuGameDesktop : OsuEditorGame : OsuGameBase`.

`OsuEditorGame` (`osu.Game/OsuEditorGame.cs`):
- `SetHost`: hides the OS cursor, wires window drag & drop (`.osz` / `.olz` import, folders also work via `DirectoryArchiveReader`).
- `LoadComplete`: forces borderless fullscreen, no mouse confine, relative mouse mode off (see 5.6); creates the screen stack, `RealmDetachedBeatmapStore`, `VolumeOverlay`, `GlobalVolumeControl`, OSD, notification overlay (`INotificationOverlay`), dialog overlay, logo; pushes `EditorOnlyLoader -> EditorBeatmapSelect`.
- Handles `GlobalAction` volume keys and toggles window mode on F11 / Alt+Enter (`OnKeyDown`).
- Overrides `GetFrameworkConfigDefaults` (borderless, no confine).
- Removes obsolete stored key bindings (`removeObsoleteKeyBindings`) because bindings are persisted in realm by int; see section 6.

`EditorBeatmapSelect` is the only "menu": list of sets (read directly from realm, the store is just a change trigger), difficulties of the selected set, search, delete, "new beatmap" per ruleset, a refresh button and the dimmed background of the selected set.

### 4.2 Editor structure (upstream, extended)

- `Editor` (a screen) owns `EditorBeatmap` (the model being edited, with `BeginChange/EndChange/Update/Add/Remove`), `EditorClock`, `BindableBeatDivisor`, `EditorChangeHandler` (undo/redo by serialising state), menu bar, bottom bar, and a `screenContainer` with the mode screens.
- Modes: `EditorScreenMode` (`SongSetup`, `Compose`, `Rhythm`, `Hitsounds`, `Design`, `Timing`, `Verify`). Keys: F1 compose, F2 design, F3 timing, F4 setup, F5 rhythm, F7 hitsounds, Ctrl+Shift+A verify, F8 test in lazer, Ctrl+Shift+C clean up difficulty (any screen).
- Tool overlays (`Components/EditorToolOverlay`): a dimmed panel over the whole editor (Escape / click outside closes, editor shortcuts are swallowed while open, volume keys pass through). They live in `EditorOverlayHost`, which `Editor` caches and puts above the menu and bottom bar; a screen that owns one adds it to the host in `LoadComplete`. Screens live as long as the editor, so nothing removes them.
- A mode screen derives `EditorScreen` (or `EditorScreenWithTimeline`, which puts a `TimelineArea` on top and a main content area below). Screens are created lazily in `Editor.onModeChanged`.
- **Undo/redo is per screen**: `EditorScreen.ChangeHandler` (virtual, null = the beatmap's). `Editor.activeChangeHandler` picks `currentScreen.ChangeHandler ?? changeHandler`. Rhythm and Hitsounds supply their own.
- Unsaved changes: `Editor.HasUnsavedChanges` ORs the beatmap's handler with `editorRhythm` and `editorHitsounds`. `Save()` also calls `editorRhythm.Save()` / `editorHitsounds.Save()`; `updateLastSavedHash` marks them saved.
- Adding a mode (done twice, copy the pattern from commit `ddeb71c2e6` / `a7e5614f6e`): `EditorScreenMode` entry, `EditorStrings`, a `GlobalAction` **appended at the end of the enum** (stored by int), default binding, `GlobalActionKeyBindingStrings`, case in `Editor.OnPressed` and `Editor.onModeChanged`.

### 4.3 Timeline

`Compose/Components/Timeline/Timeline.cs` (scrolling/zooming; the centre marker is the current time) hosts "user content" supplied to `TimelineArea(content, timelineHeight = 80, showWaveform = true)`. Both extra parameters were added by the fork (hitsounds screen uses a tall, waveform-less timeline).

`TimelinePart` (`Edit/Components/Timelines/Summary/Parts`) is the base for anything aligned to time: it sets `Content.RelativeChildSize = (trackLength, 1)`, so children use `RelativePositionAxes = X`, `X = time in ms`, pixel sizes.

## 5. Systems deep dives

### 5.1 Hitsounds (`osu.Game/Screens/Edit/Hitsounds/`)

Purpose: an FL-Studio-like pattern grid aligned to the timeline. Hitsounds exist as a **track independent of hit objects**; they are *applied* to objects afterwards.

Files:

| File | Role |
| --- | --- |
| `EditorHitsounds.cs` | The model/service (a `CompositeDrawable` added to `Editor` and cached). Owns `Steps`, selection, clipboard, change handler, storage, apply/import. Also `HitsoundStep` and `HitsoundSerializer`. |
| `HitsoundEditing.cs` | Pure helpers: `HitMoment` (object or slider node + time), `HitsoundLane`, `SetLane`, `IsRepresentable`, `GetMoments`, `SetSamples`. Unit-tested without any UI. |
| `HitsoundLayout.cs` | Which lanes are visible (3 sections per page), geometry constants, lane colours, `TryGetLane(y)`. |
| `HitsoundPatternEditor.cs` | The grid (a `TimelinePart`): painting, select tool, drag handling, audition, lane stripes. |
| `HitsoundLaneLabels.cs` | Lane names left of the timeline: click previews, scroll pages sections, double click selects a lane. |
| `HitsoundSampleSetPanel.cs` | Sample set file management (reuses `FormSampleSetChooser`), file chooser with preview, sound folder setting. |
| `HitsoundSampleProcessor.cs` | The pipeline run on every added custom sound file. |
| `HitsoundsScreen.cs` | The screen: layout, tool buttons, apply/import buttons, key handling, clipboard actions. |

Model:
- `HitsoundStep : HitObject`: just `StartTime` and `Samples` (normal + additions). Steps live in `EditorHitsounds.Steps` (sorted by time), **not** in an `EditorBeatmap`. (A second `EditorBeatmap` as a child breaks many test lookups; see 5.2.)
- `HitsoundLane(Bank, SampleSet, Name)`: a lane = one sample in one bank in one sample set. Set 1 is the default set (suffix `null`), set N>=2 has suffix `"N"`. `Create()` makes `HitSampleInfo(name, bank, suffix, editorAutoBank: false, useBeatmapSamples: true)`.
- **`useBeatmapSamples: true` is required** for custom files to play: `LegacyBeatmapSkin.GetSample` returns null for samples without it, so default sounds play instead.
- Lanes shown = `EditorHitsounds.SampleSets` = default set + sets registered by the UI (sets having files, or created in the chooser) + sets used by steps. Sections are ordered soft, normal, drum per set.

Stable compatibility (important rule): a .osu stores one normal bank, one addition bank and one sample set per hit object / slider node. `HitsoundLane.ConflictsWith` encodes that, and `SetLane(on: true)` **replaces** conflicting samples: same sample name in another bank/set, anything in another sample set, additions in another bank than the other additions. `IsRepresentable` checks the same for existing data (imported steps can be unrepresentable -> red mark above the column, and the apply report counts them).

Selection: by **sample instance** (`HashSet<HitSampleInfo>(ReferenceEqualityComparer)`), because `HitSampleInfo` has value equality and two steps' kicks are equal. Operations (`DeleteSelected`, `MoveSelected`, `CopySelected`, `Paste`, `SelectLane`) work on sounds, not columns. `MoveSelected` is non-destructive while dragging (conflicts are only resolved by `ResolveConflicts()` on mouse up).

Painting: first cell of a stroke decides add or erase; each time is painted once per stroke (`paintedTimes`); snapped via `IBeatSnapProvider`; a stroke is one undo step (`BeginChange/EndChange`). Edit previews go through one throttled `SkinnableSound` (100 ms) because many overlapping plays were far too loud. Audition during playback uses a pool of 24 `SkinnableSound`s inside an `EditorSkinProvidingContainer`.

Volume: every sound has its own `HitSampleInfo.Volume` (1-100, `HitsoundEditing.MIN/MAX_VOLUME`), shown as the cell height. `HitsoundsScreen` has three `FormSliderBar<int>`s (`TransferValueOnCommit`, so one commit = one undo step): *paint volume* (`EditorHitsounds.PaintVolume`, used for new sounds and edit previews), *selected volume* and *all volume* (`SetSelectedVolume` / `SetAllVolume` -> `SetVolume`; they show the average, selected is disabled without a selection). `HitSampleInfo` is immutable, so `SetVolume` **replaces** the instances and re-maps the by-reference selection. A .osu stores one volume per hit object / slider node (the encoder uses the first/max sample's), so `IsRepresentable` also requires one volume per step; mixed steps get the red mark and are counted in the apply report.

Drags are continued **every frame** in `Update` (`continueDrag`) rather than per mouse event, so the box/move/paint follows the cursor while the timeline auto-scrolls near its edges (same constants as compose's `TimelineBlueprintContainer.handleScrollViaDrag`).

Persistence and undo:
- Stored per **beatmap set** in the data dir: `hitsounds/<BeatmapSetInfo.ID>.txt` (legacy fallback: per difficulty `<BeatmapInfo.ID>.txt`). Not part of the .osu files.
- Format: header line, then `time|name,bank,suffix,volume,autobank,usebeatmapsamples|...` per step (`HitsoundSerializer`; the 6th field is optional when reading, defaulting to true).
- Undo: nested `HitsoundChangeHandler : EditorChangeHandler` serialises the steps with the same serializer; `ApplyStateChange` calls `Replace`.
- `Apply to this difficulty`: matches steps to hit objects / slider nodes by rounded ms (+-1) and copies samples; steps without an object are dropped, objects without a step are untouched. `Apply to all difficulties` additionally loads each other difficulty via `BeatmapManager.GetWorkingBeatmap`, applies and `BeatmapManager.Save`s it. Applying to the open difficulty goes through its `EditorBeatmap` (its own undo history, only reachable from the compose screen).

Sample files (the sample set panel):
- Sample sets are managed **only here**: the Setup screen's Resources section no longer has a sample set chooser/file list (it duplicated this panel). Upstream's `FormSampleSet` control is now unused apart from `TestSceneFormSampleSet`. General rule for the fork: when a feature lives in a new screen (rhythm, hitsounds), don't keep a second copy in the upstream screens.
- Files are added to the beatmap *set* with `BeatmapManager.AddFile` under the legacy names `<bank>-hit<name><suffix>.wav` (e.g. `soft-hitwhistle2.wav`), so they apply to every difficulty. Removal uses `DeleteFile`.
- Adding runs `HitsoundSampleProcessor`: decode with BASS (any format), trim leading silence (threshold 0.001, same as `CheckDelayedHitsounds`), normalise the peak to -0.1 dB, pad to 25 ms (ranking criteria), warn if the peak is later than 5 ms, write 16-bit PCM WAV (MP3 isn't allowed for hitsounds). A notification reports what was done; undecodable files are added unchanged. Same-named files with other extensions are deleted first (no unused files).
- The file chooser is `PreviewFileChooserPopover : FormFileSelector.FileChooserPopover`: picking a file previews it (BASS sample store from a `StorageBackedResourceStore`) and a "use this file" button confirms.
- Setting `OsuSetting.HitsoundSamplesDirectory` (`game.ini`): the folder the chooser starts in, chosen with the "sound folder" button (a directory popover).
- After a file operation the skin updates through `RealmBackedResourceStore` realm notifications -> `EditorBeatmapSkin.BeatmapSkinChanged` -> `HitsoundSampleSetPanel.refresh`. This is asynchronous.

Ranking criteria relevant to this system (checked against the wiki): hitsounds must be WAV or Ogg; impact peak within 5 ms; at least 25 ms long; no unused or 0-byte files; audible. The verify checks in `osu.Game/Rulesets/Edit/Checks/` (`CheckDelayedHitsounds`, `CheckTooShortAudioFiles`, `CheckHitsoundsFormat`, ...) are the executable version, and a good place to look for thresholds.

### 5.2 Rhythm blueprint (`Screens/Edit/Rhythm/`)

A non-positional rhythm of clicks/holds to lay out before placing objects. `EditorRhythm` keeps its **own `EditorBeatmap` child** (sharing the real `ControlPointInfo`), so the whole timeline selection/editing machinery works on it. Consequences: test helpers filter `ChildrenOfType<EditorBeatmap>().Where(b => b.Parent is not EditorRhythm)` (there are ~20 such lookups; this is also why hitsounds avoided a second `EditorBeatmap`). Stored per difficulty in `rhythm/<BeatmapInfo.ID>.txt` (`RhythmSerializer`), own `RhythmChangeHandler`, shown as a strip under the compose timeline (setting `EditorTimelineShowRhythmStrip`). `RhythmScreen` re-caches the rhythm beatmap as `EditorBeatmap` for its children.

### 5.3 Test in osu!lazer (`Screens/Edit/LazerTestLauncher.cs`)

F8 / File > "Test in osu!lazer": `Editor.testInLazer` saves, then `LazerTestLauncher.Launch` writes the whole set as `%TMP%/osu-tryhard-lazer-test/<setGuid>.osz` and starts the lazer executable with that path as the only argument; a running lazer picks it up over its IPC and imports it (lazer deletes the file after import).

The hard part is **not creating duplicates in lazer**:
- Lazer replaces a previously imported set only when the incoming set has an online ID > 0 equal to an existing set's (`BeatmapImporter.PreImport`).
- This client's `.osu` files have **no** `BeatmapSetID:` line for unsubmitted sets (`LegacyBeatmapEncoder` only writes it for IDs > 0), so the exporter **inserts** a made-up ID after `[Metadata]` (or replaces an existing line): `GetFakeOnlineID(setGuid)` = 1e9 + hash, stable per set. Only the exported copy gets it, never this client's data.
- Verified against lazer's importer in `Database/LazerTestImportTest`. To debug on a real install read lazer's `~/.local/share/osu/logs/*.database.log`: a replacement logs "Found existing beatmap set with same OnlineID".

Executable lookup: `OsuSetting.LazerExecutable` (`game.ini`, empty by default), else `~/Downloads/osu.AppImage`, `~/Applications/osu.AppImage`, `~/osu.AppImage`. There is no UI for the setting yet. The file must be passed to the **same executable that's already running** for the IPC hand-off.

### 5.4 Storage and settings

- Data dir (Linux): `~/.local/share/osu-tryhard` (`osu-tryhard-development` in debug builds). Contains `client.realm`, `files/`, `framework.ini`, `game.ini`, `input.json`, `hitsounds/`, `rhythm/`, `exports/`, `logs/`.
- Runtime logs: `logs/*.runtime.log` (exceptions show up as `[error]` lines with stack traces). A screen that loads blank almost always means an exception in a `[BackgroundDependencyLoader]`; check the log.
- Moving the whole data dir: `storage.ini` in the default dir with `FullPath = /path` (`OsuStorage`). There is no "songs folder": maps are imported (copied) into the realm/file store; see README for drag & drop.
- Also `combocolours/<BeatmapInfo.ID>.txt` (combo colour sections, 5.9).
- Fork settings in `OsuSetting`: `HitsoundSamplesDirectory`, `LazerExecutable`, `MapsetVerifierPath`, `EditorTimelineShowRhythmStrip`, `EditorShowSliderSectionAngles`.
- The editor persists realm-stored key bindings by int `GlobalAction`; never reorder or insert in the middle of that enum.

### 5.5 Slider section angles (`Osu/Edit/Blueprints/Sliders/Components/SliderSectionAngleOverlay.cs`)

Shows the angle of each slider section while editing. Angles are folded into [0, 90] degrees (`GetSectionAngle` uses abs of the direction), decimals are a parameter (`FormatAngle(angle, decimals = 0)`), and the label side is sticky with hysteresis (`side_switch_distance`) so it doesn't flicker while placing a slider. Toggle: Alt+A (`EditorShowSliderSectionAngles`).

### 5.6 Window, cursor, volume

- Starts borderless fullscreen every launch (saved window mode is overridden on purpose); F11 or Alt+Enter toggles windowed/borderless (framework's own binding would cycle through exclusive fullscreen too, so `OsuEditorGame.OnKeyDown` intercepts first).
- `ConfineMouseMode.Never` and `MouseHandler.UseRelativeMode = false` so the cursor can leave the window (relative mode is what captures it while the OS cursor is hidden).
- `GlobalVolumeControl`: three `RoundedSliderBar`s (master/audio/hitsounds) at top centre, bound to the same `AudioManager` bindables the (still present) Alt+Up/Down `VolumeOverlay` uses.

### 5.7 Beatmap select (`Screens/EditorSelect/EditorBeatmapSelect.cs`)

Reads sets from realm (`allSets`), `SelectedSet` bindable. **`BeatmapSetInfo` equality is by ID**, so assigning a fresh model to `SelectedSet` is a no-op; `updateDifficulties` re-resolves the set by ID from `allSets` (this was the "stale difficulty name after editing" bug). Background: loads the first difficulty's texture off-thread, request counter guards against stale results, dimmed with `Background6`.

### 5.8 Ranking criteria checks with Mapset Verifier (`Screens/Edit/Verify/`)

The Verify screen (upstream's, `IssueList`/`IssueTable`) runs [Mapset Verifier](https://github.com/Naxesss/MapsetVerifier) (MV, v2+, by Naxess) when it's installed, and the built-in checks (`Rulesets/Edit/Checks/`) otherwise or when MV fails. The status text bottom left (`IssueList.Source`) says which one produced the list.

- **MV is GPL-3.0, this repo is MIT: never reference its projects, copy its checks or commit its binaries.** It is used out of process, over HTTP, like MV's own Electron UI does (`MapsetVerifierServer`).
- Backend = the ASP.NET `MapsetVerifier` executable (self-contained). `MapsetVerifierPath` (`game.ini`) is that executable, or the packaged app (an AppImage: the backend is extracted once with `--appimage-extract resources/bin/*` into `<data dir>/mapset-verifier/<size>-<mtime>/`). Empty = `/usr/bin/mapset-verifier` (the AUR/Linux package; ponytail: no other auto-detection). It's started on first use on port **51005** (MV's UI uses 5005) with `--urls=`, an already running one on that port is reused (`/health`), and `MapsetVerifierServer.Stop()` kills it in `OsuEditorGame.Dispose`. A backend orphaned by a crash/`kill -9` of this client lives on; a wedged one needs `kill -9`.
- Flow (`IssueList.runMapsetVerifier`): MV reads a **folder**, so the set's files are written to `%TMP%/osu-tryhard-verify/<setId>/` (emptied first, otherwise old files show up as unused) with the open difficulty encoded from memory (`LegacyBeatmapEncoder` on `PlayableBeatmap`, so unsaved edits are checked, the other difficulties are the saved ones), then `POST /beatmap/runChecks {folder, createSnapshot:false}`. Use absolute paths: a relative one is resolved against the backend's cwd. One check that throws makes the whole run a 500 (the message is shown, built-in checks take over).
- `MapsetVerifierIssues.Parse` turns the JSON into the upstream `Issue` type, so the table, filters and click-to-seek work unchanged: `general` -> `CheckScope.BeatmapSet`, the difficulty whose `category` equals `DifficultyName` -> `CheckScope.Difficulty`; levels Problem/Warning/Error map 1:1, Minor/Info/Check -> `Negligible` (hidden by default; Minor is the bulk of MV's output); a leading `mm:ss:fff (objects) - ` of the message becomes `Issue.Time` (and the objects at that time are attached, so a click selects them). `MapsetVerifierCheck` is the adapter `ICheck`; the table's category column shows its description (MV doesn't give categories). The interpreted-difficulty setting only affects the built-in checks.
- Tests: `MapsetVerifierTest` (Osu.Tests: parsing, folder writer, and a run against the real backend that is ignored when MV isn't installed) and `TestSceneVerifyScreen`. The real-backend tests start MV on this machine; they stop it afterwards.

### 5.9 Combo colour sections (`Screens/Edit/ComboColours/`, Setup > Colours)

Lazer already has a per-combo colour picker (`NewComboTernaryButton`, writes `ComboOffset`); this adds what it lacks: colour *patterns over time*. Lives in the Setup screen's colours section, under the palette (no second palette UI anywhere).

- `ComboColourSection(Time, Colours, Burst)`: from `Time` on, new combos cycle through `Colours` (0-based palette indices); a burst recolours only the first combo at/after its time and the cycle carries on as if it weren't there. No section, or an empty colour list = combos left alone. `TIME_LENIENCY` 1 ms.
- Colour shown for a combo = `(ComboIndexWithOffsets - 1) mod n` (the palette is rotated by one in `EditorBeatmapSkin`), so the needed offset is `(desired - previousIndexWithOffsets) mod n`. `ComputeOffsets` / `ColourIndices` are pure; `GetComboStarts` detects starts by `ComboIndex` changing (so spinners/breaks follow the ruleset's own rules); `Apply` writes `ComboOffset`s as one undo step.
- "Add section here" uses the **unsnapped** current time on purpose: snapping could land just after an unsnapped combo start. Seek with the bottom bar / arrow keys.
- Sections are tool state saved on every change (`ComboColourSerializer`), not map content; the map only gets the offsets on Apply. The preview strip shows every combo's colour after applying.
- Palette generator (same section): gradient first->last colour (HSV, short way round the hue circle), hue spread, shades. `ToHsv`/`FromHsv` are local (don't rely on framework HSV helpers).

### 5.10 Map cleaner (`Screens/Edit/MapCleaner/`, Edit > Clean up difficulty, Ctrl+Shift+C)

`MapCleaning.Plan` returns `CleanupChange`s (time, description, apply action) without touching anything; the overlay lists them (click = seek) and `Apply` runs them as one undo step. Items:
- Resnap objects: start, then end (sliders by scaling `ExpectedDistance`, so **no** velocity change, unlike upstream's "snap all notes" which scales SV; spinners by `Duration`). Only times between `MIN_RESNAP_DISTANCE` (1 ms) and the max distance (default 5 ms) are moved: closest snap over `PREDEFINED_DIVISORS` (includes 1/5, 1/7, 1/9), same as `CheckUnsnappedObjects`.
- Resnap effect points and bookmarks (same rule).
- Redundant timing points: same BPM and meter as the previous kept one, no omitted first bar line, on one of its bar lines. Red lines are never "redundant" to `ControlPointInfo.Add`, so this is the only place that finds them. (Effect point redundancy is already prevented on load/add, not handled.)
- Combo colour skips: cleared on objects that don't start a combo, reduced mod palette size on ones that do (same colour).
- `Slider.Duration`'s setter throws: match `IHasPath` before `IHasDuration`.

## 6. Gotchas learned the hard way

- **Bindable disposal**: disposing a drawable calls `UnbindAll()` on every `Bindable` held in its fields. If a popover/drawable stores a bindable *owned by someone else* (e.g. passes the same instance it was given), closing it silently unbinds all listeners of that bindable forever. Keep a callback (`Action<T>`) instead. (Caused "use this file does nothing after the first add".)
- **DI order**: for a class deriving from a base with a `[BackgroundDependencyLoader]`, the base's loader runs first. `TimelinePart.load` calls the virtual `LoadBeatmap` before the derived loader; anything `LoadBeatmap` touches must be created in the constructor/field initializer (caused the blank hitsounds screen).
- **Sample resolution**: `HitSampleInfo.UseBeatmapSamples` must be true to use the beatmap's own files; `Equals` includes it.
- **Detached realm models**: `BeatmapManager.AddFile/DeleteFile` refresh `item.Files` from realm after each call, so the detached `working.Value.BeatmapSetInfo` stays current. `AddFile` on a set that isn't saved yet fails (`SaveEditor()` first in tests with new beatmaps).
- **`GetResultSafely`**, **`TruncatingSpriteText`**: see analyzer list.
- `EditorScreenWithTimeline` loads the `TimelineArea` async after the main content; screens that build their own layout (hitsounds) add `TimelineArea` directly in `load`.
- Timeline mouse wheel is taken by the timeline (scroll/zoom): paging of anything else must live on buttons or on a non-timeline drawable.
- `EditorSkinProvidingContainer(editorBeatmap)` must wrap any `SkinnableSound` that should resolve beatmap samples. Plain `SkinnableSound`s are not gated by the editor's `samplePlaybackDisabled`, which is why non-compose screens have to play audio themselves.
- Dual keyboard handlers: `IKeyBindingHandler<GlobalAction>` + `IKeyBindingHandler<PlatformAction>` on one drawable is fine (hitsounds screen does it for select tool / select all / delete).
- Hitsounds default key `F8` for "test in lazer" is also upstream's chat key; irrelevant here (no chat).

## 7. Testing notes

- Visual tests derive `EditorTestScene` (editor on a `TestBeatmap`, saving isolated from the database) or `EditorSavingTestScene` (full `OsuGame`, real `BeatmapManager`; call `SaveEditor()` before touching files of the set). Drive the UI with `InputManager`, locate drawables with `ChildrenOfType<T>()`; private nested classes (e.g. the sample cells) are found by their `Text` ("+" / "play").
- Mouse-driven drag tests: press, `MoveMouseTo`, then `AddUntilStep` on a model value (e.g. `EditorClock.CurrentTime`) while still holding the button.
- Prefer testing logic through the pure helpers (`HitsoundEditing`, `HitsoundSampleProcessor.Process`, `LazerTestLauncher.SetOnlineID/WriteArchive`) and keep UI tests for wiring.
- `LazerTestImportTest` runs lazer's real `BeatmapImporter` against a temp realm to prove replace-on-resend behaviour.
- Not covered by any automated test: the actual hand-off to a running lazer, real audio output, fullscreen/cursor behaviour, the sound folder popover, "apply to all difficulties" saving other difficulties. Treat changes there as unverified until tried by hand.

## 8. Conventions

- Line endings: `*.cs`, `*.csproj`, `*.sln`, `*.resx` are **CRLF in the working tree** (`.gitattributes`: `eol=crlf`); some files carry a UTF-8 BOM (`.editorconfig` `charset = utf-8-bom`, e.g. `Editor.cs`). Don't strip them. Tools that write LF produce "LF will be replaced by CRLF" warnings: re-normalise with `sed -i 's/\r$//; s/$/\r/' <file>` after editing, never leave mixed endings. New files need the licence header (see any file; `osu.licenseheader`).
- Style follows upstream (`.editorconfig`, `CodeAnalysis/`); comments explain *why*, not what.
- UI text for fork features is plain English strings (not `LocalisableString` resources) unless it's a menu/key label, which goes through `EditorStrings` / `GlobalActionKeyBindingStrings`.
- Simplifications that have a known ceiling are marked with a `// ponytail:` comment naming the upgrade path.

## 9. Git workflow

- Commit and push only when asked. Keep commits focused (separate unrelated features/fixes even if developed together).
- **Keep this file alive.** Before committing a feature/fix that built successfully, update `AGENTS.md` in the same commit: add what you learned (new systems, files, settings, keys, gotchas, test notes), and correct or remove anything your change made outdated. Improve existing sections instead of appending duplicates; skip it only if the change genuinely taught nothing new.
- Message style: short imperative title, bullet body explaining *what and why*. Trailer: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- No git identity is configured in this environment; set `GIT_AUTHOR_NAME/EMAIL` and `GIT_COMMITTER_NAME/EMAIL` (`Claude` / `noreply@anthropic.com`) for the command instead of changing git config.
- `osu.Game/OsuEditorGame.cs` has seen uncommitted changes from the maintainer in the past (moving `removeObsoleteKeyBindings` into `LoadComplete`); check `git status` and `git diff` before committing to avoid sweeping in unrelated work without saying so.

## 10. Where to look for what

- "Why is screen X blank?" -> `~/.local/share/osu-tryhard/logs/*.runtime.log`, search `[error]`.
- "How does the editor do Y?" -> find the upstream equivalent in `osu.Game/Screens/Edit/` first; this fork mostly reuses it (e.g. `FormSampleSetChooser` / `FormSampleSet` in `Edit/Setup` and `Edit/Components`, `FileChooserPopover` in `Graphics/UserInterfaceV2/FormFileSelector.cs`).
- "How does lazer import / match sets?" -> `~/Github/osu-local/osu.Game/Beatmaps/BeatmapImporter.cs`, `Database/RealmArchiveModelImporter.cs`, `BeatmapUpdaterMetadataLookup.cs`.
- "What do the ranking criteria require?" -> `osu.Game/Rulesets/Edit/Checks/*` and https://osu.ppy.sh/wiki/en/Ranking_criteria
