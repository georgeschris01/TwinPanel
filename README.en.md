# TwinPanel

[Français](README.md) · **English**

A free, modern and complete two-panel file manager. Windows for now, Linux next. Interface in English and French.

- Two panels, tabs, quick view, folder tree, thumbnails, flat view
- Copy and move with a transfer queue, resume after a cut, free space check
- SFTP, SCP, FTP, FTPS and WebDAV servers, used just like folders
- Built-in terminal on the PC and on SSH servers: tabs, side by side, saved commands, search
- Editable zip, 7z, tar and tar.gz archives, AES-256 encrypted zip
- Search, duplicates, comparison, synchronization with profiles
- Multi-rename, also from media data
- Light or dark theme, customizable shortcuts, colors and button bar

**[Download TwinPanel](../../releases/latest)** (Windows installer).

> **“Windows protected your PC” message:** TwinPanel is free and not signed, because a signing certificate must be paid for every year. Windows therefore shows this message until the program is well enough known. Click **More info**, then **Run anyway**. Make sure the file comes from this page; its SHA-256 checksum is given with each version.

To request a feature or report a problem: open an [“Issue”](../../issues) on the TwinPanel GitHub page. See [`CONTRIBUTING.md`](CONTRIBUTING.md).

## The origin of this project (Why and how?)

This software was born from a very personal need. Since 2003, I have used the excellent **Total Commander** every day, and I find it absolutely brilliant. However, for my specific use, some features were missing or not entirely optimal.

So I decided to create **TwinPanel**, originally just for my own use and my wife's. After I showed it to friends who wanted to try it, they strongly encouraged me to share it and make it available to other potential users, because it could be of great service — even though other similar projects exist on the market.

### Complete transparency with you:

I want to be 100% honest with everyone: **I know absolutely nothing about programming and I speak no language other than French**.

**TwinPanel** came into being, was entirely coded and was translated into English thanks to the help of the artificial intelligence **Claude Code**. The AI guided me step by step to turn my needs into lines of code and to generate the English version of the software, so that it can reach as many people as possible.

The program is shared here as it is, wholeheartedly, with the sole aim of being useful. It is entirely free, but as you can read in the License section below, it remains protected to ensure it is never commercialized or distorted.

Welcome to all future TwinPanel users!

## Guide

What follows is the full guide. It is the same text as in the program (**F1** key).

<!-- guide -->
## Getting started

TwinPanel shows two folders side by side: you work in the **active panel**, the one with the blue outline, and most operations target the **other panel**.

### The screen

- At the top: the toolbar. The **≡** button gives access to all the commands.
- In each panel: drives and connections, tabs, the path, then the file list.
- At the bottom: the command line and the clickable F3 to F8 keys.
- Running transfers appear just above the command line.

### The five gestures to know

| Gesture | Result |
|---|---|
| `Tab` | Switch panel |
| `Enter` or double click | Open a folder or a file |
| `Backspace` | Go back to the parent folder |
| `Ins` or short right click | Mark a file |
| `F5` / `F6` | Copy / move marked files to the other panel |

If nothing is marked, operations apply to the file under the cursor.

To reach a file faster, just type the first letters of its name (`Esc` to cancel).

## The mouse

TwinPanel can be driven entirely with the mouse. Several gestures cannot be guessed: here is the full list.

### In the file list

| Gesture | Result |
|---|---|
| Left click | Choose the line |
| Double click | Open the folder or the file |
| Click the name of the line already chosen | Rename in the list (`Enter`: confirm, `Esc`: cancel) |
| Ctrl + click | Mark an item |
| Shift + click | Mark a range of items |
| Short right click | Mark or unmark the item |
| Right button held while dragging | Mark or unmark everything the mouse passes over |
| Right button held ½ second | Open the item menu |
| Middle click on a folder | Open it in a new tab, in the background |
| Double click on an empty part | Go back to the parent folder |
| Side buttons | Previous / next folder |
| Ctrl + wheel | Larger or smaller text (in thumbnails: image size) |
| Ctrl + middle click | Back to normal size |

