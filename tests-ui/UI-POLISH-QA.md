# UI polish verification

Baseline: `origin/codex/account-membership` at `9b709dc`.

## Changes

- Account forms and labels use transparent backgrounds over the artwork/glass layer; card height follows form content instead of filling the entire viewport.
- Fields have mail/lock/verification icons, password visibility controls, and inline send-code action. Buttons have consistent spacing; secondary links are not stacked filled buttons.
- The home heading/hint labels are transparent. The scroll cue has no filled surface or border, supports keyboard activation, and animates its chevron only when Windows menu animation is enabled.
- Rounded surfaces, artwork, cover images and hover overlays use antialiased paths rather than binary window regions. Paths scale with DPI. Progress tracks use the same path-based rendering.
- The generated home banner and add-game card are embedded resources, with keyboard-accessible add-game interaction retained.

## Verification boundary

Cross-platform compilation and 77 management/download/Magpie assertions can be checked on macOS. These assertions do **not** validate visual rendering. WinForms cannot run on this host, so Windows pixel-level acceptance remains outstanding.

## Required Windows checks

Use 100%, 125% and 150% display scaling, with both themes, at the minimum supported window size and maximized:

1. Homepage: new MU/DLSS5 art loads; heading row shows the continuous page gradient; add-game title is readable; all four cards have intact rounded corners. Hover and keyboard actions must not leave black rectangles.
2. Scroll cue: plain text and chevron only. Click/Enter/Space go to the library, and scrolling upward in the library does not reveal home content.
3. Login, register, reset password, change password and signed-in account: no opaque inner table; no touching buttons; fields/icons do not overlap; send code and password visibility remain usable; oversized forms scroll rather than clip.
4. Covers: no image beyond top rounded corners, including hovered/loading card states. Check fallback executable icons as well as full posters.
5. Header: search/update and caption controls remain wholly visible. Navigate away from Magpie using every top tab.
6. Configuration/download progress and restore/advanced actions continue to behave correctly. Do not treat artwork as proof of a successful installation.

## Hover / navigation regression

- Hover backgrounds paint explicit ancestor surfaces without cached native control rendering. Both custom button classes clear ButtonBase's inherited Opaque flag so their background painter actually runs.
- Card lift invalidates both old and new bounds, and the card-emphasis timer stops once it settles (including while hovered).
- Page switches no longer move the entire HWND tree, recolor every control or reapply native themes. Visibility/layout is changed in one batch.
- Viewing tabs/search uses the currently online, heartbeat-verified feature snapshot. Configuration, restore, launch, update and other privileged actions retain live server authorization. No feature is granted by the navigation change.
- Home-to-library navigation uses a single settled page switch. Per-frame native scrolling was removed because it exposes intermediate child-window paints. Local button/card motion remains.

On Windows, move rapidly across buttons/cards for 20 seconds, then keep the pointer stationary for 10 seconds: no accumulated ghost image or continuously high hover CPU is acceptable. Repeat fast Home → Library → Magpie → Account → Home clicks while the scroll animation is active. The last selected page must remain selected and fill the content area. Repeat at 125%/150% DPI, after resize, and after changing themes. Verify offline/login-expired state still routes to account and actual install/launch requests still authorize with the server.
