# Release checklist

Four things no test can answer, because each is a question about what is on screen, or
about which window something ended up in.

Both bugs found on a Mac in September 2026 were of exactly that kind: the code was
right, the tests were green, and the control was in a window the operating system had
put somewhere the user could not see it. Neither took a minute to spot by hand, and
nothing in CI could have spotted either.

Run through this on a real machine before publishing a release, with a film to hand.

## macOS

- [ ] **Idle screen.** Open Luma with nothing loaded. The mark and the prompt are
      centred in the black area from the first frame — not low, not off to one side.
      Drag the window about quickly: they move with it rather than lurching after it.
- [ ] **Fullscreen controls.** Play something and go fullscreen. The transport bar
      hides after three seconds and comes back when the mouse moves. Seeking works
      without leaving fullscreen.
- [ ] **Right-click over the picture.** The menu opens — over a playing film as well as
      over the idle screen — and choosing something from it has an effect.
- [ ] **Drag and drop.** A file dropped on the window plays. Holding <kbd>Shift</kbd>
      appends to the playlist instead of replacing it.

## Windows

The same four, plus the one thing only an installer can show:

- [ ] **The MSI installs, and the installed application starts.** CI builds the MSI and
      nothing runs it, so a release that installs into an unusable state would ship with
      every check green.

## Both

- [ ] **Open a film from the file manager** — Finder's *Open With*, Explorer's
      double-click. Luma is offered, and the film it was handed is the one that plays.