Click on the name can be turned off in Options → Behavior.

### Drag and drop

Hold the left button on an item and move the mouse. If the item is marked, all marked items go with it.

You can drop it on a folder, on the other panel, on a tab, on a drive or connection button, or in Windows File Explorer. The reverse direction from File Explorer works too.

| When dropping | Result |
|---|---|
| No key | “Copy here / Move here” menu |
| Shift held | Move |
| Ctrl held | Copy without asking |

### Tabs and columns

| Gesture | Result |
|---|---|
| Middle click on a tab | Close it |
| Right click on a tab | Duplicate, rename, lock, close, delete duplicates |
| Drag a tab | Move it elsewhere, or send it to the other panel |
| Wheel on the tabs | Scroll the tabs |
| Click a column title | Sort by this column |
| Drag a column title | Change the column order |
| Right click on the titles | Choose the displayed columns |
| Right click on the Back button | Recent folders |

## The keyboard

All shortcuts can be changed in Options → Shortcuts. In the program, the list below shows the ones actually in use.

<!-- raccourcis -->
### Navigation

| Key | Action |
|---|---|
| `Tab` | Switch panel |
| `Enter` | Open |
| `Backspace` | Parent folder |
| `Ctrl+T` / `Ctrl+W` | New tab / close tab |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Next / previous tab |
| `Alt+←` / `Alt+→` | Previous / next folder |
| `Alt+↓` | Recent folders |
| `Ctrl+Shift+J` | Tab sets |
| `Ctrl+PgUp` / `Ctrl+\` | Parent folder / root |
| `Ctrl+D` | Favorites |
| `Ctrl+L` | Edit the path |
| `Ctrl+H` | Hidden files |
| `Ctrl+F3` to `Ctrl+F6` | Sort by name / extension / date / size |
| `Ctrl+U` | Swap panels |
| `Ctrl+Shift+O` | Same folder in the other panel |
| `Ctrl+Shift+←` / `→` | Open the folder in the other panel |
| `Ctrl+R` | Refresh |
| `Ctrl+B` | Flat view |
| `Ctrl+Q` | Quick view |
| `Ctrl+F8` | Folder tree |
| `Ctrl+Shift+F1` | Thumbnails |
| `F12` / `Ctrl+F12` | Built-in terminal: show or hide / new terminal |
| `Ctrl+Shift+F12` | Two terminals side by side |
| `Alt+F12` | Switch side in the terminal |
| `Ctrl+Shift+F` | In the terminal: search the text |

### Marking

| Key | Action |
|---|---|
| `Ins` | Mark and move down |
| `Space` | Mark (calculates the size of a folder) |
| `Shift+↑` / `Shift+↓` | Mark while moving |
| `Num+` / `Num-` | Mark / unmark by mask |
| `Num*` | Invert marking |
| `Alt+Num+` | Mark the same extension |
| `Ctrl+A` / `Ctrl+Shift+A` | Mark all / unmark all |

### Operations

| Key | Action |
|---|---|
| `F1` | This guide |
| `F3` | View |
| `Alt+F3` | Open with the associated application |
| `F4` | Edit |
| `Shift+F4` | New text file |
| `F5` / `F6` | Copy / move |
| `Shift+F6` | Rename |
| `Ctrl+M` | Multi-rename |
| `Ctrl+Z` | Undo the last multi-rename |
| `F7` | New folder |
| `F8` / `Del` | Delete |
| `Alt+Enter` | Properties |
| `Alt+F5` / `Alt+F9` | Create an archive / extract |
| `Ctrl+Enter` / `Ctrl+Shift+Enter` | Name / path in the command line |
| `Ctrl+Shift+C` / `Ctrl+Alt+C` | Copy paths / names |
| `Ctrl+Shift+T` | Open a terminal here |

### Search and connections

| Key | Action |
|---|---|
| `Alt+F7` | Search |
| `Shift+F2` | Compare folders |
| `Ctrl+Shift+F2` | Compare the contents of two files |
| `Ctrl+Shift+S` | Synchronize folders |
| `Ctrl+F` | Connection manager |
| `Ctrl+N` | Quick connect |
| `Ctrl+Shift+F` | Disconnect the active panel |

### Viewer and editor

| Key | Action |
|---|---|
| `N` / `P` (viewer) | Next / previous file |
| `1` `2` `3` `4` (viewer) | Text, hex, image, media info |
| `Ctrl+F` (viewer) | Search |
| `Ctrl+S` (editor) | Save |
| `Ctrl+F` / `F3` (editor) | Search / next |
| `Ctrl+H` (editor) | Replace all |
| `Ctrl+G` (editor) | Go to line |
<!-- /raccourcis -->

## Tabs and views

Each panel has its tabs: `Ctrl+T` opens one, `Ctrl+W` closes it. They are reopened at the next startup, including those on a server (Options → Startup).

### Locking a tab

Right click on the tab:

- **Lock this tab**: going elsewhere opens a new tab.
- **Lock this tab, folder changes allowed**: you can navigate, and the tab returns to its folder when you come back to it.

### Tab sets

`Ctrl+Shift+J` or ≡ → Tabs: save the tabs of both panels under a name, then bring them back with one click.

### Views

- **Quick view** (`Ctrl+Q`): the other panel shows the file under the cursor.
- **Folder tree** (`Ctrl+F8`): the folder tree next to the list.
- **Thumbnails** (`Ctrl+Shift+F1`): images and videos on the PC, images on a server (each tab has its own mode).
- **Flat view** (`Ctrl+B`): all the files from subfolders, on the PC and on a server. `Esc` stops reading.

Sorting by size (`Ctrl+F6`) also orders the folders whose size is known: calculated automatically (**Folder size** option) or with the space bar. Folders stay before files.

## Marking and working on files

### Marking

Mark with `Ins`, `Space`, `Shift+arrows`, the right click, or by mask with `Num+`: `*.jpg;*.png` chooses two types, `*.* | *.tmp` chooses everything except `.tmp` files.

### Copy and move

`F5` copies, `F6` moves. The window lets you change the destination and choose what to do if an item already exists: ask, overwrite, overwrite older items, skip, or keep both.

- The number of simultaneous transfers is set in Options → Behavior (1 by default): the next ones wait their turn.
- A transfer can be paused, resumed or canceled.
- When space runs out, TwinPanel stops on the file concerned. Free some space then click **Resume**.

### View, edit, delete

- `F3` opens the built-in viewer: text, hex, image, media info.
- `F4` opens the chosen editor, Notepad by default. A built-in editor is also available, and each extension can have its own editor (Options → Editors).
- `F8` sends to the system trash. On a server or in an archive, deletion is permanent and always confirmed.

### Multi-rename

`Ctrl+M` shows the result before renaming. The name mask accepts `[N]` name, `[N1-3]` first three letters, `[C]` counter, `[YMD]` date, `[h][m][s]` time, `[P]` folder. Search / replace accepts regular expressions. `Ctrl+Z` undoes the last multi-rename.

### Properties

`Alt+Enter` shows the real size, dates, permissions, owner and group on a server or on Linux, NTFS attributes and security on Windows, and media tags.

## Search, compare, synchronize

### Search

`Alt+F7` searches by name, contained text, size and date, in several folders and on open servers. The **Duplicates** box finds files with identical contents.

On a server, sizes are compared first. Over SSH, the server calculates the hashes itself, without transferring anything. Otherwise, TwinPanel announces the volume to read and asks for your approval.

### Comparison

- `Shift+F2` compares both panels and marks the differences.
- `Ctrl+Shift+F2` compares the contents of two files.
- ≡ → Tools: create and verify MD5, SHA-1 and SHA-256 checksums.

### Synchronization

`Ctrl+Shift+S` opens synchronization. Choose the folders and the direction, click **Compare**, check the list, then start. Click an arrow to change the action of a line. Nothing is copied without this step.

A **profile** keeps the folders, direction and options.

## Servers

TwinPanel connects over SFTP, SCP, FTP, FTPS and WebDAV. A server is used like a folder: `F5` uploads or downloads, including from one server to another.

### Connecting

- `Ctrl+N`: quick connect, for example `user@192.168.0.10`, `ftps://…` or `https://…/remote.php/dav/files/me`.
- `Ctrl+F`: connection manager, with entries, groups, SSH keys, identities, known servers and vault.
- A button in the drive bar opens each saved connection.
- Connections from FileZilla, WinSCP, PuTTY, OpenSSH and Total Commander can be imported.
- A server that cannot be reached directly goes through a **jump server**, chosen in its entry. That jump server can itself go through another one, up to 8 in a row. The fingerprint of each server is checked.

