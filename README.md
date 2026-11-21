# Stickies_Window

A copy of macOS Stickies for Windows. Small single `.exe`, nothing to install.

## Build

1. Download this repo.
2. Double-click `build.bat`.
3. Run `Stickies_Window.exe`.

Needs Windows 10 or 11 (.NET Framework 4.8 is already built in). Rounded corners (optional) need Windows 11.

## Use

| Action | How |
|---|---|
| Menu | Right-click a note |
| New note | `Ctrl+N` or `Ctrl+T`, the tray icon, or `Ctrl+Alt+N` from anywhere |
| Reopen the last closed note | `Ctrl+Shift+T` |
| Close note (deletes it) | `Ctrl+W` or the left box. Asks to save first |
| Move | Drag the title bar |
| Resize | Drag any edge |
| Collapse / expand | Double-click the title bar, the far-right box, or `Ctrl+M` |
| Full screen / back to normal size | The full screen box on the title bar, or `F11` |
| Minimize | The minimize box on the title bar. Bring it back from the tray icon > Minimized Notes (or Bring All to Front) |
| Keep on top | The pin on the title bar, or `Ctrl+Alt+F` |
| Keep this note and the notes snapped to it on top | `Ctrl+Shift+Alt+F` (press again to switch it off) |
| Snap notes together | Drag a note near another one and the edges stick like magnets |
| Move snapped notes together | Hold `Shift` while dragging a title bar |
| Translucent | `Ctrl+Alt+T` |
| Settings | `Ctrl+,`, or `Alt+`` ` from anywhere |
| Bring all notes to front | `Alt+Shift+F` from anywhere, or click the tray icon |
| Bigger / Smaller text | `Ctrl++` / `Ctrl+-` |
| Fonts / Text color | `Ctrl+Shift+F` / `Ctrl+Shift+C` |
| Align left / center / right | `Ctrl+Q` / `Ctrl+E` / `Ctrl+R` |
| Find / Replace / Find next | `Ctrl+F` / `Ctrl+H` / `Ctrl+G` |
| Open a saved note as a sticky | `Ctrl+O`, or double-click a `.sticky` file |
| Save | `Ctrl+S` (asks where only the first time) |
| Save As | `Ctrl+Shift+S` |
| Print | `Ctrl+P` |
| Images | Paste, or drag an image file onto a note |
| Quit | Right-click the tray icon > Quit Stickies_Window |

### Formatting shortcuts

All of these can be changed in Settings > Formatting. Press a shortcut again to undo it.

| Format | Shortcut |
|---|---|
| Bold / Italic / Underline | `Ctrl+B` / `Ctrl+I` / `Ctrl+U` |
| Strikethrough | `Ctrl+J` |
| Highlight | `Ctrl+Shift+H` |
| Task list: add / remove a checkbox | `Ctrl+Shift+X` |
| Task list: check / uncheck | `Ctrl+Enter`, or click the checkbox |
| Heading 1 / Heading 2 | `Ctrl+Shift+1` / `Ctrl+Shift+2` |
| Emphasize (extra weight on top of bold) | `Ctrl+Shift+B` |

- **Task list:** checking a task strikes through the text after the box and unchecking removes it, as one undo step (the strikethrough can be turned off in Settings).
- **Links:** just type or paste a web address and it becomes clickable on its own.
- **Headings:** bold + bigger text on the line.

## Look

- **Border:** a solid 2 pixel black border around every note.
- **Corners:** sharp 90-degree corners by default, like macOS Stickies. Switch to rounded in Settings > Window.
- **Title bar:** a full-width strip across the top of the active note. When you click away it disappears, and comes back when you click the note again. Close is on the left. On the right, from left to right: pin (keep this note on top of other windows), minimize, full screen, and collapse / expand. Icons show on the buttons when you hover the bar (can be turned off in Settings).
- **Scrollbar:** a thin macOS-style overlay scrollbar. It is just a rounded translucent thumb, fades in when you scroll or move the mouse near the right edge, and fades out when you stop. Drag it, or click next to it to jump.
- **Text cursor:** thin, rounded, same color as the text, blinking half a second on and off, like macOS. Can be switched back to the normal Windows cursor in Settings > Text.
- **Snapping:** notes stick to each other's edges when you drag them close. Can be turned off in Settings > Window. Hold `Shift` while dragging to move every note that is snapped together as one group.

