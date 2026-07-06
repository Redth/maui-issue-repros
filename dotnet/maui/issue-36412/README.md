# dotnet/maui#36412 — iOS "Done" keyboard accessory is full-width and blocks taps on the Entry above the keyboard

- **Repo:** [dotnet/maui](https://github.com/dotnet/maui)
- **Issue:** [#36412](https://github.com/dotnet/maui/issues/36412)

## The bug

On iOS, MAUI auto-attaches a "Done" keyboard accessory to every `Entry`/`Editor`
(`TextFieldExtensions.AddMauiDoneAccessoryView`, added in dotnet/maui#24645). It
is a **full-width, transparent `UIToolbar`** set as the field's
`InputAccessoryView`.

Because it's transparent, the text field that sits **directly above the
on-screen keyboard shows through it** — but the accessory still **captures
touches across its entire width and 44pt height**. So that field can't be
tapped/focused: the tap lands on the (mostly empty) accessory, not the field
behind it. Fields higher up on the page focus fine.

The iOS accessibility tree shows the full-width accessory over the field:

```
{ AXUniqueId: "Toolbar", AXFrame: {{0, 522}, {402, 44}} }   // width = full screen
```

## Expected

Tapping a visible text field just above the keyboard focuses it (only the small
"Done" button area should be interactive).

## Actual

The field is fully visible but **un-tappable** — the transparent full-width Done
accessory eats the touch. Only the far-right "Done" button responds.

## Repro project

Minimal `dotnet new maui` app pinned to `net10.0-ios` + `Microsoft.Maui.Controls`
**10.0.60**. [`MainPage.xaml`](MauiDoneAccessoryRepro/MainPage.xaml) is a
`ScrollView` of 15 numeric `Entry`s (numeric keyboards have no Return key, so the
Done accessory is always shown).

### Build & run

```bash
cd MauiDoneAccessoryRepro
dotnet build -f net10.0-ios -t:Run -p:_DeviceName=:v2:udid=<simulator-udid>
```

Then:

1. Tap **Field 1**. The numeric keyboard + transparent Done accessory appear.
2. Try to tap the field just above the keyboard (e.g. **Field 7** on iPhone),
   anywhere except the far-right Done button. It will **not** focus.
3. Tap a field higher up (fully above the accessory) — it focuses normally.

## Workaround

Replace the accessory on each focus with a full-width, clear `InputAccessoryView`
that hit-tests only over the Done button and returns `null` elsewhere, so touches
fall through to the field behind it. See the issue for the snippet.