### Security

Saved passwords are encrypted in a vault tied to your Windows account, with an optional master password. Exported connections contain no secret.

At the first SSH connection, compare the server fingerprint before accepting it. If it changes later, TwinPanel blocks the connection and warns you.

### Files on a server

`F4`, `Enter` or a double click open a copy on the PC. At each save, TwinPanel offers to send it back to the server (or without asking: Options → Behavior).

### Interrupted transfer

TwinPanel retries by itself (3 times by default), then offers **Resume**. Resuming continues from where it stopped over SFTP, and over FTP / FTPS or for WebDAV downloads if the server accepts it. Otherwise, the current file restarts from zero. Files already finished are never sent again.

If TwinPanel was closed during a transfer, at the next startup it offers **Resume**, **Later** or **Forget**. To come back to it: ≡ → Connections → Unfinished transfers.

## Archives

`Enter` opens an archive like a folder. `Backspace` leaves it.

| Format | Possible |
|---|---|
| zip, 7z, tar, tar.gz | Read, extract, edit |
| rar, gz, bz2, xz | Read and extract |
| Encrypted zip | Read, extract, edit with its password |
| Encrypted 7z | Read and extract only |

| Key | Action |
|---|---|
| `F5` (in an archive) | Extract to the other panel |
| `F6` (in an archive) | Move out of the archive |
| `F5` / `F6` to an open archive | Add to the archive |
| `Shift+F6`, `F7`, `F8` | Rename, create a folder, delete |
| `Alt+F9` | Extract the whole archive next to it |
| `Alt+F5` | Create an archive (.zip, .tar, .tar.gz), with an optional password |