## Colors

12 slots. The first 6 are the exact colors macOS Stickies uses:

| Color | Hex | RGB |
|---|---|---|
| Yellow | `#FEF49C` | rgb(254, 244, 156) |
| Blue | `#ADF4FF` | rgb(173, 244, 255) |
| Green | `#B2FFA0` | rgb(178, 255, 160) |
| Pink | `#FFC7C7` | rgb(255, 199, 199) |
| Purple | `#B6CAFF` | rgb(182, 202, 255) |
| Gray | `#EEEEEE` | rgb(238, 238, 238) |

The other 6 start empty so you can add your own, by hex code (`#FEF49C`) or RGB. Any color can be disabled, which skips it when cycling and hides it from the Color menu.

### Softer colors to try

If you want something gentler than the defaults, these look good too. Put them in the Custom slots in Settings > Colors (they were the defaults in earlier versions):

| Color | Hex | RGB |
|---|---|---|
| Soft Yellow | `#F3E4AA` | rgb(243, 228, 170) |
| Soft Blue | `#C9DBE8` | rgb(201, 219, 232) |
| Soft Green | `#CFE0C3` | rgb(207, 224, 195) |
| Soft Pink | `#EED3D3` | rgb(238, 211, 211) |
| Soft Purple | `#DCD3E8` | rgb(220, 211, 232) |
| Soft Gray | `#E2E0DC` | rgb(226, 224, 220) |

## Settings

Open with `Ctrl+,`, the tray menu, or `Alt+`` ` from anywhere.

- **New notes:** color mode (cycle / random / always the same / start with my color, then cycle) and where on screen new notes open (9 positions).
- **Colors:** edit, add and disable colors.
- **Text:** default font, size, style and color for new notes, and what Emphasize does (extra size, optional highlight and color) for when Bold is already your normal text.
- **Formatting:** change any formatting shortcut, the highlight color, and whether checked tasks get a strikethrough.
- **Window:** sharp or rounded corners, title bar icons, snapping notes together.
- **General:** start with Windows, the three shortcuts that work from anywhere (new note `Ctrl+Alt+N`, Settings `Alt+ ~ ` and bring all to front `Alt+Shift+F`), and the notes folder.

## Saving and opening

Your notes are saved automatically as you type (see below), so you never lose one. On top of that, a note can be saved to a file of your choice:

- `Ctrl+S` saves the note to its file. The first time it asks where; after that it just saves, with no questions.
- `Ctrl+Shift+S` (Save As) always asks where, so you can save a copy or move the note's file.
- Once a note has a file, every change you make is saved to that file automatically too. Closing such a note never asks you to save, because the file is already up to date.
- A note that has never been saved to a file asks before it is closed, since closing a note removes it.
- Save as `.sticky` to keep everything: the text and formatting, plus the note's position, size, color and settings. Opening a `.sticky` file (`Ctrl+O`, or double-click it in Explorer) brings the note back just where and how it was written. `.rtf` and `.txt` work too (text only).

## Start with Windows

Settings > General > "Start Stickies_Window when I sign in to Windows".

## Where notes are saved automatically

On first launch the app asks which folder to use (default `%AppData%\Stickies_Window\`). In that folder: one `.rtf` file per note, plus `notes.xml` for position, size, color and settings, and `notes.xml.bak` (the previous version). Saved automatically, using temp files so a crash can't corrupt a note. If saving fails you get a tray warning.

Change the folder any time in Settings (notes are copied, the old ones are left in place), or open it from the tray menu. The folder choice and the global shortcuts are stored per computer in `%AppData%\Stickies_Window\config.xml`.
