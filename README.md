# OmniClass room classification for Revit

Classifies Revit rooms against **OmniClass Table 13 (Spaces by Function)** by matching the
room name people actually typed to a curated list of aliases, so `Restroom`, `RestRoom`,
`Rest_Room`, `RR` and `R Room` all resolve to `13-23 17: Restroom` without anyone picking
from a list of thousands.

**Current state:** the matching engine and a Revit add-in are both in the repo. The add-in
lands on the **Arch Tools** ribbon, in a panel named **Room Data**, with Classify Rooms and
Harvest Names. See [Install the add-in](#install-the-add-in).

## Why the spreadsheet layout changed

The original sheet put each alias in its own column (`D`, `E`, `F`, `G`, ...). That works as
a thing to type into, but not as a thing to look up: the number of variants is unbounded, so
columns grow forever; nothing can be indexed; and nothing stops the same alias appearing on
two rows with two different numbers.

So the sheet stays wide for authoring and is **unpivoted into one row per alias on load**.
Authors keep the layout they like, and the lookup gets a unique index and conflict detection.

Two columns also turned out to be unnecessary:

- **The level column is derived.** `13-23 17` is level 3 and `13-23 17 11` is level 4, which
  you can count off the number itself. It is cross-checked, never trusted.
- **Most alias cells are punctuation, not synonyms.** `Restroom`, `RestRoom` and `Rest_Room`
  are one word with three habits. Normalization collapses them for free, so the dictionary
  only has to carry real synonyms like `RR`, `Toilet` and `WC`.

On the four rows from the original screenshot, 8 of the 16 authored alias cells were
redundant once normalized. `validate` points at each one by cell reference:

```
F2  RedundantAlias  'RestRoom' already matches this classification via 'Restroom' (B2);
                     both normalize to 'RESTROOM'. This cell can be deleted.
```

That is the difference between curating a list that converges and one that never ends.

## Normalization

Every comparison happens on a normalized key, never on raw text:

| Room name | Key |
| --- | --- |
| `Restroom`, `RestRoom`, `Rest_Room`, `REST-ROOM` | `RESTROOM` |
| `Restroom 101`, `RESTROOM-2`, `Restroom 101A` | `RESTROOM` |
| `Men's Restroom`, `Mens Restroom`, `Men's-Restroom` | `MENSRESTROOM` |
| `RR` | `RR` |
| `101` | *(nothing — reported as unmatched rather than guessed at)* |

Casing, punctuation, apostrophes, accents, camel case and trailing room numbers all come out
in the wash. Runs of initials like `RR` survive intact, because those are real aliases.

## Matching

Three tiers, and only the first one is ever written to a model unattended:

| Status | Meaning | Applied |
| --- | --- | --- |
| `Exact` | Normalized name is in the dictionary | Yes |
| `Probable` | One strong fuzzy candidate, e.g. `Restrooms` | Only after review |
| `Ambiguous` | Several plausible candidates, or one weak one | Only after review |
| `Unmatched` | Nothing close | No — goes to the harvest report |

A wrong OmniClass number is worse than a blank one, because it flows into schedules and IFC
exports where nobody checks it again. So the guardrails matter more than the hit rate:

- **No substring matching, ever.** Matching is whole-token or whole-string, so `RR` cannot
  classify a corridor and `WR` cannot classify a work room.
- **The fuzzy tier needs real overlap** — a shared whole word, or a spelling within two
  edits. Ratio alone is too loose: `BREAKROOM` is three edits from `RESTROOM`, which scores
  a respectable 0.67 and is a completely different room.
- **The specific classification wins.** `Restroom Mens` resolves to `13-23 17 11`, not the
  generic `13-23 17`, and word order does not matter.
- **Genuine ties come back ambiguous.** `E Room` sits equally close to `R Room`, `M Room` and
  `W Room`, so it is handed to a human instead of guessed.
- **Bare numbers are rejected as aliases**, so rooms are never classified by room number.

## The workflow

The dictionary is the product, and it is grown from evidence rather than imagination:

1. **Harvest.** Export room schedules from finished models and run `audit`. You get every
   distinct room name ranked by how often it really occurs.
2. **Curate.** Add the frequent unmatched names to the sheet. The top hundred rows will cover
   most rooms on most jobs.
3. **Classify.** Apply exact hits, review the rest.
4. **Repeat.** Every project's unmatched list makes the next project better. That feedback
   loop is the whole point.

## Try it without Revit

Export a room schedule to CSV and point the CLI at it. This is how to judge the approach on
your own naming habits before committing to an add-in.

```bash
dotnet run --project tools/OmniClass.Cli -- validate data/room_aliases.csv
dotnet run --project tools/OmniClass.Cli -- audit data/sample_room_schedule.csv data/room_aliases.csv
dotnet run --project tools/OmniClass.Cli -- classify rooms.csv data/room_aliases.csv --out report.csv
```

`audit` against the bundled sample, using a dictionary that only covers restrooms:

```
30 rooms, 25 distinct names after normalizing.

  Exact          17   56.7%  (applied unattended)
  Probable        1    3.3%  (needs a human)
  Ambiguous       1    3.3%  (needs a human)
  Unmatched      11   36.7%  (needs a human)

Most common names with no classification - add these to the sheet first:
       2  Corridor
       1  Break Room
       1  Conference 101
```

Room names are read from the column whose header contains `Name`, or the first column.
Use `--column` to choose explicitly.

## The dictionary file

`data/room_aliases.csv` — Number and Name in columns A and B, then each room-name
option in C, D, E, … so the sheet grows like a small database:

```csv
Number,Name,Room Name 1,Room Name 2,Room Name 3,Room Name 4,Room Name 5,Room Name 6
13-23 17,Restroom,RR,R Room,RestRoom,Rest_Room,Toilet,WC
13-23 17 11,Men's Restroom,MR,M Room,Men's Restroom,Men's-Restroom,Mens Toilet,Gents
```

Harvest Names writes this same layout, so you can paste new spellings into the next
empty column. An older sheet that still has a level digit in column C is still accepted.

The title is always an alias whether or not it is repeated in an alias column.

Two things to watch:

- **Format the number column as Text.** Excel will otherwise turn `13-23 17` into a date.
  The loader detects that and says so rather than failing quietly.
- **The bundled data is a seed, not the official table.** The four restroom rows come from
  the original screenshot; the extra synonyms are suggestions. Verify numbers and titles
  against the official OmniClass Table 13 release before using this on deliverables.

`validate` grades problems as errors (row dropped), warnings (loads, but suspicious) or info
(redundant cells you can delete), each with a spreadsheet cell reference.

## Layout

```
src/OmniClass.Core     Matching engine. netstandard2.0, no Revit references.
src/OmniClass.Revit    Add-in: Arch Tools > Room Data. net48 (2023-24) and net8 (2025-26).
tools/OmniClass.Cli    validate / audit / classify against schedule exports.
install/               .addin manifest and PowerShell installer.
data/                  The alias dictionary and a sample room schedule.
tests/                 Unit tests, including "the shipped dictionary has no errors".
```

`OmniClass.Core` targets `netstandard2.0` so one binary loads under both .NET Framework 4.8
(Revit 2023–2024) and .NET 8 (Revit 2025+).

## Install the add-in

You do not need the repo, Visual Studio, or Terminal.

1. Download the zip for your Revit year from [`install/packages/`](install/packages):
   - [OmniClassRooms-Revit2023-2024.zip](install/packages/OmniClassRooms-Revit2023-2024.zip) for Revit 2023 or 2024
   - [OmniClassRooms-Revit2025-2026.zip](install/packages/OmniClassRooms-Revit2025-2026.zip) for Revit 2025 or 2026
2. Unzip it. You will see `OmniClass.Rooms.addin` and a folder named `OmniClassRooms`.
3. In File Explorer paste this into the address bar and press Enter:
   `%AppData%\Autodesk\Revit\Addins`
4. Open your Revit year folder (`2024`, `2025`, …). Create it if it is missing.
5. Copy **both** items into that year folder.
6. Close Revit completely, open it, open a **project**, and look for **Arch Tools → Room Data**.

A `HOW-TO-INSTALL.txt` file is also inside each zip.

To rebuild the zips after changing code: `dotnet build -c Release` then `.\install\Pack-InstallZips.ps1`.

## What the add-in writes

Classify writes the OmniClass number and name onto the Arch template parameters:

- `Classification.Space.Number` — e.g. `13-23 17 11`
- `Classification.Space.Description` — e.g. `Men's Restroom`
- `COBie.Space.Category` — both together, e.g. `13-23 17 11: Men's Restroom`

If those parameters already exist they are reused. They are only created when the
project does not already have them. Names are configurable in `OmniClass.Rooms.config`.

The classify preview pre-selects only exact dictionary hits. Probable and ambiguous matches
are listed for review. Unplaced rooms, not-enclosed rooms, rooms owned by another user, and
rooms that already have a value are left alone unless `overwriteExisting = true`. The whole
write is one undo.

Dictionary version stamping is not in yet: a later pass should record which dictionary
classified the model so a correction can find the rooms it touched.

## Upstream fix

The same dictionary can feed a **Room key schedule** in the project template, making
classification a dropdown at room-creation time. New projects then never need matching at
all, and this tool handles legacy models, consultant models and cleanup — a much smaller
problem than every room on every job.