An edited archive is rewritten next to the original, read back, then it replaces the original: free space is needed, and it takes time on large files.

An archive password stays in memory until TwinPanel closes and is saved nowhere. In an encrypted zip, the content is protected, but file names remain visible.

## Media

### Information and tags

- Optional columns: duration, resolution, video codec, audio languages (right click on the column titles).
- `F3` then `4`: full report on a video, music file or photo.
- `Alt+Enter`: change the tags of music files and MP4s.

Two optional tools, to install yourself, add more possibilities: **mkvpropedit** (MKV tracks) and **exiftool** (photo EXIF). Their path is set in Options → View.

### Renaming from media data

In `Ctrl+M`, on PC files: `[Title]`, `[Artist]`, `[Album]`, `[Track]`, `[Year]`, `[Genre]`, `[Resolution]`, `[Duration]`, `[Date taken]`. A file without this value keeps its name. The French names work too: `[Titre]`, `[Artiste]`…

## Terminal

A real terminal at the bottom of the window, written for TwinPanel: colours, `vim`, `nano` and `htop` all work in it.

### Opening it

- `F12` opens the panel. If the keyboard is already in the terminal, `F12` hides it: terminals keep running.
- The **Terminal** button in the top bar opens a menu: show or hide, new terminal, duplicate, a program on your PC or one of your SFTP / SCP servers.
- `Ctrl+F12` opens another terminal in a new tab. The **+** button after the tabs offers your programs and your SFTP / SCP servers.
- The **⧉** button on a tab duplicates it: same program or same server, in its starting folder. The copy has a number in its tab, such as `server (2)`. Right-clicking a tab also offers **Duplicate**; side by side, you choose which side the copy goes to.
- The **Side by side** button or `Ctrl+Shift+F12` shows two terminals together, with a separator to drag. Each side has its own tabs and **+** button. If there is only one terminal, the second is a copy of it. Click a side to make it active: it receives the keyboard and saved commands. `Alt+F12` switches side. The width of the separator is remembered. Right-clicking a tab also offers **Move** to the other side. Going back to one side keeps every terminal open.
- The terminal starts in the folder of the active panel.
- On an SFTP or SCP server, it opens a shell on that server, with the same credentials, jump host and server check as the panel.
- Drag the separator above the terminal to change its height: TwinPanel remembers it.

