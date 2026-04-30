# Deck Curve Viewer

BepInEx 6 IL2CPP plugin for Vampire Crawlers.

## What it does

- Adds a `Deck Curve` button to the bottom-left in-game GUI when the player has cards.
- Shows a compact mana curve graph above the button.
- Shows all cards in the current player deck collection, grouped by cost and name.
- Sorts cards by mana cost, then card name.
- Shows a simple mana curve count for each cost.

## Install

1. Install BepInEx 6 IL2CPP x64 for Vampire Crawlers.
2. Launch the game once so BepInEx creates its folders.
3. Copy `dist/DeckCurveViewer.dll` to `BepInEx/plugins/DeckCurveViewer.dll`.
4. Launch the game.

The button appears at the bottom-left of the screen as `Deck Curve` once the player has at least one card.

## Build

```powershell
dotnet build .\DeckCurveViewer.csproj -c Release
```

The compiled DLL is written to `bin/Release/net6.0/DeckCurveViewer.dll`.
