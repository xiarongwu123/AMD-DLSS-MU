# Rendering regression: Windows verification required

This revision removes cached native parent painting. It draws only explicit
background layers and artwork, with GDI text preserving clipping/translation.
Explorer themes are no longer assigned to owner-drawn controls. Press effects
do not scale GDI text independently of GDI+ geometry. Page switches flush one
complete settled frame. The banner title uses bright ink in both themes.

Build: file version 2.0.0.3, `artifacts/button-background-fix-win-x64`.
MU ICO contains 16/20/24/32/40/48/64/128/256 pixel PNG entries. The icon is both
the Win32 application icon and an embedded resource assigned to MainForm.

Windows checks (not executed on the macOS build host):

- Run `dotnet run --project tests-ui/rendering/Rendering.Tests.csproj -c Release`
  on Windows. This checks the inherited Opaque flag, actual HWND bitmap corner
  pixels and repeated hover/text repaint. ButtonBase enables Opaque by default;
  both custom buttons now disable it so WM_PAINT executes their backgrounds.
- Home-to-library navigation now changes pages once rather than animating native
  AutoScrollPosition. Hover/press animations remain local. Both card grids reserve
  a six-logical-pixel image gutter for the hover border.

- At 100%, 125%, 150%, 200% DPI, repeatedly hover every navigation/filter tab.
  Text must remain unique to its tab, with no overprinted words or stale fill.
- Rapidly switch home/library/Magpie/account for 30 cycles in each theme.
  No horizontal strips, image fragments, or stale previous-page controls.
- Hover and press the hero buttons on light and dark themes. Round corners
  reveal the correct banner artwork, not white/black squares.
- Scroll an unpaginated library in both directions, resize, minimize/restore,
  and switch themes while scrolled. No residual scroll-window pixels.
- Verify bright banner title, clear light-mode button text, and small icon
  legibility. Inspect Explorer EXE icon, taskbar, Alt+Tab, and file version.

Compilation and portable management assertions are not a substitute for these
Windows/GDI rendering checks. Do not report this checklist as passed from a
non-Windows host.