When the keyboard is in the terminal, keys go to the program running there: `F5`, `F8` or `Ctrl+W` do not act on files. Only the terminal's own shortcuts stay active.

### Copy and paste

- `Ctrl+C` copies the selected text. With no selection, it interrupts the program, as in any terminal.
- `Ctrl+V` always pastes. `Ctrl+Shift+C`, `Ctrl+Shift+V` and `Shift+Insert` work too.
- Right-click: copies the selection, or pastes if nothing is selected.
- Double-click: a word. Triple-click: a line.
- If a program uses the mouse, hold `Shift` to select text.
- Before pasting several lines into a program that would run them at once, TwinPanel asks for confirmation. Pasted text loses its control characters, and a final line break runs nothing.

When the keyboard is in the terminal, every key goes to the program running there: `F5` does not copy a file. Only `F12` and `Alt+F4` stay with TwinPanel. Click a list to go back to your files.

### Saved commands

The **Commands** button shows or hides a panel to the right of the terminal.

- **Add**: give a name and write the command, exactly as it should be sent.
- **Run** starts the command in the terminal on display. **Copy** puts it on the clipboard.
- **Ask before running**: a confirmation, useful for `rm` or `reboot`.
- **Only on this server**: the command only appears on that server. Otherwise it appears everywhere.

Commands are saved as plain text in your settings: never put a password in them.

### Scrolling back

The mouse wheel or `Shift+PgUp` / `Shift+PgDn` scroll through the last 10,000 lines of each terminal. This text stays in memory and is never written to disk. It disappears on closing.

If a server sends text much faster than the display can follow, TwinPanel skips part of the middle and says so in one line: it stays responsive and `Ctrl+C` acts at once.

### Searching the text

`Ctrl+Shift+F` in a terminal opens a search bar, as does **Search the terminal** in the Terminal button's menu. The word is searched regardless of case, through all the text of that terminal, and the result appears selected. `Enter` goes to older results, `Shift+Enter` to newer ones, `Esc` closes the bar. `Ctrl+C` copies the result once the bar is closed.

### Settings

**Options → Terminal**: the program to start (PowerShell by default, among those installed on your PC), the text size, and a box to turn the built-in terminal off completely.

The `Ctrl+Shift+T` command is still there: it opens the terminal of your choice in its own window.

## Customizing

Everything is set in ≡ → Tools → Options.

| Page | Contents |
|---|---|
| View | Language, theme, font, columns, formats, thumbnails, media tools |
| Colors | Colors by file type, rules by mask, disk icons |
| Behavior | Recycle bin, confirmations, simultaneous transfers, tips |
| Startup | Tabs to reopen, starting folders |
| Shortcuts | All keys, with search |
| Button bar | Your programs, folders and commands |
| Start menu | Your personal menu, with submenus |
| Editors | Editor by extension |
| Terminal | Terminal to open |
| Backup | All the settings in a single file |

Parameters for buttons and the menu: `%P` active folder, `%N` item name, `%F` its full path, `%T` folder of the other panel, `%S` marked items.

## Frequently asked questions

### How do I change the language?

Options → View → Language. TwinPanel offers to restart. At the first launch, it follows the system language.

### I cannot find a feature.

The **≡** button contains all the commands. Options → Shortcuts also lets you search for a command by name, and this guide has a search box.

### How do I undo a deletion?

`F8` sends to the system trash: restore the file from there. On a server or in an archive, there is no recycle bin. `Ctrl+Z` only undoes the last multi-rename.

### Where are my settings?

In `%APPDATA%\TwinPanel`. An update does not touch them.

### How do I move to another PC?

Options → Backup creates a single file with all the settings, and the list of connections if you wish. Passwords are never included: they will be asked again.

### Are my passwords safe?

They are encrypted, never written in clear text, and only your Windows account on this PC can read them. A master password adds a second protection.

### I forgot my master password.

It cannot be recovered: the passwords and keys in the vault are lost. Your connections remain. To start again with an empty vault, close TwinPanel and delete `secrets.dat` in `%APPDATA%\TwinPanel`.

### I changed a file from a server, but the server did not change.

The file opens from a copy on the PC. Save it in your program, then answer **Send back** to the TwinPanel question. Without sending it back, the change stays on the PC.

### Why can't I change this archive?

rar, gz, bz2 and xz are read-only, as are encrypted 7z files. Extract the contents, then create a new archive with `Alt+F5`.

### F4 does not open the right editor.

Choose it in ≡ → Tools → Editor and viewer, or by extension in Options → Editors. If the chosen editor no longer exists, TwinPanel says so and uses Notepad.

### How do I show hidden files?

`Ctrl+H`, or the toolbar button.

### Why are copies done one after the other?

That is the default setting. Increase “Simultaneous transfers” in Options → Behavior.

### How do I request a feature or report a problem?

Open an “Issue” on the TwinPanel GitHub page. Describe what you did and what you expected. Nothing is sent automatically from the program.

### Can I use it at work?

Yes, free of charge, also in a company. It is only forbidden to sell it, rent it out or include it in a paid offering. See the license.
<!-- /guide -->

## Building

The .NET 10 SDK is needed.

- `build.bat`: builds everything, runs the tests, writes the result to `build.log`.
- `run.bat`: starts the application.
- Or: `dotnet run --project src/TwinPanel.App`

| Project | Role |
|---|---|
| `src/TwinPanel.Core` | Logic without interface, testable |
| `src/TwinPanel.App` | Avalonia interface |
| `tests/TwinPanel.Core.Tests` | xUnit tests |

Components used and code map: `COMPOSANTS.md` (in French).

## License

![TwinPanel License](logo/licence/licence-twinpanel.png)

TwinPanel is published under the **TwinPanel License 1.0** — © 2026 Georges Christophe (chrypox). The full French text in the [`LICENSE`](LICENSE) file is the only one that prevails; [`LICENSE.en.txt`](LICENSE.en.txt) is a courtesy translation. In short:

- **Free**: free use, also in companies and internally; forbidden to sell it, rent it out or include it in a paid offering.
- **Sharing**: only the original version, complete, free of charge and with the license.
- **No distribution of changes**: anyone can change the code for their own use, but cannot distribute a modified version or reuse the code in another project.
- **Plugins**: allowed if they are free and do not contain TwinPanel code.
- **Corrections**: proposed through an “Issue”; the author integrates them personally, crediting their author.
- **Rights of others**: something that does not respect a license or your rights? Write to admin@chrypox.be.

This is not a Creative Commons license. Libraries from other developers shipped with TwinPanel remain under their own license: see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) (and, in the program, **About → Libraries used and their licenses**).
